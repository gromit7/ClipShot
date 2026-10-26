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
function Process-Checked([string]$File,[string]$Arguments,[int]$Timeout=45000,[int]$ExpectedExit=0){
    $process=Start-Process -FilePath $File -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    if(-not $process.WaitForExit($Timeout)){$process.Kill();throw "Test process timed out: $Arguments"}
    $process.Refresh();if($process.ExitCode -ne $ExpectedExit){throw "Test process exited $($process.ExitCode), expected ${ExpectedExit}: $Arguments"}
}
$oldTestRoot=$env:CLIPSHOT_TEST_ROOT
try{
    if(-not $SkipBuild){Step 'Build, unit tests and publish' {& (Join-Path $PSScriptRoot 'Build.ps1') -Test -Publish}}
    $publication=Join-Path $repo 'ClipShot-app'
    $app=Join-Path $publication 'ClipShot.exe'
    $project=[xml](Get-Content -LiteralPath (Join-Path $repo 'ClipShot/ClipShot.csproj') -Raw)
    $version=[string]$project.Project.PropertyGroup.Version
    Step 'Publication contents and version' {
        foreach($name in 'ClipShot.exe','ClipShot.dll','ClipShot.pri','ClipShot.runtimeconfig.json','Updater/ClipShot.Updater.exe','FileInventory.json','FileHashes.json','LICENSE','Assets/ClipShot.ico','Assets/ClipShot.Muted.ico','Assets/ClipShot.png','Assets/Selection.cur'){
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
            @{Name='ThemeCatPink';Args='--smoke-test --theme=CatPink';Report='ui-ok.txt';Theme='CatPink'},
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
        Step 'Synthetic OS package publication contracts' {& (Join-Path $PSScriptRoot 'Test-WindowsVariants.ps1')}
        Step 'Compile versioned installer' {& (Join-Path $repo 'ClipShot/build-installer.ps1') -MakeNsis $MakeNsis -NoLatestCopy}
        $setup=Join-Path $repo "ClipShot-Setup-$version.exe"
        $wrappers=@(
            @{Flavor='legacy';Path=$setup;Min=17763;Max=[int]::MaxValue},
            @{Flavor='win10';Path=(Join-Path $repo "ClipShot-Win10-Setup-$version.exe");Min=17763;Max=21999},
            @{Flavor='win11';Path=(Join-Path $repo "ClipShot-Win11-Setup-$version.exe");Min=22000;Max=[int]::MaxValue}
        )
        Step 'Installer OS compatibility matrix' {
            $lines=@(foreach($wrapper in $wrappers){
                foreach($build in 17762,17763,19041,21999,22000,26100){
                    $guardRoot=Join-Path ([IO.Path]::GetTempPath()) ('ClipShot.Tests/'+[guid]::NewGuid().ToString('N'))
                    New-Item -ItemType Directory -Path $guardRoot -Force | Out-Null
                    $allowed=[int]($build -ge $wrapper.Min -and $build -le $wrapper.Max)
                    $exit=if($allowed){0}else{2}
                    Process-Checked $wrapper.Path ('/S /OSCHECK /OSBUILD='+$build+' /TESTROOT="'+$guardRoot+'"') 45000 $exit
                    $line=(Get-Content -LiteralPath (Join-Path $guardRoot 'installer-oscheck.txt') -Raw -Encoding unicode).TrimEnd("`r","`n")
                    if($line -cne "Flavor=$($wrapper.Flavor); Build=$build; Allowed=$allowed"){throw "Incorrect OS guard report: $line"}
                    if(Test-Path -LiteralPath (Join-Path $guardRoot 'App')){throw 'OS guard test installed files.'}
                    "PASS $line; Exit=$exit"
                }
            })
            $lines | Set-Content -LiteralPath (Join-Path $reports 'installer-os-checks.txt') -Encoding utf8
        }
        foreach($wrapper in $wrappers){Step ('Compiled installer Russian strings '+$wrapper.Flavor) {
            $languageRoot=Join-Path ([IO.Path]::GetTempPath()) ('ClipShot.Tests/'+[guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $languageRoot -Force | Out-Null
            Process-Checked $wrapper.Path ('/S /LANGCHECK /TESTROOT="'+$languageRoot+'"')
            $language=Get-Content -LiteralPath (Join-Path $languageRoot 'installer-language.txt') -Raw -Encoding unicode
            $expected="Установка и обновление ClipShot`r`nМастер установит ClipShot на этот компьютер. Если программа уже установлена, новая версия обновит её поверх старой. История буфера, шаблоны и настройки сохраняются. Предварительно удалять ClipShot не требуется."
            if($language -cne $expected){throw 'Compiled Russian welcome text is corrupted'}
            Copy-Item -LiteralPath (Join-Path $languageRoot 'installer-language.txt') -Destination (Join-Path $reports ('installer-language-'+$wrapper.Flavor+'.txt')) -Force
        }}
        if(-not $SkipDesktop){
            Step 'Windows 11 wrapper install and upgrade' {
                # Это настоящий Win11 install-path, а не заявление о проверке UI Win10.
                $os=[Environment]::OSVersion.Version
                if($os.Build -lt 22000){throw 'Полная проверка Win11-пакета требует Windows 11.'}
                $win11=$wrappers | Where-Object Flavor -eq 'win11'
                $variantRoot=Join-Path ([IO.Path]::GetTempPath()) ('ClipShot.Tests/'+[guid]::NewGuid().ToString('N'))
                New-Item -ItemType Directory -Path $variantRoot -Force | Out-Null
                $variantArgs='/S /TESTROOT="'+$variantRoot+'"'
                Process-Checked $win11.Path $variantArgs 120000
                $variantDir=Join-Path $variantRoot 'App'
                $variantApp=Join-Path $variantDir 'ClipShot.exe'
                $savedRoot=$env:CLIPSHOT_TEST_ROOT
                try{
                    $env:CLIPSHOT_TEST_ROOT=Join-Path $variantRoot 'Data'
                    Process-Checked $variantApp '--upgrade-self-test'
                    $database=Join-Path $env:CLIPSHOT_TEST_ROOT 'clips.db'
                    $before=(Get-FileHash -LiteralPath $database).Hash
                    # Варианты должны обновляться друг поверх друга без смены
                    # схемы, путей данных, single-instance mutex и полезной нагрузки.
                    Process-Checked $setup $variantArgs 120000
                    Process-Checked $win11.Path $variantArgs 120000
                    if((Get-FileHash -LiteralPath $database).Hash -ne $before){throw 'Switching legacy to Win11 changed user data.'}
                    $manifest=Get-Content -LiteralPath (Join-Path $publication 'FileHashes.json') -Raw | ConvertFrom-Json -AsHashtable
                    foreach($entry in $manifest.GetEnumerator()){
                        if((Get-FileHash -LiteralPath (Join-Path $variantDir $entry.Key)).Hash -ne $entry.Value){throw ('Win11 payload differs from common publication: '+$entry.Key)}
                    }
                    Process-Checked $variantApp '--upgrade-self-test'
                    if((Get-Content -LiteralPath (Join-Path $env:CLIPSHOT_TEST_ROOT 'upgrade-checks.txt') -Raw) -notmatch '^PASS preserved'){throw 'Win11 upgrade lost synthetic state.'}
                    Process-Checked (Join-Path $variantDir 'Uninstall.exe') ('/S _?='+$variantDir) 120000
                    if(Test-Path -LiteralPath $variantApp){throw 'Win11 uninstall left app files.'}
                    Set-Content -LiteralPath (Join-Path $reports 'installer-win11-checks.txt') -Value 'PASS legacy to Win11 wrapper, identical publication, synthetic data preserved and uninstall'
                }finally{$env:CLIPSHOT_TEST_ROOT=$savedRoot}
            }
            Step 'Released 0.7.4 updater migration from App working directory' {
                $migrationSetup=($wrappers | Where-Object Flavor -eq 'win11').Path
                & (Join-Path $PSScriptRoot 'Test-LegacyUpdaterBridge.ps1') -Setup $migrationSetup -Publication $publication -NestedActualApp
            }
            Step 'Transactional install, locked DLL and verification rollback' {
                $transactionRoot=Join-Path ([IO.Path]::GetTempPath()) ('ClipShot.Tests/'+[guid]::NewGuid().ToString('N'))
                New-Item -ItemType Directory -Path $transactionRoot -Force | Out-Null
                $transactionDir=Join-Path $transactionRoot 'App'
                $transactionArgs='/S /TESTROOT="'+$transactionRoot+'"'
                Process-Checked $setup $transactionArgs 120000
                $unrelatedDir=Join-Path $transactionDir 'not-shipped/nested'
                New-Item -ItemType Directory -Path $unrelatedDir -Force | Out-Null
                $unrelatedPath=Join-Path $unrelatedDir 'keep.txt'
                Set-Content -LiteralPath $unrelatedPath -Value 'synthetic unrelated nested file'
                $dll=Join-Path $transactionDir 'ClipShot.dll'
                [IO.File]::AppendAllText($dll,'synthetic previous revision')
                $beforeTransaction=@{}
                Get-ChildItem -LiteralPath $transactionDir -File -Recurse | ForEach-Object {$beforeTransaction[[IO.Path]::GetRelativePath($transactionDir,$_.FullName)]=(Get-FileHash -LiteralPath $_.FullName).Hash}
                # Реальная блокировка старой DLL: запрет записи и удаления, а не mock.
                $locked=[IO.File]::Open($dll,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
                try{
                    $attempt=Start-Process -FilePath $setup -ArgumentList $transactionArgs -PassThru -WindowStyle Hidden
                    if(-not $attempt.WaitForExit(120000)){throw 'Locked-DLL installation timed out'}
                    if($attempt.ExitCode -notin 0,3){throw ('Unexpected locked-DLL setup result: '+$attempt.ExitCode)}
                    if($attempt.ExitCode -eq 3){
                        foreach($entry in $beforeTransaction.GetEnumerator()){
                            if((Get-FileHash -LiteralPath (Join-Path $transactionDir $entry.Key)).Hash -ne $entry.Value){throw ('Failed setup changed original file: '+$entry.Key)}
                        }
                    }else{
                        $expectedHashes=Get-Content -LiteralPath (Join-Path $publication 'FileHashes.json') -Raw | ConvertFrom-Json -AsHashtable
                        foreach($entry in $expectedHashes.GetEnumerator()){
                            if((Get-FileHash -LiteralPath (Join-Path $transactionDir $entry.Key)).Hash -ne $entry.Value){throw ('Locked DLL produced mixed installation: '+$entry.Key)}
                        }
                    }
                    $lockedResult=$attempt.ExitCode
                }finally{$locked.Dispose()}
                if((Get-Content -LiteralPath $unrelatedPath -Raw) -notmatch 'synthetic unrelated nested file'){throw 'Update lost an unrelated nested file'}
                Process-Checked $setup $transactionArgs 120000
                # Пакет с намеренно неверным хешем должен откатить уже скопированные файлы.
                $badPublication=Join-Path $transactionRoot 'BadPayload'
                New-Item -ItemType Directory -Path $badPublication -Force | Out-Null
                Copy-Item -Path (Join-Path $publication '*') -Destination $badPublication -Recurse -Force
                $badHashes=Get-Content -LiteralPath (Join-Path $badPublication 'FileHashes.json') -Raw | ConvertFrom-Json -AsHashtable
                $badHashes['ClipShot.dll']='0'*64
                $badHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $badPublication 'FileHashes.json') -Encoding utf8
                & $MakeNsis /V2 /WX /INPUTCHARSET UTF8 '/DVERSION=99.0.0' ("/DPUBLICATION_DIR="+$badPublication) (Join-Path $repo 'ClipShot/installer.nsi')
                if($LASTEXITCODE -ne 0){throw 'Cannot compile invalid-manifest fixture'}
                $badSetup=Join-Path $repo 'ClipShot-Setup-99.0.0.exe'
                $beforeTransaction=@{}
                Get-ChildItem -LiteralPath $transactionDir -File -Recurse | ForEach-Object {$beforeTransaction[[IO.Path]::GetRelativePath($transactionDir,$_.FullName)]=(Get-FileHash -LiteralPath $_.FullName).Hash}
                $attempt=Start-Process -FilePath $badSetup -ArgumentList $transactionArgs -PassThru -WindowStyle Hidden
                if(-not $attempt.WaitForExit(120000) -or $attempt.ExitCode -ne 3){throw 'Invalid manifest did not fail with the integrity error'}
                foreach($entry in $beforeTransaction.GetEnumerator()){
                    if((Get-FileHash -LiteralPath (Join-Path $transactionDir $entry.Key)).Hash -ne $entry.Value){throw ('Integrity failure changed original file: '+$entry.Key)}
                }
                Process-Checked (Join-Path $transactionDir 'Updater/ClipShot.Updater.exe') ('--verify-install "'+$transactionDir+'"')
                Set-Content -LiteralPath (Join-Path $reports 'installation-integrity-checks.txt') -Value ("PASS locked DLL leaves one complete version (exit $lockedResult), unrelated nested file preserved, invalid manifest restores all original files")
            }
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
                $manifest=Get-Content -LiteralPath (Join-Path $publication 'FileHashes.json') -Raw | ConvertFrom-Json -AsHashtable;foreach($entry in $manifest.GetEnumerator()){if((Get-FileHash -LiteralPath (Join-Path $installDir $entry.Key)).Hash -ne $entry.Value){throw ('Installed file differs: '+$entry.Key)}}
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
