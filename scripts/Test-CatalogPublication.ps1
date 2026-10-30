$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'TestWorkspace.ps1')
. (Join-Path $PSScriptRoot 'CatalogPublication.ps1')
$scope=New-ClipShotTestScope
$previousExit=$global:LASTEXITCODE
try {
    $root=New-ClipShotTestRoot $scope
    $setup=Join-Path $root 'ClipShot-1.2.3.exe'
    $hook=Join-Path $root 'fake-hook.py'
    $receipt=Join-Path $root 'receipt.json'
    [IO.File]::WriteAllBytes($setup,[byte[]](1..64))
    [IO.File]::WriteAllText($hook,"import json, pathlib, sys`npathlib.Path(__file__).with_name('receipt.json').write_text(json.dumps(sys.argv[1:]), encoding='utf-8')`nsys.exit(int(pathlib.Path(__file__).with_name('exit.txt').read_text()))`n")
    Set-Content -LiteralPath (Join-Path $root 'exit.txt') -Value '0'
    $digest=(Get-FileHash -LiteralPath $setup).Hash.ToLowerInvariant()
    $script:official=@{draft=$false;prerelease=$false;published_at='2026-01-01T00:00:00Z';tag_name='v1.2.3';assets=@(@{name='ClipShot-1.2.3.exe';state='uploaded';size=64;digest="sha256:$digest";browser_download_url='https://github.com/gromit7/ClipShot/releases/download/v1.2.3/ClipShot-1.2.3.exe'})}
    $script:publicMode='valid'
    function Get-ClipShotCatalogRelease([string]$Version) { $script:official }
    function Get-ClipShotPublicPackageDigest([string]$Url,[long]$ExpectedSize) {
        if ($Url -cne 'https://bot.data-clouds.ru/clipshot/v1.2.3/ClipShot-1.2.3.exe' -or $ExpectedSize -ne 64) { throw 'Wrong public request' }
        if ($script:publicMode -eq 'error') { throw 'Synthetic HTTP 404' }
        if ($script:publicMode -eq 'changed') { [IO.File]::WriteAllBytes($setup,[byte[]](2..65)) }
        @{Size=$(if($script:publicMode -eq 'short'){63}else{64});Sha256=$(if($script:publicMode -eq 'bad-sha'){'0'*64}else{$digest})}
    }
    $argsForPublish=@{Version='1.2.3';Setup=$setup;Hook=$hook;Python='python'}
    $count=0
    function Assert-Rejected([string]$Case) {
        $rejected=$false
        try { Publish-ClipShotCatalog @argsForPublish | Out-Null } catch { $rejected=$true }
        if (-not $rejected -or (Test-Path -LiteralPath $receipt)) { throw "Unsafe acceptance or hook called: $Case" }
        $script:count++
    }
    Publish-ClipShotCatalog @argsForPublish -CheckOnly | Out-Null
    if(Test-Path -LiteralPath $receipt){throw 'CheckOnly invoked the hook'}; $count++
    foreach($field in 'draft','prerelease') { $official[$field]=$true; Assert-Rejected $field; $official[$field]=$false }
    $official.assets[0].digest='sha256:'+('0'*64); Assert-Rejected 'local SHA'; $official.assets[0].digest="sha256:$digest"
    $official.assets[0].browser_download_url='https://invalid.example/file.exe'; Assert-Rejected 'foreign URL'; $official.assets[0].browser_download_url='https://github.com/gromit7/ClipShot/releases/download/v1.2.3/ClipShot-1.2.3.exe'
    foreach($mode in 'short','bad-sha','error','changed') { $script:publicMode=$mode; Assert-Rejected $mode; [IO.File]::WriteAllBytes($setup,[byte[]](1..64)) }; $script:publicMode='valid'
    Publish-ClipShotCatalog @argsForPublish | Out-Null
    [string[]]$actual=Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
    $expected=@('--project','clipshot','--version','1.2.3','--file',$setup,'--channel','stable')
    if($actual.Count -ne $expected.Count -or ($actual -join [char]0) -cne ($expected -join [char]0)){throw 'Hook received incorrect arguments'}; $count++
    Set-Content -LiteralPath (Join-Path $root 'exit.txt') -Value '17'
    $failed=$false
    try { Publish-ClipShotCatalog @argsForPublish | Out-Null } catch { if($_.Exception.Message -like '*17*'){$failed=$true} }
    if(-not $failed){throw 'Nonzero hook exit was accepted'}; $count++
    Write-Output "PASS catalog post-release boundary: $count checks; no network or production calls."
} finally {
    try { Complete-ClipShotTestScope $scope } finally { $global:LASTEXITCODE=$previousExit }
}
