param(
 [string]$FeedPath=(Join-Path (Split-Path $PSScriptRoot) 'update-feed.json'),
 [string]$RemoteExecutorPath=$env:CLIPSHOT_MIRROR_EXECUTOR,
 [switch]$CheckOnly
)
$ErrorActionPreference='Stop'
if(-not $RemoteExecutorPath -or -not(Test-Path -LiteralPath $RemoteExecutorPath -PathType Leaf)){throw 'Set CLIPSHOT_MIRROR_EXECUTOR to the private configured executor.'}
$file=Get-Item -LiteralPath $FeedPath
if($file.PSIsContainer -or $file.Length -lt 1 -or $file.Length -gt 32768){throw 'Origin feed must contain 1..32768 UTF-8 bytes.'}
$raw=[IO.File]::ReadAllBytes($file.FullName)
$utf8=New-Object Text.UTF8Encoding -ArgumentList $false,$true
$local=$utf8.GetString($raw)|ConvertFrom-Json
if($local.schema -ne 1 -or -not($local.PSObject.Properties.Name -contains 'stable') -or -not($local.PSObject.Properties.Name -contains 'rollback')){throw 'Invalid origin feed schema.'}
$hasher=[Security.Cryptography.SHA256]::Create()
try{$digest=([BitConverter]::ToString($hasher.ComputeHash($raw))).Replace('-','').ToLowerInvariant()}finally{$hasher.Dispose()}
$expected=@()
function Add-ExpectedRelease($Release){
 if($null -eq $Release){return}
 $assets=@($Release.assets)
 if($assets.Count -ne 1 -or $Release.tag_name -cnotmatch '^v\d+\.\d+\.\d+$' -or $assets[0].digest -cnotmatch '^sha256:[0-9a-fA-F]{64}$'){throw 'Invalid origin package metadata.'}
 $script:expected+=([pscustomobject]@{version=$Release.tag_name.Substring(1);name=[string]$assets[0].name;size=$assets[0].size;sha256=$assets[0].digest.Substring(7).ToLowerInvariant()})
}
Add-ExpectedRelease $local.stable
if($local.variants){Add-ExpectedRelease $local.variants.win10;Add-ExpectedRelease $local.variants.win11}
if($local.rollback){Add-ExpectedRelease $local.rollback.release;if($local.rollback.variants){Add-ExpectedRelease $local.rollback.variants.win10;Add-ExpectedRelease $local.rollback.variants.win11}}
$expected=@($expected|Sort-Object version,name -Unique)
$python=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'origin_feed.py') -Raw -Encoding UTF8
$mode=if($CheckOnly){'check'}else{'publish'}
$requestId=[guid]::NewGuid().ToString('N')
$encoded=[Convert]::ToBase64String($raw)
# Команда содержит только публичные метаданные и собственный Python-код. Сеть и
# реквизиты принадлежат внешнему executor; Python никогда не обращается к GitHub.
$command="python3 - '$mode' '$encoded' '$requestId' <<'CLIPSHOT_ORIGIN_FEED'`n"+$python.Replace("`r`n","`n")+"`nCLIPSHOT_ORIGIN_FEED"
$oldExit=$global:LASTEXITCODE
try{$global:LASTEXITCODE=0;$output=@(& $RemoteExecutorPath -Command $command);$executorCode=$LASTEXITCODE}finally{$global:LASTEXITCODE=$oldExit}
if($executorCode -ne 0){throw 'Origin feed executor failed.'}
$text=($output|ForEach-Object {[string]$_}) -join [Environment]::NewLine
$ssh=[regex]::Matches($text,'(?m)^ssh_exit\s+([^\r\n]+)')
if($ssh.Count -eq 0){throw 'Remote SSH exit verification is missing.'}
foreach($result in $ssh){if($result.Groups[1].Value.Trim() -ne '0'){throw 'Remote origin feed command failed.'}}
$records=@([regex]::Matches($text,'(?m)^\{[^\r\n]*\}\s*$')|ForEach-Object {try{$_.Value|ConvertFrom-Json}catch{}})
$matching=@($records|Where-Object {$_.verification -ceq 'clipshot-origin-feed-v1' -and $_.request_id -ceq $requestId -and $_.mode -ceq $mode})
if($matching.Count -ne 1){throw 'Unique origin feed receipt is missing.'}
$record=$matching[0]
if($record.feed_sha256 -cne $digest -or $record.current_sha256 -cne $digest -or $record.bytes -ne $raw.Length -or $record.current_matches_input -ne $true -or $record.packages_count -ne $expected.Count -or @($record.packages).Count -ne $expected.Count){throw 'Origin feed bytes or package receipt mismatch.'}
if($record.atomic_replace -ne (-not [bool]$CheckOnly)){throw 'Unexpected origin feed publication mode.'}
foreach($package in $expected){
 $items=@($record.packages|Where-Object {$_.version -ceq $package.version -and $_.name -ceq $package.name})
 if($items.Count -ne 1 -or $items[0].verified -ne $true -or $items[0].sha256 -cne $package.sha256 -or $items[0].size -ne $package.size -or $items[0].url -cne "https://bot.data-clouds.ru/clipshot/v$($package.version)/$($package.name)"){throw 'Origin package verification mismatch.'}
}
$record|ConvertTo-Json -Depth 8 -Compress
