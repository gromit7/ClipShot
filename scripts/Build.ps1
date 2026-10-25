param([switch]$Test,[switch]$Publish)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$dotnet=$env:CLIPSHOT_DOTNET
if(-not $dotnet){$local=Join-Path $repo '.tools/dotnet/dotnet.exe';if(Test-Path -LiteralPath $local){$dotnet=$local}}
if(-not $dotnet){$command=Get-Command dotnet -ErrorAction SilentlyContinue;if($command){$dotnet=$command.Source}}
if(-not $dotnet){throw 'Run scripts/Setup.ps1 first.'}
Push-Location $repo
try{
    & $dotnet build ClipShot/ClipShot.csproj -c Release;if($LASTEXITCODE -ne 0){throw 'Build failed'}
    if($Test){& $dotnet test ClipShot.Checks/ClipShot.Checks.csproj -c Release --logger 'trx;LogFileName=core.trx' --results-directory (Join-Path $repo 'TestResults');if($LASTEXITCODE -ne 0){throw 'Checks failed'}}
    if($Publish){
        $helper=Join-Path $repo '.tools/updater-publish'
        & $dotnet publish ClipShot.Updater/ClipShot.Updater.csproj -c Release -o $helper;if($LASTEXITCODE -ne 0){throw 'Updater build failed'}
& $dotnet publish ClipShot/ClipShot.csproj -c Release --self-contained true -o (Join-Path $repo 'ClipShot-app');if($LASTEXITCODE -ne 0){throw 'Publish failed'};New-Item -ItemType Directory -Path (Join-Path $repo 'ClipShot-app/Updater') -Force | Out-Null;Copy-Item -LiteralPath (Join-Path $helper 'ClipShot.Updater.exe') -Destination (Join-Path $repo 'ClipShot-app/Updater/ClipShot.Updater.exe') -Force;
        $publication=Join-Path $repo 'ClipShot-app';$inventory=@(Get-ChildItem -LiteralPath $publication -File -Recurse | ForEach-Object {[IO.Path]::GetRelativePath($publication,$_.FullName)} | Where-Object {$_ -ne 'FileInventory.json'})+@('FileInventory.json');ConvertTo-Json -InputObject $inventory | Set-Content -LiteralPath (Join-Path $publication 'FileInventory.json') -Encoding utf8}
}finally{Pop-Location}
