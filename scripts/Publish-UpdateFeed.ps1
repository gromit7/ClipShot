param([string]$ExpectedVersion,[switch]$Publish)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
function Read-Release([string]$Endpoint){
    $release=& gh api "repos/gromit7/ClipShot/releases/$Endpoint" | ConvertFrom-Json
    if($LASTEXITCODE -ne 0){throw 'Не удалось прочитать выпуск GitHub.'}
    if($release.draft -or $release.prerelease -or $release.tag_name -notmatch '^v\d+\.\d+\.\d+$'){throw 'Нужен опубликованный стабильный выпуск.'}
    $version=$release.tag_name.Substring(1);$name="ClipShot-Setup-$version.exe"
    $assets=@($release.assets|Where-Object name -eq $name)
    if($assets.Count -ne 1){throw 'В выпуске нет единственного версионного установщика.'}
    $asset=$assets[0]
    $url="https://github.com/gromit7/ClipShot/releases/download/v$version/$name"
    if($asset.browser_download_url -cne $url -or $asset.digest -notmatch '^sha256:[0-9a-fA-F]{64}$' -or $asset.size -lt 1024 -or $asset.size -gt 262144000){throw 'Неверные метаданные установщика.'}
    return [ordered]@{draft=$false;prerelease=$false;tag_name=$release.tag_name;assets=@([ordered]@{name=$name;size=$asset.size;digest=$asset.digest;browser_download_url=$url})}
}
if($Publish -and (& git -C $repo status --porcelain)){throw 'Сначала нужен чистый коммит исходников и политики.'}
$stable=Read-Release 'latest'
if($ExpectedVersion -and $stable.tag_name -ne "v$ExpectedVersion"){throw 'Latest не совпадает с выпускаемой версией.'}
$policy=Get-Content -LiteralPath (Join-Path $repo 'update-policy.json') -Raw|ConvertFrom-Json
if($policy.schema -ne 1 -or $policy.channel -ne 'stable'){throw 'Неизвестная политика обновления.'}
$rollback=$null
if($policy.action -eq 'rollback'){
    if($policy.targetVersion -notmatch '^\d+\.\d+\.\d+$' -or [version]$policy.targetVersion -lt [version]'0.7.0' -or @($policy.affectedVersions).Count -eq 0){throw 'Неверная цель аварийного отката.'}
    foreach($version in $policy.affectedVersions){if($version -notmatch '^\d+\.\d+\.\d+$' -or [version]$version -le [version]$policy.targetVersion){throw 'Откат разрешён только явно указанным более новым версиям.'}}
    $rollback=[ordered]@{policy=$policy;release=(Read-Release ('tags/v'+$policy.targetVersion))}
}elseif($policy.action -ne 'normal'){throw 'Неизвестное действие политики.'}
$json=[ordered]@{schema=1;stable=$stable;rollback=$rollback}|ConvertTo-Json -Depth 12 -Compress
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
    if($remote.stable.tag_name -ne $stable.tag_name -or $remote.stable.assets[0].digest -ne $stable.assets[0].digest){throw 'Публичная лента не совпадает с опубликованным выпуском.'}
}
Write-Output ("Лента обновлений: "+$stable.tag_name+", "+[Text.Encoding]::UTF8.GetByteCount($json)+" байт.")
