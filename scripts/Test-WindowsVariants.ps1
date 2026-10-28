param([string]$Python=$env:CLIPSHOT_PYTHON)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('.tools/windows-variants-tests/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force|Out-Null
function Assert($Condition,[string]$Message){if(-not $Condition){throw $Message};Write-Output ('PASS '+$Message)}
function Text([string]$Path,[string]$Value){$parent=Split-Path $Path;New-Item -ItemType Directory -Path $parent -Force|Out-Null;[IO.File]::WriteAllText($Path,$Value)}
function Must-Fail([scriptblock]$Run,[string]$Message){$failed=$false;try{&$Run|Out-Null}catch{$failed=$true};Assert $failed $Message}
# Фикстуры используют только .tools, синтетические байты и подставной GitHub.
# Пользовательская база, D, сеть, SSH и настоящий установщик не затрагиваются.
$savedFixture=Get-Variable -Name ClipShotVariantsFixture -Scope Global -ErrorAction SilentlyContinue
$global:ClipShotVariantsFixture=@{Stable=$null;Old=$null}
try{
# Publish-Installer: synthetic bytes only, no executable launch or D access.
$source=Join-Path $fixture 'source';$destination=Join-Path $fixture 'destination'
$version='9.8.7'
foreach($flavor in '', '-Win10','-Win11'){Text (Join-Path $source "ClipShot$flavor-Setup-$version.exe") ('synthetic '+$flavor)}
Text (Join-Path $destination 'ClipShot-Setup-0.7.3.exe') 'previous legacy'
Text (Join-Path $destination 'ClipShot-0.7.2.exe') 'previous universal public name'
Text (Join-Path $destination 'ClipShot-Win10-Setup-0.7.3.exe') 'previous Win10'
Text (Join-Path $destination 'ClipShot-Win11-Setup-9.8.7.exe') 'same name, previous bytes'
Text (Join-Path $destination 'other.exe') 'foreign'
$previous=@(Get-ChildItem -LiteralPath $destination -File|Where-Object Name -ne 'other.exe'|ForEach-Object{@{Name=$_.Name;Hash=(Get-FileHash -LiteralPath $_.FullName).Hash}})
& (Join-Path $repo 'scripts/Publish-Installer.ps1') -Setup (Join-Path $source "ClipShot-Setup-$version.exe") -Destination $destination | Out-Null
$names=@(Get-ChildItem -LiteralPath $destination -File|ForEach-Object Name)
Assert ($names.Count -eq 2 -and $names -contains 'other.exe' -and $names -contains 'ClipShot-9.8.7.exe') 'D destination keeps one universal installer and unrelated software'
foreach($old in $previous){Assert (@(Get-ChildItem -LiteralPath $source -File|Where-Object {(Get-FileHash -LiteralPath $_.FullName).Hash -eq $old.Hash}).Count -gt 0) ('Previous bytes archived: '+$old.Name)}
Assert ((Get-FileHash -LiteralPath (Join-Path $source "ClipShot-Setup-$version.exe")).Hash -eq (Get-FileHash -LiteralPath (Join-Path $destination "ClipShot-$version.exe")).Hash) 'Universal public name preserves verified package bytes'
Copy-Item -LiteralPath (Join-Path $source "ClipShot-Setup-$version.exe") -Destination (Join-Path $source "ClipShot-$version.exe")
& (Join-Path $repo 'scripts/Publish-Installer.ps1') -Setup (Join-Path $source "ClipShot-$version.exe") -Destination $destination | Out-Null
Assert (@(Get-ChildItem -LiteralPath $destination -Filter 'ClipShot*.exe').Count -eq 1) 'Publishing with the canonical source name is idempotent'
$blocked=Join-Path $fixture 'blocked';Text (Join-Path $blocked 'ClipShot-Setup-1.2.3.exe') 'keep on failure'
$old=Get-Item -LiteralPath (Join-Path $blocked 'ClipShot-Setup-1.2.3.exe');$hash=(Get-FileHash -LiteralPath $old.FullName).Hash
Text (Join-Path $source $old.Name) 'archive conflict'
Text (Join-Path $source ($old.BaseName+'-sha256-'+$hash.ToLowerInvariant()+'.exe')) 'corrupt second archive'
Must-Fail {& (Join-Path $repo 'scripts/Publish-Installer.ps1') -Setup (Join-Path $source "ClipShot-Setup-$version.exe") -Destination $blocked} 'Archive mismatch blocks replacement and deletion'
Assert ((Get-Content -LiteralPath $old.FullName -Raw) -ceq 'keep on failure') 'Original survives archive verification failure'
Assert (@(Get-ChildItem -LiteralPath $blocked -File).Count -eq 1) 'No partial published installer after archive verification failure'
# Feed: mocked GitHub command returning strict official metadata. No HTTP/git.
$feedRoot=Join-Path $fixture 'feed';New-Item -ItemType Directory -Path (Join-Path $feedRoot 'scripts') -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'scripts/Publish-UpdateFeed.ps1') -Destination (Join-Path $feedRoot 'scripts/Publish-UpdateFeed.ps1')
function Release([string]$Version,[switch]$Variants){
    $prefixes=if($Variants){'ClipShot','ClipShot-Win10','ClipShot-Win11'}else{@('ClipShot')}
    return @{draft=$false;prerelease=$false;tag_name=('v'+$Version);assets=@(foreach($prefix in $prefixes){$name="$prefix-Setup-$Version.exe";@{state='uploaded';name=$name;size=4096;digest=('sha256:'+('a'*64));browser_download_url="https://github.com/gromit7/ClipShot/releases/download/v$Version/$name"}})}
}
$global:ClipShotVariantsFixture.Stable=Release '9.8.7' -Variants;$global:ClipShotVariantsFixture.Old=Release '0.7.3'
function gh {param([Parameter(ValueFromRemainingArguments=$true)]$Arguments);$global:LASTEXITCODE=0;if($Arguments[1] -like '*/latest'){$global:ClipShotVariantsFixture.Stable|ConvertTo-Json -Depth 6}else{$global:ClipShotVariantsFixture.Old|ConvertTo-Json -Depth 6}}
$policy=@{schema=1;revision=1;channel='stable';action='normal';targetVersion=$null;affectedVersions=@();reason=''}
Text (Join-Path $feedRoot 'update-policy.json') ($policy|ConvertTo-Json)
& (Join-Path $feedRoot 'scripts/Publish-UpdateFeed.ps1') -ExpectedVersion '9.8.7' | Out-Null
$feed=Get-Content -LiteralPath (Join-Path $feedRoot 'update-feed.json') -Raw|ConvertFrom-Json
Assert ($feed.schema -eq 1 -and $feed.stable.assets[0].name -eq 'ClipShot-Setup-9.8.7.exe') 'Legacy feed contract preserved'
Assert ($feed.variants.win10.assets[0].name -eq 'ClipShot-Win10-Setup-9.8.7.exe' -and $feed.variants.win11.assets[0].name -eq 'ClipShot-Win11-Setup-9.8.7.exe') 'Feed variants use exact flavored filenames'& (Join-Path $feedRoot 'scripts/Publish-UpdateFeed.ps1') -ExpectedVersion '9.8.7' -PrivateRepository | Out-Null
$privateFeed=Get-Content -LiteralPath (Join-Path $feedRoot 'update-feed.json') -Raw|ConvertFrom-Json
Assert ($privateFeed.stable.assets[0].browser_download_url -ceq 'https://bot.data-clouds.ru/clipshot/v9.8.7/ClipShot-Setup-9.8.7.exe' -and $privateFeed.variants.win11.assets[0].browser_download_url -ceq 'https://bot.data-clouds.ru/clipshot/v9.8.7/ClipShot-Win11-Setup-9.8.7.exe') 'Private repository feed uses public origin packages without GitHub auth'

$policy.action='rollback';$policy.targetVersion='0.7.3';$policy.affectedVersions=@('9.8.7');$policy.reason='synthetic test'
Text (Join-Path $feedRoot 'update-policy.json') ($policy|ConvertTo-Json)
& (Join-Path $feedRoot 'scripts/Publish-UpdateFeed.ps1') -ExpectedVersion '9.8.7' | Out-Null
$feed=Get-Content -LiteralPath (Join-Path $feedRoot 'update-feed.json') -Raw|ConvertFrom-Json
Assert ($feed.rollback.release.tag_name -eq 'v0.7.3' -and $null -eq $feed.rollback.variants) 'Rollback to pre-variant release keeps universal installer'
$global:ClipShotVariantsFixture.Old=Release '9.8.6' -Variants;$policy.targetVersion='9.8.6';Text (Join-Path $feedRoot 'update-policy.json') ($policy|ConvertTo-Json)
& (Join-Path $feedRoot 'scripts/Publish-UpdateFeed.ps1') -ExpectedVersion '9.8.7' | Out-Null
$feed=Get-Content -LiteralPath (Join-Path $feedRoot 'update-feed.json') -Raw|ConvertFrom-Json
Assert ($feed.rollback.variants.win11.tag_name -eq 'v9.8.6') 'Rollback with OS packages retains both variants'
$global:ClipShotVariantsFixture.Stable.assets=@($global:ClipShotVariantsFixture.Stable.assets|Where-Object name -notlike '*Win10*')
Must-Fail {& (Join-Path $feedRoot 'scripts/Publish-UpdateFeed.ps1') -ExpectedVersion '9.8.7'} 'Missing OS package blocks feed'
$global:ClipShotVariantsFixture.Stable=Release '9.8.7' -Variants;$global:ClipShotVariantsFixture.Stable.assets[2].browser_download_url='https://example.org/wrong.exe'
Must-Fail {& (Join-Path $feedRoot 'scripts/Publish-UpdateFeed.ps1') -ExpectedVersion '9.8.7'} 'Foreign OS URL blocks feed'
# Evidence: fake project, unit report, common publication and package files.
$evidenceRoot=Join-Path $fixture 'evidence';New-Item -ItemType Directory -Path (Join-Path $evidenceRoot 'scripts') -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'scripts/New-ReleaseEvidence.ps1') -Destination (Join-Path $evidenceRoot 'scripts/New-ReleaseEvidence.ps1')
Text (Join-Path $evidenceRoot 'ClipShot/ClipShot.csproj') '<Project><PropertyGroup><Version>9.8.7</Version></PropertyGroup></Project>'
Text (Join-Path $evidenceRoot 'TestResults/core.trx') '<TestRun><ResultSummary><Counters total="3" passed="3" failed="0" /></ResultSummary></TestRun>'
$gate=@('Isolated install, running-app upgrade and uninstall','Installer OS compatibility matrix','Windows 11 wrapper install and upgrade','Released 0.7.4 updater migration from App working directory')|ForEach-Object{@{Name=$_;Passed=$true}}
Text (Join-Path $evidenceRoot 'TestResults/release-checks.json') (ConvertTo-Json -InputObject @($gate))
foreach($name in 'ClipShot.exe','ClipShot.dll','ClipShot.pri','Assets/ClipShot.ico','Assets/ClipShot.Muted.ico','Assets/ClipShot.png','LICENSE','FileInventory.json','FileHashes.json','Updater/ClipShot.Updater.exe'){Text (Join-Path $evidenceRoot ('ClipShot-app/'+$name)) ('synthetic '+$name)}
foreach($prefix in 'ClipShot','ClipShot-Win10','ClipShot-Win11'){Text (Join-Path $evidenceRoot "$prefix-Setup-9.8.7.exe") ('synthetic '+$prefix)}
Copy-Item -LiteralPath (Join-Path $evidenceRoot 'ClipShot-Setup-9.8.7.exe') -Destination (Join-Path $evidenceRoot 'ClipShot-9.8.7.exe')
function git {param([Parameter(ValueFromRemainingArguments=$true)]$Arguments);$global:LASTEXITCODE=0;if($Arguments -contains 'rev-parse'){'1'*40}}
function FakeDotnet {$global:LASTEXITCODE=0;'10.0.402'}
$oldDotnet=$env:CLIPSHOT_DOTNET
try{$env:CLIPSHOT_DOTNET='FakeDotnet';& (Join-Path $evidenceRoot 'scripts/New-ReleaseEvidence.ps1') -Setup (Join-Path $evidenceRoot 'ClipShot-Setup-9.8.7.exe')|Out-Null}finally{$env:CLIPSHOT_DOTNET=$oldDotnet}
$evidence=Get-Content -LiteralPath (Join-Path $evidenceRoot 'ClipShot-9.8.7-buildinfo.json') -Raw|ConvertFrom-Json
Assert ($evidence.installer.file -eq 'ClipShot-Setup-9.8.7.exe' -and $evidence.packages.Count -eq 4 -and $evidence.packages[3].file -eq 'ClipShot-9.8.7.exe' -and $evidence.packages[3].sha256 -eq $evidence.packages[0].sha256) 'Evidence includes universal public name and compatible update packages'
Assert ($evidence.packages[0].minBuild -eq 17763 -and $null -eq $evidence.packages[0].maxBuild -and $evidence.packages[1].maxBuild -eq 21999 -and $evidence.packages[2].minBuild -eq 22000) 'Evidence records exact OS build boundaries'
Assert (@(Get-Content -LiteralPath (Join-Path $evidenceRoot 'SHA256SUMS.txt')).Count -eq 6) 'SHA256SUMS covers all public installers and two reports'
Assert ($evidence.publishedFiles.path -contains 'FileHashes.json') 'Shared payload evidence includes file hashes'
# Проверяем ровно функцию gate из Release.ps1, не выполняя команды выпуска.
$releaseSource=Get-Content -LiteralPath (Join-Path $repo 'scripts/Release.ps1') -Raw
$resumeAst=[Management.Automation.Language.Parser]::ParseInput($releaseSource,[ref]$null,[ref]$null)
$resumeFunction=$resumeAst.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Confirm-UnpublishedDraft'},$true)
Invoke-Expression $resumeFunction.Extent.Text
$draftFixture=@{draft=$true;published_at=$null;tag_name='v9.8.7'}
Confirm-UnpublishedDraft $draftFixture $false 'v9.8.7'
Assert $true 'Interrupted unpublished draft without a Git tag can resume'
Must-Fail {Confirm-UnpublishedDraft $draftFixture $true 'v9.8.7'} 'Existing Git tag blocks draft overwrite'
$draftFixture.published_at='2026-01-01T00:00:00Z'
Must-Fail {Confirm-UnpublishedDraft $draftFixture $false 'v9.8.7'} 'Previously published release cannot be overwritten even if converted to draft'
$draftFixture.published_at=$null;$draftFixture.draft=$false
Must-Fail {Confirm-UnpublishedDraft $draftFixture $false 'v9.8.7'} 'Published release is immutable'
$releaseTokens=$null;$releaseErrors=$null
$releaseAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/Release.ps1'),[ref]$releaseTokens,[ref]$releaseErrors)
$confirm=$releaseAst.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Confirm-Packages'},$true)|Select-Object -First 1
if(-not $confirm -or $releaseErrors){throw 'Не найден gate проверки GitHub-пакетов.'}
. ([scriptblock]::Create($confirm.Extent.Text))
$repository='gromit7/ClipShot';$tag='v9.8.7'
$packagePaths=@(foreach($prefix in 'ClipShot','ClipShot-Win10','ClipShot-Win11'){Join-Path $evidenceRoot "$prefix-Setup-9.8.7.exe"})
$draft=@{draft=$true;assets=@(foreach($path in $packagePaths){$item=Get-Item -LiteralPath $path;@{name=$item.Name;state='uploaded';size=$item.Length;digest=('sha256:'+(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant());browser_download_url=('https://github.com/gromit7/ClipShot/releases/download/untagged-test/'+$item.Name)}})}
Confirm-Packages $draft $packagePaths
Assert $true 'Draft untagged URLs pass byte and SHA checks before publication'
$draft.draft=$false
Must-Fail {Confirm-Packages $draft $packagePaths} 'Published release rejects draft URLs'
foreach($asset in $draft.assets){$asset.browser_download_url="https://github.com/$repository/releases/download/$tag/$($asset.name)"}
Confirm-Packages $draft $packagePaths
Assert $true 'Published release verifies all three strict official URLs'
$draft.assets[1].size++
Must-Fail {Confirm-Packages $draft $packagePaths} 'Any flavored asset size mismatch blocks release'
# Python разбирает точный embedded publisher из production-сценария; запросы
# подменены BytesIO. Linux flock/symlink в Windows-фикстуре не проверяются.
if(-not $Python){$pythonCommand=Get-Command python -ErrorAction SilentlyContinue;if($pythonCommand){$Python=$pythonCommand.Source}}
if(-not $Python){throw 'Для локальных проверок зеркала укажите -Python <python.exe> или CLIPSHOT_PYTHON.'}
$mirror=Get-Content -LiteralPath (Join-Path $repo 'scripts/Publish-Mirror.ps1') -Raw
$match=[regex]::Match($mirror,'(?s)\$python=@''\r?\n(.*?)\r?\n''@')
if(-not $match.Success){throw 'Не найден встроенный publisher зеркала.'}
$mirrorSource=Join-Path $fixture 'mirror-source.py'
[IO.File]::WriteAllText($mirrorSource,$match.Groups[1].Value,[Text.UTF8Encoding]::new($false))
& $Python (Join-Path $repo 'scripts/Test-WindowsVariants.Mirror.py') $mirrorSource
if($LASTEXITCODE -ne 0){throw 'Проверки зеркала не прошли.'}
Write-Output ('Фикстуры: '+$fixture)
}finally{
    if($null -ne $savedFixture){$global:ClipShotVariantsFixture=$savedFixture.Value}else{Remove-Variable -Name ClipShotVariantsFixture -Scope Global -ErrorAction SilentlyContinue}
}
