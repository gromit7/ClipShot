param([Parameter(Mandatory=$true)][string]$MakeNsis,[string]$LatestInstallerDir,[switch]$NoLatestCopy)
$ErrorActionPreference='Stop'
$publish=Join-Path (Split-Path $PSScriptRoot) 'ClipShot-app'
$project=[xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ClipShot.csproj') -Raw)
$version=[string]$project.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid release version.'}
if (-not (Test-Path -LiteralPath (Join-Path $publish 'ClipShot.exe'))) {throw 'Publish ClipShot-app first.'}
if((Get-Item -LiteralPath (Join-Path $publish 'ClipShot.exe')).VersionInfo.ProductVersion.Split('+')[0] -ne $version){throw 'Publish version does not match project.'}
$unexpected=Get-ChildItem -LiteralPath $publish -File -Recurse | Where-Object {$_.Extension -in '.db','.sqlite','.pfx','.key' -or $_.Name -like '.env*'}
if($unexpected){throw 'Publication contains prohibited user data or secret files.'}
foreach($required in 'ClipShot.pri','Updater/ClipShot.Updater.exe','FileInventory.json','LICENSE','Assets/ClipShot.ico','Assets/ClipShot.Muted.ico','Assets/ClipShot.png','Assets/Selection.cur'){if(-not(Test-Path -LiteralPath (Join-Path $publish $required))){throw "Missing publication asset: $required"}}
# Uninstall only the files shipped by this installer, never recursively delete an arbitrary chosen directory.
$lines=@('; Generated from the publication. User data is never included.')
foreach($f in Get-ChildItem -LiteralPath $publish -File -Recurse) {
    $relative=[IO.Path]::GetRelativePath($publish,$f.FullName)
    $lines+='Delete "$INSTDIR\'+$relative+'"'
}
foreach($d in Get-ChildItem -LiteralPath $publish -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending) {
    $relative=[IO.Path]::GetRelativePath($publish,$d.FullName)
    $lines+='RMDir "$INSTDIR\'+$relative+'"'
}
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'uninstall-files.nsh') -Value $lines -Encoding utf8
& $MakeNsis /V2 /WX /INPUTCHARSET UTF8 "/DVERSION=$version" (Join-Path $PSScriptRoot 'installer.nsi')
if($LASTEXITCODE -ne 0){throw "NSIS failed: $LASTEXITCODE"}
$setup=Join-Path (Split-Path $PSScriptRoot) ("ClipShot-Setup-"+$version+".exe")
if(-not (Test-Path -LiteralPath $setup)){throw 'Compiled installer not found.'}
if($NoLatestCopy){return}
if(-not $LatestInstallerDir -and (Test-Path -LiteralPath 'D:\install')){$LatestInstallerDir='D:\install\Codex Software'}
if($LatestInstallerDir){& (Join-Path (Split-Path $PSScriptRoot) 'scripts/Publish-Installer.ps1') -Setup $setup -Destination $LatestInstallerDir}
