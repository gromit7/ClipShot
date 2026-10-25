param([string]$MakeNsis,[switch]$SkipBuild,[switch]$SkipDesktop,[switch]$PublishInstaller)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$reports=Join-Path $repo 'TestResults'
New-Item -ItemType Directory -Force -Path $reports | Out-Null
$results=[Collections.Generic.List[object]]::new()
function Step([string]$Name,[scriptblock]$Action){
    $watch=[Diagnostics.Stopwatch]::StartNew()
    try{& $Action;$results.Add([pscustomobject]@{Name=$Name;Passed=$true;Seconds=$watch.Elapsed.TotalSeconds});Write-Host "PASS $Name"}
    catch{$results.Add([pscustomobject]@{Name=$Name;Passed=$false;Seconds=$watch.Elapsed.TotalSeconds;Error=$_.Exception.Message});throw}
}
function Process-Checked([string]$File,[string]$Arguments,[int]$Timeout=45000){
    $process=Start-Process -FilePath $File -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    if(-not $process.WaitForExit($Timeout)){$process.Kill();throw "Test process timed out: $Arguments"}
    $process.Refresh();if($process.ExitCode -ne 0){throw "Test process exited $($process.ExitCode): $Arguments"}
}
$oldTestRoot=$env:CLIPSHOT_TEST_ROOT
try{
    if(-not $SkipBuild){Step 'Build, unit tests and publish' {& (Join-Path $PSScriptRoot 'Build.ps1') -Test -Publish}}
    $publication=Join-Path $repo 'ClipShot-app'
    $app=Join-Path $publication 'ClipShot.exe'
    $project=[xml](Get-Content -LiteralPath (Join-Path $repo 'ClipShot/ClipShot.csproj') -Raw)
    $version=[string]$project.Project.PropertyGroup.Version
    Step 'Publication contents and version' {
        foreach($name in 'ClipShot.exe','ClipShot.dll','ClipShot.pri','ClipShot.runtimeconfig.json','Updater/ClipShot.Updater.exe','FileInventory.json','LICENSE','Assets/ClipShot.ico','Assets/ClipShot.Muted.ico','Assets/ClipShot.png','Assets/Selection.cur'){
            if(-not(Test-Path -LiteralPath (Join-Path $publication $name))){throw "Missing $name"}
        }
        if((Get-Item -LiteralPath $app).VersionInfo.ProductVersion.Split('+')[0] -ne $version){throw 'Wrong published version'}
        if(Get-ChildItem -LiteralPath $publication -File -Recurse | Where-Object {$_.Extension -in '.db','.sqlite','.pfx','.key' -or $_.Name -like '.env*'}){throw 'Publication contains user data or secret files'}
    }
    if(-not $SkipDesktop){
        foreach($case in @(
            @{Name='ThemeGradient';Args='--smoke-test --theme=Gradient';Report='ui-ok.txt';Theme='Gradient'},
            @{Name='ThemeTransparent';Args='--smoke-test --theme=Transparent';Report='ui-ok.txt';Theme='Transparent'},
            @{Name='ThemeWhite';Args='--smoke-test --theme=White';Report='ui-ok.txt';Theme='White'},
            @{Name='ThemeBlack';Args='--smoke-test --theme=Black';Report='ui-ok.txt';Theme='Black'},
            @{Name='ThemeDark';Args='--smoke-test --theme=Dark';Report='ui-ok.txt';Theme='Dark'},
            @{Name='ThemeWindows98';Args='--smoke-test --theme=Windows98';Report='ui-ok.txt';Theme='Windows98'},
            @{Name='ThemeWindowsXP';Args='--smoke-test --theme=WindowsXP';Report='ui-ok.txt';Theme='WindowsXP'},
            @{Name='Search, templates, clear, focus and themes';Args='--interaction-self-test';Report='interaction-checks.txt'},
            @{Name='Synthetic selection, PNG and cancel';Args='--capture-self-test';Report='capture-checks.txt'}
            @{Name='Screenshot editor, arrows and export';Args='--editor-self-test';Report='editor-checks.txt'}
        )){
            Step $case.Name {
                $env:CLIPSHOT_TEST_ROOT=Join-Path ([IO.Path]::GetTempPath()) ('ClipShot.Tests/'+[guid]::NewGuid().ToString('N'))
                Process-Checked $app $case.Args
                $report=Join-Path $env:CLIPSHOT_TEST_ROOT $case.Report
                if(-not(Test-Path -LiteralPath $report)){throw 'Test report was not produced'}
                $text=Get-Content -LiteralPath $report -Raw
                Copy-Item -LiteralPath $report -Destination (Join-Path $reports $case.Report.Replace('.txt',('-'+($case.Name.Split(' ')[0])+'.txt'))) -Force
                if($text -notmatch '^PASS ' -or $text -match '(?m)^FAIL '){throw $text}
                if($case.Theme -and $text -notmatch ('ThemeId='+[regex]::Escape($case.Theme)+';')){throw 'Selected theme was not applied'}
                if($case.Theme -eq 'Transparent' -and $text -notmatch 'Acrylic=True'){throw 'Transparent theme is missing its desktop material'}
                if(Test-Path -LiteralPath (Join-Path $env:CLIPSHOT_TEST_ROOT 'test-error.txt')){throw 'Unhandled UI exception'}
            }
        }
    }
    if($MakeNsis){
        if($SkipDesktop -and $PublishInstaller){throw 'Desktop and installer checks must pass before publishing an installer.'}
        Step 'Compile versioned installer' {& (Join-Path $repo 'ClipShot/build-installer.ps1') -MakeNsis $MakeNsis -NoLatestCopy}
        $setup=Join-Path $repo "ClipShot-Setup-$version.exe"
        Step 'Compiled installer Russian strings' {
            $languageRoot=Join-Path ([IO.Path]::GetTempPath()) ('ClipShot.Tests/'+[guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $languageRoot -Force | Out-Null
            Process-Checked $setup ('/S /LANGCHECK /TESTROOT="'+$languageRoot+'"')
            $language=Get-Content -LiteralPath (Join-Path $languageRoot 'installer-language.txt') -Raw -Encoding unicode
            $expected="Установка и обновление ClipShot`r`nМастер установит ClipShot на этот компьютер. Если программа уже установлена, новая версия обновит её поверх старой. История буфера, шаблоны и настройки сохраняются. Предварительно удалять ClipShot не требуется."
            if($language -cne $expected){throw 'Compiled Russian welcome text is corrupted'}
            Copy-Item -LiteralPath (Join-Path $languageRoot 'installer-language.txt') -Destination (Join-Path $reports 'installer-language.txt') -Force
        }
        if(-not $SkipDesktop){
            Step 'Isolated install, running-app upgrade and uninstall' {
                $sandbox=Join-Path ([IO.Path]::GetTempPath()) ('ClipShot.Tests/'+[guid]::NewGuid().ToString('N'))
                New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
                $env:CLIPSHOT_TEST_ROOT=Join-Path $sandbox 'Data'
                $installDir=Join-Path $sandbox 'App'
                $installedApp=Join-Path $installDir 'ClipShot.exe'
                $arguments='/S /TESTROOT="'+$sandbox+'"'
                Process-Checked $setup $arguments 120000
                if(-not(Test-Path -LiteralPath $installedApp)){throw 'App not installed in isolated directory'}
                $running=Start-Process -FilePath $installedApp -ArgumentList '--upgrade-self-test --wait-for-upgrade --background' -PassThru -WindowStyle Hidden
                try{
                    $ready=Join-Path $env:CLIPSHOT_TEST_ROOT 'upgrade-checks.txt'
                    $limit=[DateTime]::UtcNow.AddSeconds(30)
                    while(-not(Test-Path -LiteralPath $ready)){
                        if($running.HasExited -or [DateTime]::UtcNow -gt $limit){throw 'Upgrade fixture did not start'}
                        Start-Sleep -Milliseconds 100
                    }
                    if((Get-Content -LiteralPath $ready -Raw) -notmatch '^PASS created'){throw 'Upgrade fixture failed'}
                    Process-Checked $setup $arguments 120000
                    if(-not $running.WaitForExit(10000)){throw 'Installer did not close running test app'}
                }finally{if(-not $running.HasExited){$running.Kill()}}
                Process-Checked $installedApp '--upgrade-self-test'
                if((Get-Content -LiteralPath $ready -Raw) -notmatch '^PASS preserved'){throw 'User data did not survive upgrade'}
                if((Get-FileHash -LiteralPath $installedApp).Hash -ne (Get-FileHash -LiteralPath $app).Hash){throw 'Installed binary differs from publication'}
                Copy-Item -LiteralPath $ready -Destination (Join-Path $reports 'upgrade-checks.txt') -Force
                # Exercise the actual external updater in an explicit sandbox. Its fallback may
                # never launch a normal app or send a production shutdown message in this mode.
                $updates=Join-Path $sandbox 'Updates';New-Item -ItemType Directory -Path $updates -Force | Out-Null
                $updateInstaller=Join-Path $updates ([IO.Path]::GetFileName($setup));Copy-Item -LiteralPath $setup -Destination $updateInstaller
                $runner=Join-Path $updates 'ClipShot.Updater.exe';Copy-Item -LiteralPath (Join-Path $publication 'Updater/ClipShot.Updater.exe') -Destination $runner
                $licensePath=Join-Path $installDir 'LICENSE';Set-Content -LiteralPath $licensePath -Value 'synthetic rollback marker'
                $updateHash=(Get-FileHash -LiteralPath $updateInstaller).Hash
                $updaterArgs='"'+$updateInstaller+'" "'+$updateHash+'" "'+$installedApp+'" "--test-root='+$sandbox+'"'
                # Блокировки не должны закрывать приложение или менять файлы при конкуренции.
                $lease=[IO.File]::Open((Join-Path $updates 'updater.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
                try{
                    $blocked=Start-Process -FilePath $runner -ArgumentList $updaterArgs -PassThru -WindowStyle Hidden
                    if(-not $blocked.WaitForExit(30000) -or $blocked.ExitCode -ne 4){throw 'Parallel updater was not deferred'}
                }finally{$lease.Dispose()}
                $setupLock=[Threading.Mutex]::new($false,'Local\ClipShot.Test.Setup.v1')
                try{
                    $blocked=Start-Process -FilePath $setup -ArgumentList $arguments -PassThru -WindowStyle Hidden
                    if(-not $blocked.WaitForExit(30000) -or $blocked.ExitCode -ne 4){throw 'Parallel installer was not deferred silently'}
                }finally{$setupLock.Dispose()}
                Process-Checked $runner $updaterArgs 180000
                if((Get-Content -LiteralPath $ready -Raw) -notmatch '^PASS preserved'){throw 'External updater did not restart and preserve isolated state'}
                Copy-Item -LiteralPath $ready -Destination (Join-Path $reports 'updater-restart-checks.txt') -Force
                if((Get-Content -LiteralPath $licensePath -Raw) -notmatch 'MIT License'){throw 'Updater did not replace the old shipped file'}
                $snapshot=Get-ChildItem -LiteralPath $updates -Directory -Filter 'backup-*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
                $rollbackArgs='--rollback "'+$snapshot.FullName+'" "'+$installedApp+'" "--test-root='+$sandbox+'"'
                Process-Checked $runner $rollbackArgs 120000
                if((Get-Content -LiteralPath $licensePath -Raw) -notmatch 'synthetic rollback marker'){throw 'Rollback did not restore the previous shipped file'}
                if((Get-Content -LiteralPath $ready -Raw) -notmatch '^PASS preserved'){throw 'Rollback changed user data'}
                $badInstaller=Join-Path $updates 'ClipShot-Setup-99.0.0.exe';[IO.File]::WriteAllBytes($badInstaller,[byte[]]::new(2048))
                $badHash=(Get-FileHash -LiteralPath $badInstaller).Hash
                $badArgs='"'+$badInstaller+'" "'+$badHash+'" "'+$installedApp+'" "--test-root='+$sandbox+'"'
                $failed=Start-Process -FilePath $runner -ArgumentList $badArgs -PassThru -WindowStyle Hidden
                if(-not $failed.WaitForExit(120000)){throw 'Failed update recovery timed out'}
                if($failed.ExitCode -ne 1){throw 'Invalid executable was not rejected'}
                if((Get-Content -LiteralPath $ready -Raw) -notmatch '^PASS preserved'){throw 'Failed update did not recover the application'}
                # Незавершённая копия не должна мешать перезапуску исходного приложения.
                $inventoryPath=Join-Path $installDir 'FileInventory.json';$inventoryText=[IO.File]::ReadAllText($inventoryPath)
                try{
                    $inventory=@($inventoryText|ConvertFrom-Json)+@('missing-synthetic-file.txt')
                    ConvertTo-Json -InputObject $inventory|Set-Content -LiteralPath $inventoryPath -Encoding utf8
                    $failed=Start-Process -FilePath $runner -ArgumentList $updaterArgs -PassThru -WindowStyle Hidden
                    if(-not $failed.WaitForExit(120000) -or $failed.ExitCode -ne 1){throw 'Incomplete backup was not rejected'}
                    if((Get-Content -LiteralPath $ready -Raw) -notmatch '^PASS preserved'){throw 'Incomplete backup stopped the application'}
                }finally{[IO.File]::WriteAllText($inventoryPath,$inventoryText)}
                Set-Content -LiteralPath (Join-Path $reports 'updater-rollback-checks.txt') -Value 'PASS update restart, rollback file restoration, failed package recovery and preserved synthetic database'

                $database=Join-Path $env:CLIPSHOT_TEST_ROOT 'clips.db' 
                $before=(Get-FileHash -LiteralPath $database).Hash
                $unrelated=Join-Path $installDir 'not-shipped-by-installer.txt'
                Set-Content -LiteralPath $unrelated -Value 'synthetic unrelated file'
                Process-Checked (Join-Path $installDir 'Uninstall.exe') ('/S _?='+$installDir) 120000
                if(Test-Path -LiteralPath $installedApp){throw 'Uninstaller left shipped app behind'}
                if(-not(Test-Path -LiteralPath $unrelated)){throw 'Uninstaller removed unrelated file'}
                if((Get-FileHash -LiteralPath $database).Hash -ne $before){throw 'Uninstaller changed user database'}
            }
        }
        if($PublishInstaller){
            Step 'Copy verified versioned installer' {
                & (Join-Path $PSScriptRoot 'Publish-Installer.ps1') -Setup $setup
            }
        }
    }
}finally{
    $env:CLIPSHOT_TEST_ROOT=$oldTestRoot
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $reports 'release-checks.json') -Encoding utf8
}
