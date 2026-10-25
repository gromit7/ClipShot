param([string]$ExpectedVersion,[switch]$Publish)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
function Read-Release([string]$Endpoint){
    $release=& gh api "repos/gromit7/ClipShot/releases/$Endpoint" | ConvertFrom-Json
    if($LASTEXITCODE -ne 0){throw 'Не удалось прочитать выпуск GitHub.'}
    if($release.draft -or $release.prerelease -or $release.tag_name -notmatch '^v\d+\.\d+\.\d+$'){throw 'Нужен опубликованный стабильный выпуск.'}
    return $release
}
function Package-Release($Release,[string]$Flavor='legacy',[switch]$Optional){
    $version=$Release.tag_name.Substring(1)
    $name=switch($Flavor){'legacy'{"ClipShot-Setup-$version.exe"};'win10'{"ClipShot-Win10-Setup-$version.exe"};'win11'{"ClipShot-Win11-Setup-$version.exe"};default{throw 'Неизвестный вариант установщика.'}}
    $assets=@($Release.assets|Where-Object name -eq $name)
    if($Optional -and $assets.Count -eq 0){return $null}
    if($assets.Count -ne 1){throw "В выпуске нет единственного установщика $name."}
    $asset=$assets[0]
    $url="https://github.com/gromit7/ClipShot/releases/download/v$version/$name"
    if($asset.state -ne 'uploaded' -or $asset.browser_download_url -cne $url -or $asset.digest -notmatch '^sha256:[0-9a-fA-F]{64}$' -or $asset.size -lt 1024 -or $asset.size -gt 262144000){throw "Неверные метаданные установщика $name."}
    return [ordered]@{draft=$false;prerelease=$false;tag_name=$Release.tag_name;assets=@([ordered]@{name=$name;size=$asset.size;digest=$asset.digest;browser_download_url=$url})}
}
function Variant-Releases($Release,[switch]$Optional){
    $win10=Package-Release $Release 'win10' -Optional:$Optional
    $win11=Package-Release $Release 'win11' -Optional:$Optional
    if($Optional -and $null -eq $win10 -and $null -eq $win11){return $null}
    if($null -eq $win10 -or $null -eq $win11){throw 'OS-пакеты должны публиковаться парой.'}
    return [ordered]@{win10=$win10;win11=$win11}
}
if($Publish -and (& git -C $repo status --porcelain)){throw 'Сначала нужен чистый коммит исходников и политики.'}
$release=Read-Release 'latest'
$stable=Package-Release $release
$variants=Variant-Releases $release
if($ExpectedVersion -and $stable.tag_name -ne "v$ExpectedVersion"){throw 'Latest не совпадает с выпускаемой версией.'}
$policy=Get-Content -LiteralPath (Join-Path $repo 'update-policy.json') -Raw|ConvertFrom-Json
if($policy.schema -ne 1 -or $policy.channel -ne 'stable'){throw 'Неизвестная политика обновления.'}
$rollback=$null
if($policy.action -eq 'rollback'){
    if($policy.targetVersion -notmatch '^\d+\.\d+\.\d+$' -or [version]$policy.targetVersion -lt [version]'0.7.0' -or @($policy.affectedVersions).Count -eq 0){throw 'Неверная цель аварийного отката.'}
    foreach($version in $policy.affectedVersions){if($version -notmatch '^\d+\.\d+\.\d+$' -or [version]$version -le [version]$policy.targetVersion){throw 'Откат разрешён только явно указанным более новым версиям.'}}
    $target=Read-Release ('tags/v'+$policy.targetVersion)
    $rollback=[ordered]@{policy=$policy;release=(Package-Release $target)}
    # Старые теги могут иметь только legacy. Тогда поле variants отсутствует,
    # и новый клиент использует проверенный универсальный пакет при откате.
    $targetVariants=Variant-Releases $target -Optional
    if($null -ne $targetVariants){$rollback.variants=$targetVariants}
}elseif($policy.action -ne 'normal'){throw 'Неизвестное действие политики.'}
# schema=1 и stable/rollback.release остаются совместимы с уже установленными
# клиентами. Новые клиенты читают свой OS-вариант, не меняя общую публикацию DLL.
$json=[ordered]@{schema=1;stable=$stable;rollback=$rollback;variants=$variants}|ConvertTo-Json -Depth 12 -Compress
if([Text.Encoding]::UTF8.GetByteCount($json) -gt 32768){throw 'Лента превышает 32 КиБ.'}
$path=Join-Path $repo 'update-feed.json';[IO.File]::WriteAllText($path,$json+"`n",[Text.UTF8Encoding]::new($false))
if($Publish){
    & git -C $repo add -- update-feed.json;if($LASTEXITCODE -ne 0){throw 'Не удалось подготовить ленту.'}
    & git -C $repo diff --cached --quiet -- update-feed.json
    if($LASTEXITCODE -eq 1){& git -C $repo commit -m ("Лента обновлений "+$stable.tag_name);if($LASTEXITCODE -ne 0){throw 'Не удалось сохранить ленту.'}}
    elseif($LASTEXITCODE -ne 0){throw 'Не удалось сравнить ленту.'}
    & git -C $repo push origin main;if($LASTEXITCODE -ne 0){throw 'Не удалось отправить ленту.'}
    $commit=(& git -C $repo rev-parse HEAD).Trim()
    $response=Invoke-WebRequest -Uri ("https://raw.githubusercontent.com/gromit7/ClipShot/main/update-feed.json?commit="+$commit) -Headers @{'Cache-Control'='no-cache'} -TimeoutSec 15
    $remote=$response.Content|ConvertFrom-Json
    $local=$json|ConvertFrom-Json
    foreach($flavor in 'legacy','win10','win11'){
        $expected=if($flavor -eq 'legacy'){$local.stable}else{$local.variants.$flavor}
        $actual=if($flavor -eq 'legacy'){$remote.stable}else{$remote.variants.$flavor}
        if($actual.tag_name -ne $expected.tag_name -or $actual.assets[0].name -cne $expected.assets[0].name -or $actual.assets[0].size -ne $expected.assets[0].size -or $actual.assets[0].digest -cne $expected.assets[0].digest -or $actual.assets[0].browser_download_url -cne $expected.assets[0].browser_download_url){throw "Публичная лента варианта $flavor не совпадает с опубликованным выпуском."}
    }
    if(($remote.rollback|ConvertTo-Json -Depth 12 -Compress) -cne ($local.rollback|ConvertTo-Json -Depth 12 -Compress)){throw 'Публичная политика отката не совпадает с проверенной.'}
}
Write-Output ("Лента обновлений (legacy, Windows 10, Windows 11): "+$stable.tag_name+", "+[Text.Encoding]::UTF8.GetByteCount($json)+" байт.")
