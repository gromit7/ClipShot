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
$setups=@($setup,(Join-Path $repo "ClipShot-Win10-Setup-$version.exe"),(Join-Path $repo "ClipShot-Win11-Setup-$version.exe"))
& (Join-Path $PSScriptRoot 'New-ReleaseEvidence.ps1') -Setup $setup -MakeNsis $MakeNsis
$assets=$setups+@((Join-Path $repo 'SHA256SUMS.txt'),(Join-Path $repo "ClipShot-$version-buildinfo.json"),(Join-Path $repo "ClipShot-$version-checks.json"))
function Confirm-Packages($Release,[string[]]$Paths){
    foreach($path in $Paths){
        $file=Get-Item -LiteralPath $path
        $matching=@($Release.assets | Where-Object name -eq $file.Name)
        $hash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
        if($matching.Count -ne 1 -or $matching[0].state -ne 'uploaded' -or $matching[0].size -ne $file.Length -or $matching[0].digest -ne "sha256:$hash"){throw "GitHub не подтвердил пакет $($file.Name), его размер и SHA-256."}
        $expectedUrl="https://github.com/$repository/releases/download/$tag/$($file.Name)"
        # До публикации GitHub может дать URL untagged-*. Для черновика
        # достаточно имени/размера/digest; публичный latest проверяется строго.
        if(-not $Release.draft -and $matching[0].browser_download_url -cne $expectedUrl){throw "Неверный адрес пакета $($file.Name)."}
    }
}
# Черновик не попадёт к обновлятору. Полный SHA связывает выпуск с исходниками.
GitHub -Arguments (@('release','create',$tag,'--repo',$repository,'--target',$commit,'--title',"ClipShot $version",'--notes-file',$NotesFile,'--draft')+$assets) | Write-Output
# У черновика ещё нет публичного тега: проверяем по ID, а не через releases/tags.
$releaseId=(GitHub -Arguments @('release','view',$tag,'--repo',$repository,'--json','databaseId','--jq','.databaseId')).Trim()
$release=GitHub -Arguments @('api',"repos/$repository/releases/$releaseId") | ConvertFrom-Json
Confirm-Packages $release $setups
GitHub -Arguments @('release','edit',$tag,'--repo',$repository,'--draft=false','--latest') | Write-Output
$latest=GitHub -Arguments @('api',"repos/$repository/releases/latest") | ConvertFrom-Json
if($latest.tag_name -ne $tag){throw 'Лента автообновления не указывает на новый выпуск.'}
Confirm-Packages $latest $setups
# Ленту переключаем только после проверки российского зеркала. При ошибке
# опубликованный GitHub-пакет сохраняется; продолжение выполняется штатным publisher.
& (Join-Path $PSScriptRoot 'Publish-Mirror.ps1') -Version $version
& (Join-Path $PSScriptRoot 'Publish-UpdateFeed.ps1') -ExpectedVersion $version -Publish
& (Join-Path $PSScriptRoot 'Publish-Installer.ps1') -Setup $setup
Write-Output "Выпущен ClipShot ${version}: GitHub и зеркало содержат три проверенных пакета; лента выбирает Windows 10/11, на D сохранены два явных установщика."
