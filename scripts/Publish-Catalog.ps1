param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$Setup,
    [string]$Hook='D:/Мои документы/codex-projects/data-clouds.ru/scripts/publish-release.py',
    [string]$Python='python',
    [switch]$CheckOnly
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CatalogPublication.ps1')
try {
    Publish-ClipShotCatalog -Version $Version -Setup $Setup -Hook $Hook -Python $Python -CheckOnly:$CheckOnly
} catch {
    throw "Витрина не подтверждена: $($_.Exception.Message) Уже опубликованный ClipShot и feed не откатываются. Исправьте причину и повторите только scripts/Publish-Catalog.ps1 для этой версии, не весь Release.ps1."
}
