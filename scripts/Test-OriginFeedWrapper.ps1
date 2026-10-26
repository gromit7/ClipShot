# Offline receipt checks. The temporary executor never runs its Command.
$ErrorActionPreference='Stop'
$publisher=Join-Path $PSScriptRoot 'Publish-OriginFeed.ps1'
$base=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'ClipShot.Tests'))
$fixture=Join-Path $base ('origin-feed-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force|Out-Null
$executor=Join-Path $fixture 'executor.ps1';$feedPath=Join-Path $fixture 'feed.json';$control=Join-Path $fixture 'mode.txt'
$oldExit=$global:LASTEXITCODE
$fake=@'
param([string]$Command)
$ErrorActionPreference='Stop'
$request=[regex]::Match($Command,"^python3 - '(check|publish)' '([A-Za-z0-9+/=]+)' '([0-9a-f]{32})' <<")
if(-not $request.Success){throw 'Unexpected fixture command'}
Add-Content -LiteralPath (Join-Path $PSScriptRoot 'calls.txt') -Value 'called'
$raw=[Convert]::FromBase64String($request.Groups[2].Value)
$feed=[Text.Encoding]::UTF8.GetString($raw)|ConvertFrom-Json
$sha=[Security.Cryptography.SHA256]::Create()
try{$hash=([BitConverter]::ToString($sha.ComputeHash($raw))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
$packages=@()
foreach($release in @($feed.stable,$feed.variants.win10,$feed.variants.win11,$feed.rollback.release,$feed.rollback.variants.win10,$feed.rollback.variants.win11)){
 if($null -eq $release){continue};$asset=$release.assets[0];$version=$release.tag_name.Substring(1)
 $packages+=@{version=$version;name=$asset.name;size=$asset.size;sha256=$asset.digest.Substring(7).ToLowerInvariant();verified=$true;url="https://bot.data-clouds.ru/clipshot/v$version/$($asset.name)"}
}
$record=@{verification='clipshot-origin-feed-v1';mode=$request.Groups[1].Value;request_id=$request.Groups[3].Value;feed_sha256=$hash;current_sha256=$hash;bytes=$raw.Length;current_matches_input=$true;packages_count=$packages.Count;packages=@($packages);atomic_replace=($request.Groups[1].Value -eq 'publish');backup_id='fixture'}
$response=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'mode.txt') -Raw).Trim()
switch($response){
 'wrong-sha'{$record.feed_sha256='0'*64}
 'wrong-count'{$record.packages_count++}
 'wrong-request'{$record.request_id='0'*32}
 'foreign-package'{$record.packages[0].url='https://evil.invalid/package.exe'}
 'unverified-package'{$record.packages[0].verified=$false}
 'check-mutated'{$record.atomic_replace=$true}
}
if($response -ne 'missing-ssh'){if($response -eq 'bad-ssh'){'ssh_exit 1'}else{'ssh_exit 0'}}
$json=$record|ConvertTo-Json -Depth 8 -Compress
$json
if($response -eq 'duplicate-receipt'){$json}
'@
[IO.File]::WriteAllText($executor,$fake,[Text.UTF8Encoding]::new($true))
$count=0
function Assert-Rejected([string]$Mode,[switch]$CheckOnly){
 Set-Content -LiteralPath $control -Value $Mode
 $failed=$false
 try{& $publisher -FeedPath $feedPath -RemoteExecutorPath $executor -CheckOnly:$CheckOnly|Out-Null}catch{$failed=$true}
 if(-not $failed){throw ('Accepted invalid receipt: '+$Mode)}
 $script:count++
}
try{
 $variants=@{}
 foreach($flavor in 'legacy','win10','win11'){
  $prefix=switch($flavor){'legacy'{'ClipShot'};'win10'{'ClipShot-Win10'};'win11'{'ClipShot-Win11'}}
  $name="$prefix-Setup-9.8.7.exe"
  $variants[$flavor]=@{draft=$false;prerelease=$false;tag_name='v9.8.7';assets=@(@{name=$name;size=4096;digest=('sha256:'+('a'*64));browser_download_url="https://github.com/gromit7/ClipShot/releases/download/v9.8.7/$name"})}
 }
 $feed=@{schema=1;stable=$variants.legacy;rollback=$null;variants=@{win10=$variants.win10;win11=$variants.win11}}|ConvertTo-Json -Depth 8 -Compress
 [IO.File]::WriteAllText($feedPath,$feed,[Text.UTF8Encoding]::new($false));Set-Content -LiteralPath $control -Value 'valid'
 $global:LASTEXITCODE=17
 $receipt=& $publisher -FeedPath $feedPath -RemoteExecutorPath $executor|ConvertFrom-Json
 if($receipt.packages_count -ne 3 -or $receipt.atomic_replace -ne $true -or $LASTEXITCODE -ne 17){throw 'Valid receipt or caller exit-state restoration failed'};$count++
 $receipt=& $publisher -FeedPath $feedPath -RemoteExecutorPath $executor -CheckOnly|ConvertFrom-Json
 if($receipt.mode -cne 'check' -or $receipt.atomic_replace -ne $false){throw 'Check-only receipt failed'};$count++
 foreach($mode in 'wrong-sha','wrong-count','wrong-request','foreign-package','unverified-package','missing-ssh','bad-ssh','duplicate-receipt'){Assert-Rejected $mode}
 Assert-Rejected 'check-mutated' -CheckOnly
 [IO.File]::WriteAllText($feedPath,'{"schema":1,"stable":null,"rollback":null}',[Text.UTF8Encoding]::new($false));Set-Content -LiteralPath $control -Value 'valid'
 $receipt=& $publisher -FeedPath $feedPath -RemoteExecutorPath $executor|ConvertFrom-Json
 if($receipt.packages_count -ne 0 -or @($receipt.packages).Count -ne 0){throw 'Null revoke receipt failed'};$count++
 $before=@(Get-Content -LiteralPath (Join-Path $fixture 'calls.txt')).Count
 [IO.File]::WriteAllBytes($feedPath,(New-Object byte[] 32769));Assert-Rejected 'valid'
 if(@(Get-Content -LiteralPath (Join-Path $fixture 'calls.txt')).Count -ne $before){throw 'Oversized feed reached executor'}
 Write-Output ("PASS $count origin wrapper checks; no network or SSH; PowerShell "+$PSVersionTable.PSVersion)
}finally{
 $global:LASTEXITCODE=$oldExit
 $resolved=[IO.Path]::GetFullPath($fixture)
 if([IO.Path]::GetDirectoryName($resolved) -cne $base -or -not([IO.Path]::GetFileName($resolved)).StartsWith('origin-feed-')){throw 'Unsafe fixture cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
