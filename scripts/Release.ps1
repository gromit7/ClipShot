param([Parameter(Mandatory=$true)][string]$MakeNsis,[Parameter(Mandatory=$true)][string]$NotesFile)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
function GitHub([string[]]$Arguments){$value=& gh @Arguments;if($LASTEXITCODE -ne 0){throw "GitHub CLI: $($Arguments[0]) завершился с ошибкой"};return $value}
if(& git -C $repo status --porcelain){throw 'Перед выпуском нужен чистый коммит.'}
$commit=(& git -C $repo rev-parse HEAD).Trim()
$project=[xml](Get-Content -LiteralPath (Join-Path $repo 'ClipShot/ClipShot.csproj') -Raw)
$version=[string]$project.Project.PropertyGroup.Version
$tag="v$version"
$repository='gromit7/ClipShot'
if(-not $env:CLIPSHOT_MIRROR_EXECUTOR -or -not(Test-Path -LiteralPath $env:CLIPSHOT_MIRROR_EXECUTOR -PathType Leaf)){
    throw 'Для выпуска настройте CLIPSHOT_MIRROR_EXECUTOR: приватный исполнитель публикации российского зеркала.'
}
$existing=& gh release view $tag --repo $repository --json tagName 2>$null
if($LASTEXITCODE -eq 0){throw 'Выпущенные версии неизменяемы. Увеличьте версию.'}
# Рабочий дистрибутив на D остаётся прежним до подтверждения GitHub.
& (Join-Path $PSScriptRoot 'Test-Release.ps1') -MakeNsis $MakeNsis
& git -C $repo push origin main
if($LASTEXITCODE -ne 0){throw 'Не удалось отправить исходники.'}
# Независимая сборка GitHub должна подтвердить тот же коммит до публикации.
$deadline=[DateTime]::UtcNow.AddMinutes(20)
do{
    $runs=GitHub -Arguments @('run','list','--repo',$repository,'--commit',$commit,'--workflow','build.yml','--json','databaseId,status,conclusion') | ConvertFrom-Json
    $run=$runs | Select-Object -First 1
    if($run -and $run.status -eq 'completed'){
        if($run.conclusion -ne 'success'){throw 'Независимая сборка GitHub не прошла.'}
        break
    }
    if([DateTime]::UtcNow -gt $deadline){throw 'Истекло ожидание независимой сборки GitHub.'}
    Start-Sleep -Seconds 10
}while($true)
$setup=Join-Path $repo "ClipShot-Setup-$version.exe"
& (Join-Path $PSScriptRoot 'New-ReleaseEvidence.ps1') -Setup $setup -MakeNsis $MakeNsis
$assets=@($setup,(Join-Path $repo 'SHA256SUMS.txt'),(Join-Path $repo "ClipShot-$version-buildinfo.json"),(Join-Path $repo "ClipShot-$version-checks.json"))
# Черновик не попадёт к обновлятору. Полный SHA связывает выпуск с исходниками.
GitHub -Arguments (@('release','create',$tag,'--repo',$repository,'--target',$commit,'--title',"ClipShot $version",'--notes-file',$NotesFile,'--draft')+$assets) | Write-Output
# У черновика ещё нет публичного тега: проверяем по ID, а не через releases/tags.
$releaseId=(GitHub -Arguments @('release','view',$tag,'--repo',$repository,'--json','databaseId','--jq','.databaseId')).Trim()
$release=GitHub -Arguments @('api',"repos/$repository/releases/$releaseId") | ConvertFrom-Json
$asset=$release.assets | Where-Object name -eq "ClipShot-Setup-$version.exe"
$hash=(Get-FileHash -LiteralPath $setup).Hash.ToLowerInvariant()
if(-not $asset -or $asset.state -ne 'uploaded' -or $asset.size -ne (Get-Item $setup).Length -or $asset.digest -ne "sha256:$hash"){throw 'GitHub не подтвердил размер и SHA-256. Черновик сохранён.'}
GitHub -Arguments @('release','edit',$tag,'--repo',$repository,'--draft=false','--latest') | Write-Output
$latest=GitHub -Arguments @('api',"repos/$repository/releases/latest") | ConvertFrom-Json
if($latest.tag_name -ne $tag){throw 'Лента автообновления не указывает на новый выпуск.'}
$published=$latest.assets | Where-Object name -eq "ClipShot-Setup-$version.exe"
if($published.digest -ne "sha256:$hash"){throw 'Контрольная сумма в ленте не совпадает.'}
# Ленту переключаем только после проверки российского зеркала. При ошибке
# опубликованный GitHub-пакет сохраняется; продолжение выполняется штатным publisher.
& (Join-Path $PSScriptRoot 'Publish-Mirror.ps1') -Version $version
& (Join-Path $PSScriptRoot 'Publish-UpdateFeed.ps1') -ExpectedVersion $version -Publish
& (Join-Path $PSScriptRoot 'Publish-Installer.ps1') -Setup $setup
Write-Output "Выпущен ClipShot ${version}: GitHub, автообновление и локальный установщик совпадают."
