param(
    [Parameter(Mandatory=$true)][string]$Setup,
    [string]$OldSetup=(Join-Path (Split-Path $PSScriptRoot) 'ClipShot-Setup-0.7.4.exe'),
    [string]$Publication=(Join-Path (Split-Path $PSScriptRoot) 'ClipShot-app'),
    [switch]$NestedActualApp
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'TestWorkspace.ps1')
$fixtureScope=New-ClipShotTestScope
$fixtureGate=$null
try {
    $fixtureGate=Enter-ClipShotTestGate

$oldHash='e9f1be64b9e3dcb01768b8b51261ae1d2a2d59e5ef16c8969cea16fecb95ff74'
if(-not(Test-Path -LiteralPath $OldSetup) -or (Get-FileHash -LiteralPath $OldSetup).Hash.ToLowerInvariant() -ne $oldHash){throw 'A verified official 0.7.4 legacy installer is required for the migration regression.'}
$root=New-ClipShotTestRoot $fixtureScope
$appDir=Join-Path $root 'App';$app=Join-Path $appDir 'ClipShot.exe'
$updates=Join-Path $root 'Updates';$data=Join-Path $root 'Data'
New-Item -ItemType Directory -Path $root,$updates -Force | Out-Null
$saved=$env:CLIPSHOT_TEST_ROOT
function Run([string]$File,[string]$Arguments,[string]$Cwd,[int]$Expected=0){
    $process=Start-Process -FilePath $File -ArgumentList $Arguments -WorkingDirectory $Cwd -PassThru -WindowStyle Hidden
    Register-ClipShotTestProcess $fixtureScope $process
    if(-not $process.WaitForExit(180000)){$process.Kill();throw 'Isolated migration process timed out'}
    if($process.ExitCode -ne $Expected){throw ('Unexpected isolated migration exit: '+$process.ExitCode+' expected '+$Expected)}
}
function Stop-TestApp {
    $own=@(Get-CimInstance Win32_Process -Filter "Name='ClipShot.exe'" | Where-Object {$_.ExecutablePath -eq $app})
    if($own.Count -eq 0){return}
    Run $app '--smoke-test --quit' $updates
    foreach($item in $own){$process=Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue;if($process -and -not $process.WaitForExit(10000)){$process.Kill();throw 'Isolated migration app did not quit'}}
}
try{
    $env:CLIPSHOT_TEST_ROOT=$data
    Run $OldSetup ('/S /TESTROOT="'+$root+'"') $updates
    if($NestedActualApp){
        # Default INSTDIR points at App, while the actual process lives in App/Actual.
        # This reproduces a stale registry destination without changing the real registry.
        $temporary=Join-Path $root 'RelocateApp'
        $actual=Join-Path $appDir 'Actual'
        foreach($target in @($appDir,$temporary,$actual)){if(-not([IO.Path]::GetFullPath($target)).StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe fixture relocation'}}
        [IO.Directory]::Move($appDir,$temporary);New-Item -ItemType Directory -Path $appDir | Out-Null
        [IO.Directory]::Move($temporary,$actual);$appDir=$actual;$app=Join-Path $actual 'ClipShot.exe'
    }
    Run $app '--upgrade-self-test' $updates
    $ready=Join-Path $data 'upgrade-checks.txt'
    if((Get-Content -LiteralPath $ready -Raw) -notmatch '^PASS created'){throw 'Old synthetic history was not created'}
    $legacyRunnerDir=Join-Path $updates 'runner-legacy-0.7.4';New-Item -ItemType Directory -Path $legacyRunnerDir -Force | Out-Null
    $runner=Join-Path $legacyRunnerDir 'ClipShot.Updater.exe'
    Copy-Item -LiteralPath (Join-Path $appDir 'Updater/ClipShot.Updater.exe') -Destination $runner
    if((Get-Item -LiteralPath $runner).VersionInfo.ProductVersion.Split('+')[0] -ne '0.7.4'){throw 'Migration must exercise the actual released old updater'}
    $beforeVersion=(Get-Item -LiteralPath $app).VersionInfo.ProductVersion
    Run $Setup ('/S /TESTROOT="'+$root+'" /APPDIR="'+$appDir+'"') $updates 2
    if((Get-Item -LiteralPath $app).VersionInfo.ProductVersion -ne $beforeVersion){throw 'Untrusted APPDIR changed the old app'}
    $package=Join-Path $updates ([IO.Path]::GetFileName($Setup));Copy-Item -LiteralPath $Setup -Destination $package
    $hash=(Get-FileHash -LiteralPath $package).Hash
    $arguments='"'+$package+'" "'+$hash+'" "'+$app+'" "--test-root='+$root+'"'
    # Это существенная часть регрессии: старый runner удерживает App как CWD.
    # Без моста тот же официальный updater завершается exit1 и возвращает 0.7.4.
    $oldDelay=$env:CLIPSHOT_TEST_BRIDGE_WAIT_DELAY
    try{$env:CLIPSHOT_TEST_BRIDGE_WAIT_DELAY='1';Run $runner $arguments $appDir 4}
    finally{$env:CLIPSHOT_TEST_BRIDGE_WAIT_DELAY=$oldDelay}
    $log=Join-Path $updates 'updater-log.jsonl';$deadline=[DateTime]::UtcNow.AddSeconds(180);$done=$false
    while([DateTime]::UtcNow -lt $deadline){
        if(Test-Path -LiteralPath $log){
            $entries=@(Get-Content -LiteralPath $log | ForEach-Object {try{$_ | ConvertFrom-Json}catch{}})
            $complete=@($entries | Where-Object {$_.stage -eq 'complete' -and $_.mode -eq 'bridge'})
            if($complete.Count){if($complete[-1].result -ne 0){throw 'Bridge reported a failed transaction'};$done=$true;break}
        }
        Start-Sleep -Milliseconds 100
    }
    if(-not $done){throw 'Bridge did not report completion'}
    Start-Sleep -Milliseconds 1200
    $survivors=@(Get-CimInstance Win32_Process -Filter "Name='ClipShot.exe'" | Where-Object {$_.ExecutablePath -eq $app})
    if($survivors.Count -ne 1){throw 'Updated app did not survive updater completion'}
    $survivor=Get-Process -Id $survivors[0].ProcessId -ErrorAction Stop
    if($survivor.HasExited){throw 'Updated app exited after updater completion'}
    Stop-TestApp
    $expected=(Get-Item -LiteralPath (Join-Path $Publication 'ClipShot.exe')).VersionInfo.ProductVersion
    if((Get-Item -LiteralPath $app).VersionInfo.ProductVersion -ne $expected){throw 'Old updater did not install the new version'}
    $hashes=Get-Content -LiteralPath (Join-Path $Publication 'FileHashes.json') -Raw | ConvertFrom-Json -AsHashtable
    foreach($entry in $hashes.GetEnumerator()){if((Get-FileHash -LiteralPath (Join-Path $appDir $entry.Key)).Hash -ne $entry.Value){throw ('Mixed migration payload: '+$entry.Key)}}
    Run $app '--upgrade-self-test' $updates
    if((Get-Content -LiteralPath $ready -Raw) -notmatch '^PASS preserved'){throw 'Migration changed synthetic history or templates'}
    $entries=@(Get-Content -LiteralPath $log | ForEach-Object {try{$_ | ConvertFrom-Json}catch{}})
    if(-not($entries | Where-Object {$_.stage -eq 'health.ready'})){throw 'Migration skipped the real startup health handshake'}
    $noticeProof=Join-Path $data 'startup-notice-checks.txt'
    if(-not(Test-Path -LiteralPath $noticeProof) -or (Get-Content -LiteralPath $noticeProof -Raw) -notmatch '^PASS '){throw 'Background app did not survive the real update notice lifecycle'}
    $forbidden=Join-Path $root 'UpdatesOther';New-Item -ItemType Directory -Path $forbidden | Out-Null
    $badMarker=Join-Path $forbidden 'health.txt'
    Run $app ('--upgrade-self-test --background --update-health-test --update-health-file="'+$badMarker+'"') $updates
    if(Test-Path -LiteralPath $badMarker){throw 'Isolated health marker escaped its exact Updates directory'}
    $report='PASS official 0.7.4 updater with App CWD hands off after forced parent-exit delay, installs verified payload, survives updater exit and notice closure, preserves history/templates/settings'
    Set-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot) 'TestResults/legacy-updater-bridge-checks.txt') -Value $report
    $report
}finally{try{Stop-TestApp}finally{$env:CLIPSHOT_TEST_ROOT=$saved}}

} finally {
    try { Complete-ClipShotTestScope $fixtureScope }
    finally { if($fixtureGate){$fixtureGate.ReleaseMutex();$fixtureGate.Dispose()} }
}
