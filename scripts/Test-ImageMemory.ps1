param([Parameter(Mandatory=$true)][string]$BaselineApp,[Parameter(Mandatory=$true)][string]$CandidateApp)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'TestWorkspace.ps1')
$scope=New-ClipShotTestScope
$gate=$null
$oldRoot=$env:CLIPSHOT_TEST_ROOT
$reports=Join-Path (Split-Path $PSScriptRoot) 'TestResults'
New-Item -ItemType Directory -Path $reports -Force | Out-Null
try {
    $gate=Enter-ClipShotTestGate
    $readings=@{}
    foreach($case in @(@{Name='baseline';App=$BaselineApp},@{Name='candidate';App=$CandidateApp})) {
        $env:CLIPSHOT_TEST_ROOT=New-ClipShotTestRoot $scope
        $process=Start-Process -FilePath ([IO.Path]::GetFullPath($case.App)) -ArgumentList '--smoke-test --memory-benchmark' -PassThru -WindowStyle Hidden
        Register-ClipShotTestProcess $scope $process
        if(-not $process.WaitForExit(180000)){$process.Kill();$process.WaitForExit();throw 'Memory benchmark timed out'}
        $report=Join-Path $env:CLIPSHOT_TEST_ROOT 'memory-checks.json'
        if(-not(Test-Path -LiteralPath $report)){throw 'Memory benchmark report is missing'}
        Copy-Item -LiteralPath $report -Destination (Join-Path $reports ('memory-'+$case.Name+'.json')) -Force
        $result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if($process.ExitCode -ne 0 -or -not $result.Passed){throw ('Memory benchmark failed: '+$case.Name+'; '+$result.Error)}
        $readings[$case.Name]=$result
    }
    $comparison=@(foreach($stage in 'empty','collapsed','open','preview','reopen-5','idle-hidden','hidden-after-gc') {
        $before=$readings.baseline.Samples | Where-Object Stage -eq $stage
        $after=$readings.candidate.Samples | Where-Object Stage -eq $stage
        [pscustomobject]@{Stage=$stage;BeforeWorkingSet=$before.WorkingSet;AfterWorkingSet=$after.WorkingSet;BeforePrivate=$before.PrivateBytes;AfterPrivate=$after.PrivateBytes;BeforeSourcePixels=$before.ReportedSourcePixels;AfterSourcePixels=$after.ReportedSourcePixels}
    })
    $comparison | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reports 'memory-comparison.json') -Encoding utf8
    $comparison | Format-Table -AutoSize
    $open=$comparison | Where-Object Stage -eq 'open'
    if($open.AfterPrivate -ge $open.BeforePrivate){throw 'Candidate did not reduce open-list private memory'}
} finally {
    $env:CLIPSHOT_TEST_ROOT=$oldRoot
    try{Complete-ClipShotTestScope $scope}finally{if($gate){$gate.ReleaseMutex();$gate.Dispose()}}
}
