param([string]$Python=$env:CLIPSHOT_PYTHON)
$ErrorActionPreference='Stop'
$sourcePath=Join-Path $PSScriptRoot 'Publish-Mirror.ps1'
$source=Get-Content -LiteralPath $sourcePath -Raw
$errors=$null;$tokens=$null
$ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Publisher has PowerShell parser errors.'}
foreach($name in 'Confirm-LocalRelease','Confirm-AssetRedirect','Get-PrivateAssetUrls'){
    $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    if($null -eq $function){throw 'Missing publisher validation function.'}
    Invoke-Expression $function.Extent.Text
}
function Check($condition,[string]$message){if(-not $condition){throw $message};Write-Output "PASS $message"}
function Rejects([scriptblock]$run,[string]$message){$failed=$false;try{&$run|Out-Null}catch{$failed=$true};Check $failed $message}
$release=@{draft=$false;prerelease=$false;tag_name='v9.8.7';assets=@()}
foreach($prefix in 'ClipShot','ClipShot-Win10','ClipShot-Win11'){
    $name="$prefix-Setup-9.8.7.exe"
    $release.assets+=@{id=100+$release.assets.Count;name=$name;state='uploaded';size=4096;digest='sha256:'+('a'*64);browser_download_url="https://github.com/gromit7/ClipShot/releases/download/v9.8.7/$name"}
}
$json=$release|ConvertTo-Json -Depth 8 -Compress
Check ((Confirm-LocalRelease $json '9.8.7').assets.Count -eq 3) 'Authenticated private release metadata validates locally'
$bad=$json|ConvertFrom-Json;$bad.assets[0].id='../foreign'
Rejects {Confirm-LocalRelease ($bad|ConvertTo-Json -Depth 8) '9.8.7'} 'Non-numeric asset ID fails before authenticated HTTP'
$bad=$json|ConvertFrom-Json;$bad.assets[0].browser_download_url='https://example.test/foreign.exe'
Rejects {Confirm-LocalRelease ($bad|ConvertTo-Json -Depth 8) '9.8.7'} 'Foreign browser URL is never trusted as official metadata'
$response=[Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]::Found)
try{
    $response.Headers.Location=[uri]'https://release-assets.githubusercontent.com/fake/asset?signature=synthetic'
    Check ((Confirm-AssetRedirect $response) -ceq $response.Headers.Location.AbsoluteUri) 'Only approved HTTPS asset redirect is returned in memory'
    foreach($uri in 'http://release-assets.githubusercontent.com/fake','https://evil.test/fake','https://release-assets.githubusercontent.com:444/fake','https://user@release-assets.githubusercontent.com/fake','https://release-assets.githubusercontent.com/fake#fragment','/relative'){
        $response.Headers.Location=[uri]$uri
        Rejects {Confirm-AssetRedirect $response} 'Unapproved redirect fails closed without disclosing its URL'
    }
    foreach($status in 200,301,307,401,403,404){
        $response.StatusCode=[Net.HttpStatusCode]$status
        Rejects {Confirm-AssetRedirect $response} 'Non-302 asset response fails closed'
    }
}finally{$response.Dispose()}
# A subprocess-scoped gh stub rejects authentication before any HTTP client exists.
function gh { $global:LASTEXITCODE=1; return 'synthetic-output-that-must-not-leak' }
$caught=$null
try{Get-PrivateAssetUrls ($json|ConvertFrom-Json) '9.8.7'|Out-Null}catch{$caught=$_.Exception.Message}
Check ($caught -ceq 'Unable to obtain private release asset download locations.') 'Authentication failure is sanitized and makes no asset request'

if(-not $Python){$Python=(Get-Command python -ErrorAction Stop).Source}
$match=[regex]::Match($source,'(?s)\$python=@''\r?\n(.*?)\r?\n''@')
if(-not $match.Success){throw 'Missing embedded Python publisher.'}
$fixture=Join-Path (Split-Path $PSScriptRoot) ('.tools/private-mirror-tests/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force|Out-Null
$embedded=Join-Path $fixture 'mirror-source.py'
[IO.File]::WriteAllText($embedded,$match.Groups[1].Value,[Text.UTF8Encoding]::new($false))
# This file contains literal fixture defaults, never real signed URLs or tokens.
& $Python (Join-Path $PSScriptRoot 'Test-WindowsVariants.Mirror.py') $embedded
if($LASTEXITCODE -ne 0){throw 'Legacy producer compatibility fixture failed.'}
$privateChecks=@'
import contextlib, hashlib, io, json, pathlib, shutil, sys, tempfile, types
shim=types.ModuleType('fcntl');shim.LOCK_EX=1;shim.LOCK_NB=2;shim.flock=lambda *args:None;sys.modules['fcntl']=shim
module=types.ModuleType('private_producer_fixture')
exec(compile(pathlib.Path(sys.argv[1]).read_text(encoding='utf-8'),'embedded-mirror.py','exec'),module.__dict__)
root=pathlib.Path(tempfile.mkdtemp(prefix='private-producer-',dir=pathlib.Path(sys.argv[1]).parent));module.ROOT=root
module.switch_latest=lambda bundle:[shutil.copyfile(bundle/name,root/name) for name in module.LATEST]
release={'draft':False,'prerelease':False,'tag_name':'v9.8.7','assets':[]};payloads={};requests=[]
for prefix in ('ClipShot','ClipShot-Win10','ClipShot-Win11'):
    name=f'{prefix}-Setup-9.8.7.exe';payload=(name.encode()*4096)[:4096];payloads[name]=payload
    release['assets'].append({'id':100+len(release['assets']),'name':name,'state':'uploaded','size':len(payload),'digest':'sha256:'+hashlib.sha256(payload).hexdigest(),'browser_download_url':f'https://github.com/gromit7/ClipShot/releases/download/v9.8.7/{name}'})
module.LOCAL_RELEASE=release
module.DOWNLOAD_URLS={name:f'https://release-assets.githubusercontent.com/fake/{name}?signature=SYNTHETIC_PRIVATE_SENTINEL' for name in payloads}
def response(request,timeout):
    url=request.full_url;requests.append(url)
    assert url in module.DOWNLOAD_URLS.values(), 'Embedded private mode attempted an unauthenticated GitHub request'
    name=module.urllib.parse.urlsplit(url).path.rsplit('/',1)[1]
    return io.BytesIO(payloads[name])
module.urllib.request.urlopen=response
captured=io.StringIO()
with contextlib.redirect_stdout(captured):module.main('9.8.7','check')
assert requests==[], 'CheckOnly unexpectedly requested a presigned URL'
with contextlib.redirect_stdout(captured):module.main('9.8.7','publish')
assert len(requests)==3
assert 'SYNTHETIC_PRIVATE_SENTINEL' not in captured.getvalue()
for path in root.rglob('*'):
    if path.is_file():assert b'SYNTHETIC_PRIVATE_SENTINEL' not in path.read_bytes(), 'Signed URL persisted in artifact'
print('PASS Embedded private metadata uses only ephemeral asset URLs; CheckOnly has no network; URLs absent from output/artifacts')
module.DOWNLOAD_URLS={}
try:module.download_package(root,{'name':'absent.exe','size':4096,'sha256':'a'*64,'url':'https://github.com/foreign','flavor':'legacy'})
except ValueError:pass
else:raise AssertionError('Missing presign unexpectedly fell back to GitHub')
print('PASS Missing private download location fails closed without unauthenticated fallback')
shutil.rmtree(root)
'@
& $Python -c $privateChecks $embedded
if($LASTEXITCODE -ne 0){throw 'Private producer fixture failed.'}
Write-Output 'Private mirror producer fixtures passed; no network, SSH, credentials or production mutation.'
