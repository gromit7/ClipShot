$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'TestWorkspace.ps1')
$scope=New-ClipShotTestScope
$diagnostics=Join-Path (Split-Path $PSScriptRoot) ('.artifacts/cleanup-checks/'+$scope.Id)
$source=[ClipShot.Testing.TestWorkspace]::new($diagnostics)
$other=[ClipShot.Testing.TestWorkspace]::new()
$gate=$null
try {
    $sentinel=Join-Path $other.Root 'untouched.txt';Set-Content -LiteralPath $sentinel 'other running test'
    $link=Join-Path $source.Root 'outside-link'
    New-Item -ItemType Junction -Path $link -Target $other.Root | Out-Null
    $refused=$false;try{$source.Dispose()}catch{$refused=$true}
    if(-not $refused -or -not(Test-Path -LiteralPath $sentinel)){throw 'Junction protection failed'}
    # Удаляется только созданная нами ссылка, без рекурсии к её назначению.
    [IO.Directory]::Delete($link)
    Set-Content -LiteralPath (Join-Path $source.Root 'test-error.txt') 'synthetic failure diagnostic'
    $expected=(Get-FileHash -LiteralPath (Join-Path $source.Root 'test-error.txt')).Hash
    [IO.File]::WriteAllBytes((Join-Path $source.Root 'large-copy.dll'),[byte[]]::new(1048576))
    $source.Dispose()
    $report=Join-Path $diagnostics ((Split-Path $source.Root -Leaf)+'/test-error.txt')
    if((Get-FileHash -LiteralPath $report).Hash -ne $expected -or (Test-Path -LiteralPath $source.Root)){throw 'Archive or cleanup failed'}
    if(-not(Test-Path -LiteralPath $sentinel)){throw 'Another test root was removed'}
    Write-Output 'PASS junction refused, diagnostic copied with matching SHA, own payload removed, other root preserved'

    $owned=New-ClipShotTestRoot $scope
    try{Set-Content -LiteralPath (Join-Path $owned 'failure.log') 'synthetic';throw 'injected failure'}catch{}finally{Complete-ClipShotTestScope $scope}
    if(Test-Path -LiteralPath $owned){throw 'Failed test leaked its root'}
    Write-Output 'PASS finally cleans failed PowerShell test'

    $gate=Enter-ClipShotTestGate
    $probe=Join-Path $other.Root 'gate-check.ps1'
    $helper=(Join-Path $PSScriptRoot 'TestWorkspace.ps1').Replace("'","''")
    @(". '$helper'",'try{$g=Enter-ClipShotTestGate;$g.ReleaseMutex();$g.Dispose();exit 0}catch{exit 2}') | Set-Content -LiteralPath $probe -Encoding utf8
    $pwsh=(Get-Process -Id $PID).Path
    $p=Start-Process -FilePath $pwsh -ArgumentList ('-NoProfile -File "'+$probe+'"') -WindowStyle Hidden -PassThru
    if(-not $p.WaitForExit(20000) -or $p.ExitCode -ne 2){throw 'Parallel desktop suite was not rejected'}
    $gate.ReleaseMutex();$gate.Dispose();$gate=$null
    Write-Output 'PASS another process cannot enter the active desktop test suite'
    $liveScope=New-ClipShotTestScope
    $liveRoot=New-ClipShotTestRoot $liveScope
    $start=[Diagnostics.ProcessStartInfo]::new($pwsh,'-NoProfile -Command "Start-Sleep -Seconds 2"')
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.EnvironmentVariables['CLIPSHOT_TEST_ROOT']=$liveRoot
    $live=[Diagnostics.Process]::Start($start)
    Register-ClipShotTestProcess $liveScope $live
    $refused=$false;try{Complete-ClipShotTestScope $liveScope -WarningAction SilentlyContinue}catch{$refused=$true}
    if(-not $refused -or -not(Test-Path -LiteralPath $liveRoot)){throw 'Live environment-only test was not protected'}
    if(-not $live.WaitForExit(15000)){throw 'Synthetic sleep did not finish'}
    Complete-ClipShotTestScope $liveScope
    if(Test-Path -LiteralPath $liveRoot){throw 'Completed live test leaked data'}
    Write-Output 'PASS environment-only live test retained; cleanup succeeds after natural exit'
} finally {
    if($gate){$gate.ReleaseMutex();$gate.Dispose()}
    # Незавершившиеся процессы не завершаем: при открытых файлах helper откажет
    # в очистке и оставит путь для расследования.
    if(Test-Path -LiteralPath (Join-Path $source.Root 'outside-link')){[IO.Directory]::Delete((Join-Path $source.Root 'outside-link'))}
    $source.Dispose();$other.Dispose()
}
