param([string]$Identity)
$ErrorActionPreference='Stop'
$root=Join-Path $env:LOCALAPPDATA 'CodexLink-Setup'
New-Item -ItemType Directory -Path $root -Force|Out-Null
if(-not $Identity){$candidate=Join-Path $env:USERPROFILE '.ssh/maxbot_codex_ed25519';if(Test-Path $candidate){$Identity=$candidate}}
$sshArgs=@('-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=10')
if($Identity){$sshArgs+=@('-i',$Identity)}
& scp @sshArgs 'root@vpn.data-clouds.ru:/opt/codex-link/v1/client.zip' (Join-Path $root 'client.zip')
if($LASTEXITCODE -ne 0){throw 'Existing authorized SSH access required. No new keys or security changes made.'}
Expand-Archive -LiteralPath (Join-Path $root 'client.zip') -DestinationPath (Join-Path $root 'client') -Force
& (Join-Path $root 'client/install.ps1') -Actor dacha -Identity $Identity
