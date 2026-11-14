$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'TestWorkspace.ps1')
. (Join-Path $PSScriptRoot 'CatalogPublication.ps1')
$scope=New-ClipShotTestScope
try {
    $root=New-ClipShotTestRoot $scope
    $executor=Join-Path $root 'digest-executor.ps1'
    $source=@'
param([string]$Command)
if($Command -notmatch "REQUEST_B64='([^']+)'"){throw 'Missing request'}
$request=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Matches[1]))|ConvertFrom-Json
$mode=Get-Content (Join-Path $PSScriptRoot 'mode.txt') -Raw
$record=@{verification='clipshot-https-digest-v1';request_id=$request.request_id;url=$request.url;size=$request.bytes;sha256=('a'*64);complete=$true}
switch($mode.Trim()){
 'url'{$record.url='https://foreign.invalid/file.exe'}
 'size'{$record.size=1}
 'sha'{$record.sha256='bad'}
 'request'{$record.request_id='stale'}
 'incomplete'{$record.complete=$false}
}
$record|ConvertTo-Json -Compress
if($mode.Trim() -eq 'duplicate'){$record|ConvertTo-Json -Compress}
if($mode.Trim() -ne 'missing-ssh'){Write-Output $(if($mode.Trim() -eq 'ssh-failed'){'ssh_exit 1'}else{'ssh_exit 0'})}
'@
    [IO.File]::WriteAllText($executor,$source)
    $url='https://bot.data-clouds.ru/clipshot/v1.2.3/ClipShot-1.2.3.exe'
    Set-Content (Join-Path $root 'mode.txt') 'valid'
    $result=Get-ClipShotTransportDigest $url 64 $executor
    if($result.Size -ne 64 -or $result.Sha256 -cne ('a'*64)){throw 'Valid full digest receipt rejected'}
    $count=1
    foreach($mode in 'url','size','sha','request','incomplete','duplicate','missing-ssh','ssh-failed') {
        Set-Content (Join-Path $root 'mode.txt') $mode
        $rejected=$false;try{Get-ClipShotTransportDigest $url 64 $executor|Out-Null}catch{$rejected=$true}
        if(-not $rejected){throw "Invalid digest accepted: $mode"};$count++
    }
    $rejected=$false;try{Get-ClipShotTransportDigest 'https://foreign.invalid/file.exe' 64 $executor|Out-Null}catch{$rejected=$true}
    if(-not $rejected){throw 'Foreign transport URL accepted'};$count++
    Write-Output "PASS $count full HTTPS digest transport contracts; no network or production calls"
} finally {Complete-ClipShotTestScope $scope}
