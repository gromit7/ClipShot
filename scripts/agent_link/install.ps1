param([ValidateSet('work','dacha')][string]$Actor='dacha',[string]$Identity)
$ErrorActionPreference='Stop'
$destination=Join-Path $env:USERPROFILE '.codex/tools/codex-link'
New-Item -ItemType Directory -Path $destination -Force|Out-Null
foreach($name in @('server.py','client.py','test_link.py','register.py','install.ps1','bootstrap.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $destination $name) -Force}
$runtime=Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
if(-not(Test-Path -LiteralPath $runtime)){$runtime=(Get-Command python -ErrorAction Stop).Source}
if(-not $Identity){$candidate=Join-Path $env:USERPROFILE '.ssh/maxbot_codex_ed25519';if(Test-Path -LiteralPath $candidate){$Identity=$candidate}}
$config=Join-Path $destination 'config.json'
$settings=@{actor=$Actor;host='root@vpn.data-clouds.ru';identity=$Identity}
if($Actor -eq 'work'){$settings.deepseek_adapter='D:/Мои документы/codex-projects/DeepSeek Harness/scripts/ask.mjs'}
if(Test-Path $config){Copy-Item -LiteralPath $config -Destination ($config+'.backup-'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))}
$settings|ConvertTo-Json|Set-Content -LiteralPath $config -Encoding utf8
& $runtime (Join-Path $destination 'client.py') --config $config info
if($LASTEXITCODE -ne 0){throw 'Existing SSH access unavailable. No keys, routes, firewall or authentication were changed.'}
& $runtime (Join-Path $destination 'register.py') --runtime $runtime
if($LASTEXITCODE -ne 0){throw 'MCP registration stopped; existing config preserved.'}
$hello=Join-Path $destination 'hello.txt'
('HELLO from '+$Actor+'. Own existing SSH connection and mailbox tool call succeeded. Peer delivery still requires a recipient receipt.')|Set-Content -LiteralPath $hello -Encoding utf8
$recipient=if($Actor -eq 'work'){'deepseek'}else{'work'}
& $runtime (Join-Path $destination 'client.py') --config $config send --to $recipient --thread 'clipshot/update-dacha-20261031' --kind hello --key ('hello-'+$Actor+'-20261031') --file $hello
if($LASTEXITCODE -ne 0){throw 'HELLO queued locally; retry flush after connection returns.'}
Write-Host 'Installed. No credentials copied. See ONLINE-COMMS.md for the bounded receiver.'
