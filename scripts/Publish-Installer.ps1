param([Parameter(Mandatory=$true)][string]$Setup,[string]$Destination='D:/install/Codex Software')
$ErrorActionPreference='Stop'
$source=Get-Item -LiteralPath $Setup
if($source.Name -notmatch '^ClipShot-Setup-\d+\.\d+\.\d+\.exe$'){throw 'Installer name must contain its version.'}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$root=[IO.Path]::GetFullPath($Destination)
$latest=Join-Path $root $source.Name
$staging=$latest+'.new'
Copy-Item -LiteralPath $source.FullName -Destination $staging -Force
if((Get-FileHash -LiteralPath $source.FullName).Hash -ne (Get-FileHash -LiteralPath $staging).Hash){throw 'Installer copy hash mismatch'}
Move-Item -LiteralPath $staging -Destination $latest -Force
# Keep only this application's current installer. Other software is untouched.
foreach($old in Get-ChildItem -LiteralPath $root -File | Where-Object {$_.Name -match '^(ClipShot|ClipNest)-Setup-\d+\.\d+\.\d+\.exe$' -and $_.Name -ne $source.Name}){
    $path=[IO.Path]::GetFullPath($old.FullName)
    if((Split-Path $path) -ne $root){throw 'Unexpected previous installer path'}
    $archive=Join-Path $source.DirectoryName $old.Name
    if(-not(Test-Path -LiteralPath $archive)){
        Copy-Item -LiteralPath $path -Destination $archive
    }
    if((Get-FileHash -LiteralPath $path).Hash -ne (Get-FileHash -LiteralPath $archive).Hash){throw 'Previous installer archive mismatch'}
    Remove-Item -LiteralPath $path
}
Write-Host "Ready for testing: $latest"
