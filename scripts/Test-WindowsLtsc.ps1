[CmdletBinding()]
param([string]$AppDirectory,[string]$ProjectFile,[switch]$CheckOnly,[switch]$SelfTest)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
trap { Write-Error 'Preflight failed; check OS access and explicit input files. Personal paths suppressed.' -ErrorAction Continue; exit 1 }
function Assert-Preflight([bool]$Condition,[string]$Message){if(-not $Condition){throw "Preflight: $Message"}}
function Get-PlatformAssessment([int]$Build,[string]$Architecture){
    Assert-Preflight ($Build -gt 0) 'Invalid OS build'
    Assert-Preflight ($Architecture -in @('x64','x86','Arm64','Unknown')) 'Invalid architecture'
    if($Build -lt 17763 -or $Architecture -ne 'x64'){return 'OutsideDeclaredTarget'}
    if($Build -eq 17763){return 'LTSC2019Candidate_RuntimeUnverified'}
    if($Build -ge 22000){return 'Windows11Candidate_RuntimeUnverified'}
    return 'Windows10Candidate_RuntimeUnverified'
}
function Read-ProjectMetadata([string]$File){
    Assert-Preflight ([IO.Path]::GetExtension($File) -ieq '.csproj') 'Expected csproj'
    Assert-Preflight ((Get-Item -LiteralPath $File).Length -le 1048576) 'Project file too large'
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($File,$settings)
    try{$document=[Xml.XmlDocument]::new();$document.XmlResolver=$null;$document.Load($reader)}finally{$reader.Dispose()}
    $tfm=@($document.SelectNodes('/Project/PropertyGroup/TargetFramework'))
    $minimum=@($document.SelectNodes('/Project/PropertyGroup/TargetPlatformMinVersion'))
    $sdk=@($document.SelectNodes('/Project/ItemGroup/PackageReference[@Include="Microsoft.WindowsAppSDK"]'))
    Assert-Preflight ($tfm.Count -eq 1 -and $minimum.Count -eq 1 -and $sdk.Count -eq 1) 'Missing/ambiguous project metadata'
    Assert-Preflight ($tfm[0].InnerText -match '^net[0-9]+\.[0-9]+-windows[0-9.]+$') 'Unresolved target framework'
    Assert-Preflight ($minimum[0].InnerText -match '^\d+\.\d+\.\d+\.\d+$') 'Unresolved OS minimum'
    $sdkVersion=$sdk[0].GetAttribute('Version')
    Assert-Preflight ($sdkVersion -match '^\d+\.\d+\.\d+(\.\d+)?$') 'Unresolved AppSDK version'
    [pscustomobject]@{TargetFramework=$tfm[0].InnerText;TargetPlatformMinVersion=$minimum[0].InnerText;WindowsAppSDK=$sdkVersion;RuntimeVerified=$false}
}
if($MyInvocation.InvocationName -eq '.'){return}
if($SelfTest){
    Assert-Preflight ((Get-PlatformAssessment 17763 x64) -eq 'LTSC2019Candidate_RuntimeUnverified') 'LTSC classification'
    Assert-Preflight ((Get-PlatformAssessment 19045 x64) -eq 'Windows10Candidate_RuntimeUnverified') 'Win10 classification'
    Assert-Preflight ((Get-PlatformAssessment 26100 x64) -eq 'Windows11Candidate_RuntimeUnverified') 'Win11 classification'
    Assert-Preflight ((Get-PlatformAssessment 17762 x64) -eq 'OutsideDeclaredTarget') 'Old build accepted'
    Assert-Preflight ((Get-PlatformAssessment 17763 Arm64) -eq 'OutsideDeclaredTarget') 'ARM accepted'
    foreach($case in @({Get-PlatformAssessment 0 x64},{Get-PlatformAssessment 17763 invalid})){
        $failed=$false;try{$null=& $case}catch{$failed=$true};Assert-Preflight $failed 'Invalid platform accepted'
    }
    [pscustomobject]@{Result='PASS';Cases=7;Scope='SyntheticPlatformValidator'};return
}
# CheckOnly and ordinary invocation are equally read-only. No EXE is executed.
$os=Get-CimInstance Win32_OperatingSystem
$cv=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$cpuArchitectures=@(Get-CimInstance Win32_Processor | Select-Object -ExpandProperty Architecture -Unique)
$architecture='Unknown'
if($cpuArchitectures.Count -eq 1){$architecture=switch([int]$cpuArchitectures[0]){9{'x64'} 12{'Arm64'} 0{'x86'} default{'Unknown'}}}
$platform=[pscustomobject]@{Edition=$cv.EditionID;Build=[int]$os.BuildNumber;Revision=$cv.UBR;Architecture=$architecture;Assessment=Get-PlatformAssessment ([int]$os.BuildNumber) $architecture}
$files=@()
if($AppDirectory){
    Assert-Preflight (Test-Path -LiteralPath $AppDirectory -PathType Container) 'Application directory unavailable'
    foreach($name in @('ClipShot.exe','ClipShot.dll','ClipShot.pri','Updater/ClipShot.Updater.exe')){
        $file=Join-Path $AppDirectory $name
        if(-not(Test-Path -LiteralPath $file -PathType Leaf)){$files+=[pscustomobject]@{Name=$name;Status='MISSING'};continue}
        $item=Get-Item -LiteralPath $file
        Assert-Preflight (-not($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) 'Reparse file rejected'
        $files+=[pscustomobject]@{Name=$name;Status='READ';Bytes=$item.Length;FileVersion=$item.VersionInfo.FileVersion;ProductVersion=$item.VersionInfo.ProductVersion;Sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
}
$project=$null
if($ProjectFile){$project=Read-ProjectMetadata $ProjectFile}
[pscustomobject]@{Result='METADATA_ONLY';OS=$platform;ProgramFiles=$files;Project=$project;RuntimeVerified=$false;Note='No compatibility claim; no EXE launched'}
