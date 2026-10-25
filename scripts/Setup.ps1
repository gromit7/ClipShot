$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$dotnet=$null
if($env:CLIPSHOT_DOTNET -and (Test-Path -LiteralPath $env:CLIPSHOT_DOTNET)){$dotnet=$env:CLIPSHOT_DOTNET}
if(-not $dotnet){$local=Join-Path $repo '.tools/dotnet/dotnet.exe';if(Test-Path -LiteralPath $local){$dotnet=$local}}
if(-not $dotnet){$command=Get-Command dotnet -ErrorAction SilentlyContinue;if($command){$sdks=& $command.Source --list-sdks;if($sdks -match '^10\.'){ $dotnet=$command.Source }}}
if(-not $dotnet){
    $tools=Join-Path $repo '.tools';New-Item -ItemType Directory -Force $tools | Out-Null
    $installer=Join-Path $tools 'dotnet-install.ps1'
    Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Channel 10.0 -InstallDir (Join-Path $tools 'dotnet') -NoPath
    $dotnet=Join-Path $tools 'dotnet/dotnet.exe'
}
& $dotnet --version;if($LASTEXITCODE -ne 0){throw '.NET SDK validation failed'}
& $dotnet restore (Join-Path $repo 'ClipShot/ClipShot.csproj');if($LASTEXITCODE -ne 0){throw 'Application restore failed'}
& $dotnet restore (Join-Path $repo 'ClipShot.Checks/ClipShot.Checks.csproj');if($LASTEXITCODE -ne 0){throw 'Checks restore failed'}
Write-Host 'Ready. Run scripts/Build.ps1 -Test -Publish.'
