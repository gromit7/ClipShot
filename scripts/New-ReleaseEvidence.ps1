param([Parameter(Mandatory=$true)][string]$Setup,[string]$MakeNsis)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$project=[xml](Get-Content -LiteralPath (Join-Path $repo 'ClipShot/ClipShot.csproj') -Raw)
$version=[string]$project.Project.PropertyGroup.Version
$installer=Get-Item -LiteralPath $Setup
if($installer.Name -ne "ClipShot-Setup-$version.exe"){throw 'Версия установщика не совпадает с исходниками.'}
$dirty=& git -C $repo status --porcelain
if($LASTEXITCODE -ne 0 -or $dirty){throw 'Сначала сохраните все исходники выпуска в чистом коммите Git.'}
$commit=(& git -C $repo rev-parse HEAD).Trim()
$gate=Get-Content -LiteralPath (Join-Path $repo 'TestResults/release-checks.json') -Raw | ConvertFrom-Json
if(@($gate | Where-Object {-not $_.Passed}).Count -or -not($gate | Where-Object {$_.Name -eq 'Isolated install, running-app upgrade and uninstall' -and $_.Passed})){throw 'Полная проверка выпуска не завершена.'}
$trx=[xml](Get-Content -LiteralPath (Join-Path $repo 'TestResults/core.trx') -Raw)
$counters=$trx.TestRun.ResultSummary.Counters
if([int]$counters.failed -ne 0 -or [int]$counters.passed -eq 0){throw 'Нет успешного отчёта модульных тестов.'}
$dotnet=$env:CLIPSHOT_DOTNET
if(-not $dotnet){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$sdk=(& $dotnet --version).Trim()
if($LASTEXITCODE -ne 0){throw 'Не удалось определить SDK.'}
$files=@()
foreach($relative in 'ClipShot.exe','ClipShot.dll','ClipShot.pri','Assets/ClipShot.ico','Assets/ClipShot.Muted.ico','Assets/ClipShot.png','LICENSE','FileInventory.json','Updater/ClipShot.Updater.exe'){
    $file=Get-Item -LiteralPath (Join-Path $repo ('ClipShot-app/'+$relative))
    $files+=@{path=$relative;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()}
}
$info=[ordered]@{project='ClipShot';version=$version;sourceRepository='https://github.com/gromit7/ClipShot';sourceCommit=$commit;sourceTag="v$version";sourceTree="https://github.com/gromit7/ClipShot/tree/v$version";builtOn='Windows x64';dotnetSdk=$sdk;configuration='Release';selfContained=$true;generatedUtc=[DateTime]::UtcNow.ToString('o');installer=@{file=$installer.Name;bytes=$installer.Length;sha256=(Get-FileHash -LiteralPath $installer.FullName).Hash.ToLowerInvariant()};publishedFiles=$files;binaryReproducibilityClaimed=$false}
if($MakeNsis){$info.nsisFileVersion=(Get-Item -LiteralPath $MakeNsis).VersionInfo.FileVersion}
$infoPath=Join-Path $repo "ClipShot-$version-buildinfo.json"
$checksPath=Join-Path $repo "ClipShot-$version-checks.json"
$info | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $infoPath -Encoding utf8
@{version=$version;sourceCommit=$commit;unitTests=@{total=[int]$counters.total;passed=[int]$counters.passed;failed=[int]$counters.failed};localReleaseChecks=$gate} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $checksPath -Encoding utf8
$sums=foreach($file in @($installer.FullName,$infoPath,$checksPath)){"$((Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"}
$sums | Set-Content -LiteralPath (Join-Path $repo 'SHA256SUMS.txt') -Encoding utf8
Write-Output "Сведения о выпуске $version сохранены; коммит $commit."
