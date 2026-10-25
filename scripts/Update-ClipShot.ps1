param([switch]$CheckOnly,[ValidateSet("GitHub","Russia")][string]$Source="GitHub")
$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$repository='gromit7/ClipShot'
$mirror="https://bot.data-clouds.ru/clipshot"
$metadata="https://api.github.com/repos/$repository/releases/latest"
$release=Invoke-RestMethod -Uri $metadata -Headers @{'User-Agent'='ClipShot-Recovery/1'} -TimeoutSec 20
if($release.draft -or $release.prerelease -or $release.tag_name -notmatch '^v(\d+\.\d+\.\d+)$'){throw 'Не найден стабильный выпуск ClipShot.'}
$version=$Matches[1]
$name="ClipShot-Setup-$version.exe"
$releaseRoot="https://github.com/$repository/releases/download/v$version"
$asset=@($release.assets | Where-Object name -eq $name)
if($asset.Count -ne 1 -or $asset[0].browser_download_url -cne "$releaseRoot/$name" -or $asset[0].size -lt 1024 -or $asset[0].size -gt 262144000 -or $asset[0].digest -notmatch '^sha256:([0-9a-fA-F]{64})$'){throw 'Не удалось проверить сведения о выпуске.'}
$expectedHash=$Matches[1]
# Зеркало меняет только место загрузки EXE. Версия, размер, SHA-256 и сведения
# о сборке всегда берутся из официального выпуска GitHub.
$setupUrl=if($Source -eq "Russia"){"$mirror/v$version/$name"}else{$asset[0].browser_download_url}
Write-Host "Найдена свежая версия ClipShot $version."
Write-Host "Источник установщика: $setupUrl"
if($CheckOnly){return}
$folder=Join-Path ([IO.Path]::GetTempPath()) ('ClipShot-Recovery-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
$setup=Join-Path $folder $name
Write-Host 'Скачиваем проверенный установщик…'
Invoke-WebRequest -Uri $setupUrl -OutFile $setup -UseBasicParsing -TimeoutSec 600
if((Get-Item -LiteralPath $setup).Length -ne $asset[0].size -or (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash -ine $expectedHash){throw 'Проверка установщика не пройдена. Он не будет запущен.'}
$infoAsset=@($release.assets | Where-Object name -eq "ClipShot-$version-buildinfo.json")
if($infoAsset.Count -ne 1 -or $infoAsset[0].browser_download_url -cne "$releaseRoot/ClipShot-$version-buildinfo.json"){throw 'Не найден список контрольных сумм программы.'}
$info=Invoke-RestMethod -Uri $infoAsset[0].browser_download_url -TimeoutSec 20
if($info.version -ne $version){throw 'Сведения о сборке не соответствуют выпуску.'}
Write-Host 'Восстанавливаем программу. История и шаблоны сохраняются…'
$process=Start-Process -FilePath $setup -ArgumentList '/S' -PassThru
$process.WaitForExit();$process.Refresh()
if($process.ExitCode -ne 0){throw "Установка завершилась с кодом $($process.ExitCode)."}
$installed=(Get-ItemProperty 'HKCU:\Software\ClipNest' -ErrorAction SilentlyContinue).InstallDir
if(-not $installed){$installed=Join-Path $env:LOCALAPPDATA 'Programs\ClipShot'}
$root=[IO.Path]::GetFullPath($installed).TrimEnd('\')+'\'
foreach($file in $info.publishedFiles){
    $path=[IO.Path]::GetFullPath((Join-Path $root $file.path))
    if(-not $path.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $file.sha256){throw "Файл программы не прошёл проверку: $($file.path). Установка не считается успешной."}
}
Write-Host 'ClipShot обновлён. Основные файлы проверены.' -ForegroundColor Green
