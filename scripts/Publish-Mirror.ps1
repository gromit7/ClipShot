param(
 [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
 [string]$RemoteExecutorPath=$env:CLIPSHOT_MIRROR_EXECUTOR,
 [switch]$CheckOnly
)
$ErrorActionPreference='Stop'
if(-not $RemoteExecutorPath -or -not(Test-Path -LiteralPath $RemoteExecutorPath -PathType Leaf)){throw 'Set CLIPSHOT_MIRROR_EXECUTOR to the private, configured Gateway executor.'}
# Публичный файл не содержит SSH-реквизитов. Исполнитель получает только команду.
# Latest JSON/ZIP переключаются одним symlink после проверки официального SHA.
function Installer-Names($Release,[string]$ReleaseVersion){
    @("ClipShot-Setup-$ReleaseVersion.exe","ClipShot-Win10-Setup-$ReleaseVersion.exe","ClipShot-Win11-Setup-$ReleaseVersion.exe")
    if(@($Release.assets|Where-Object name -CEQ "ClipShot-$ReleaseVersion.exe").Count){"ClipShot-$ReleaseVersion.exe"}
}

function Confirm-LocalRelease([string]$Json,[string]$ReleaseVersion){
    if([Text.Encoding]::UTF8.GetByteCount($Json) -gt 1048576){throw 'Release metadata too large.'}
    try{$release=$Json|ConvertFrom-Json}catch{throw 'Invalid local release metadata.'}
    if($release.draft -isnot [bool] -or $release.draft -or $release.prerelease -isnot [bool] -or $release.prerelease -or $release.tag_name -cne "v$ReleaseVersion"){throw 'Official published stable release required.'}
    foreach($name in (Installer-Names $release $ReleaseVersion)){
        $assets=@($release.assets|Where-Object name -CEQ $name)
        if($assets.Count -ne 1){throw 'One official installer per OS required.'}
        $asset=$assets[0]
        if($asset.state -cne 'uploaded' -or $asset.browser_download_url -cne "https://github.com/gromit7/ClipShot/releases/download/v$ReleaseVersion/$name" -or
           $asset.digest -cnotmatch '^sha256:[a-fA-F0-9]{64}$' -or $asset.size -isnot [ValueType] -or $asset.size -lt 1024 -or $asset.size -gt 262144000 -or
           "$($asset.id)" -notmatch '^[1-9][0-9]*$'){throw 'Invalid official installer metadata.'}
    }
    return $release
}

function Confirm-AssetRedirect([System.Net.Http.HttpResponseMessage]$Response){
    # Ошибки авторизации, JSON вместо asset и неожиданные редиректы не допускаются.
    # Не включаем Location, token или HTTP body в сообщения об ошибках.
    if([int]$Response.StatusCode -ne 302){throw 'GitHub asset API did not issue the required redirect.'}
    $location=$Response.Headers.Location
    if($null -eq $location -or -not $location.IsAbsoluteUri -or $location.Scheme -cne 'https' -or
       $location.Host -cne 'release-assets.githubusercontent.com' -or -not $location.IsDefaultPort -or
       $location.UserInfo.Length -ne 0 -or $location.Fragment.Length -ne 0){throw 'GitHub asset API returned an unapproved redirect.'}
    return $location.AbsoluteUri
}

function Get-PrivateAssetUrls($Release,[string]$ReleaseVersion){
    $token=$null;$handler=$null;$client=$null
    try{
        $tokenLines=@(& gh auth token --hostname github.com 2>$null)
        if($LASTEXITCODE -ne 0 -or $tokenLines.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$tokenLines[0])){throw 'GitHub authentication is unavailable.'}
        $token=[string]$tokenLines[0];$tokenLines=$null
        $handler=[System.Net.Http.HttpClientHandler]::new();$handler.AllowAutoRedirect=$false
        $client=[System.Net.Http.HttpClient]::new($handler);$client.Timeout=[TimeSpan]::FromSeconds(30)
        $urls=@{}
        foreach($name in (Installer-Names $Release $ReleaseVersion)){
            $asset=@($Release.assets|Where-Object name -CEQ $name)[0]
            # The endpoint is constructed locally from a validated numeric asset ID.
            $request=[System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get,"https://api.github.com/repos/gromit7/ClipShot/releases/assets/$($asset.id)")
            $response=$null
            try{
                $request.Headers.Authorization=[System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$token)
                $request.Headers.UserAgent.ParseAdd('ClipShot-Mirror/1.0')
                $request.Headers.Accept.ParseAdd('application/octet-stream')
                $request.Headers.Add('X-GitHub-Api-Version','2022-11-28')
                $response=$client.SendAsync($request,[System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
                $urls[$name]=Confirm-AssetRedirect $response
            }finally{if($null -ne $response){$response.Dispose()};$request.Dispose()}
        }
        return $urls
    }catch{throw 'Unable to obtain private release asset download locations.'}
    finally{
        if($null -ne $client){$client.Dispose()}elseif($null -ne $handler){$handler.Dispose()}
        $token=$null;$tokenLines=$null
    }
}

# gh keeps its existing local authentication. Neither its token nor any auth header
# is transmitted to the VDS. Release JSON contains only ordinary release metadata.
$releaseLines=@(& gh api "repos/gromit7/ClipShot/releases/tags/v$Version" 2>$null)
if($LASTEXITCODE -ne 0){throw 'Authenticated local GitHub release metadata request failed.'}
$releaseJson=$releaseLines -join "`n";$releaseLines=$null
$localRelease=Confirm-LocalRelease $releaseJson $Version
$downloadUrls=if($CheckOnly){@{}}else{Get-PrivateAssetUrls $localRelease $Version}
$python=@'
#!/usr/bin/env python3
"""Publish a verified versioned package and atomically switch the latest pair."""
import base64, fcntl, hashlib, json, os, pathlib, re, shutil, sys, tempfile, urllib.request, urllib.parse, uuid, zipfile
ROOT=pathlib.Path('/var/www/clipshot')
REPO='gromit7/ClipShot'
LATEST=('latest.json','ClipShot-Latest.zip')
LOCAL_RELEASE=None
DOWNLOAD_URLS={}
def request(url):
    return urllib.request.Request(url,headers={'User-Agent':'ClipShot-Mirror/1.0'})
def stream_hash(stream):
    digest=hashlib.sha256()
    for block in iter(lambda:stream.read(1048576),b''):digest.update(block)
    return digest.hexdigest()
def sha256(path):
    with path.open('rb') as stream:return stream_hash(stream)
def valid_zip(path,name,size,digest):
    try:
        with zipfile.ZipFile(path) as archive:
            if archive.namelist()!=[name] or archive.getinfo(name).file_size!=size:return False
            with archive.open(name) as stream:return stream_hash(stream)==digest
    except (OSError,zipfile.BadZipFile,KeyError):return False
def link(target,path):
    temporary=path.with_name(path.name+'.new-'+uuid.uuid4().hex)
    try:temporary.symlink_to(target);os.replace(temporary,path)
    finally:
        if temporary.is_symlink():temporary.unlink()
def switch_latest(bundle):
    current=ROOT/'current'
    if not current.is_symlink():
        if current.exists():raise ValueError('Unexpected current path')
        present=[(ROOT/name).is_file() and not (ROOT/name).is_symlink() for name in LATEST]
        if any(present) and not all(present):raise ValueError('Incomplete legacy latest pair')
        if all(present):
            bootstrap=pathlib.Path(tempfile.mkdtemp(prefix='bootstrap-',dir=ROOT/'.latest'))
            for name in LATEST:os.link(ROOT/name,bootstrap/name)
            bootstrap.chmod(0o755);link(str(bootstrap.relative_to(ROOT)),current)
        else:link(str(bundle.relative_to(ROOT)),current)
    if ROOT.resolve() not in current.resolve().parents:raise ValueError('Unsafe current link')
    for name in LATEST:
        public=ROOT/name;expected='current/'+name
        if public.is_symlink() and os.readlink(public)!=expected:raise ValueError('Unexpected public link')
        if not public.is_symlink():link(expected,public)
    link(str(bundle.relative_to(ROOT)),current)
def official_packages(release,version):
    packages=[]
    for flavor,prefix in [('legacy','ClipShot'),('win10','ClipShot-Win10'),('win11','ClipShot-Win11')]:
        name=f'{prefix}-Setup-{version}.exe'
        assets=[asset for asset in release['assets'] if asset.get('name')==name]
        if len(assets)!=1:raise ValueError('One official installer per OS required: '+flavor)
        asset=assets[0];url=f'https://github.com/{REPO}/releases/download/v{version}/{name}'
        digest=asset.get('digest','');size=asset.get('size',0)
        if asset.get('state')!='uploaded' or asset.get('browser_download_url')!=url or not re.fullmatch(r'sha256:[a-fA-F0-9]{64}',digest) or not 1024<=size<=262144000:raise ValueError('Invalid official asset: '+flavor)
        packages.append({'flavor':flavor,'name':name,'size':size,'sha256':digest[7:].lower(),'url':url})
    unified=f'ClipShot-{version}.exe'
    aliases=[asset for asset in release['assets'] if asset.get('name')==unified]
    if aliases:
        if len(aliases)!=1:raise ValueError('Duplicate universal installer')
        alias=aliases[0];legacy=packages[0];url=f'https://github.com/{REPO}/releases/download/v{version}/{unified}'
        if alias.get('state')!='uploaded' or alias.get('browser_download_url')!=url or alias.get('size')!=legacy['size'] or alias.get('digest','').lower()!='sha256:'+legacy['sha256']:raise ValueError('Universal installer must match verified legacy bytes')
        packages.append({'flavor':'universal','name':unified,'size':legacy['size'],'sha256':legacy['sha256'],'url':url})
    return packages

def package_valid(root,package):
    path=root/package['name']
    return path.is_file() and not path.is_symlink() and path.stat().st_size==package['size'] and sha256(path)==package['sha256']

def download_package(root,package):
    target=root/package['name'];total=0
    url=DOWNLOAD_URLS.get(package['name'])
    if LOCAL_RELEASE is not None:
        if not isinstance(url,str):raise ValueError('Missing ephemeral asset download location')
        parsed=urllib.parse.urlsplit(url)
        if parsed.scheme!='https' or parsed.hostname!='release-assets.githubusercontent.com' or parsed.port not in (None,443) or parsed.username is not None or parsed.password is not None or parsed.fragment:raise ValueError('Unapproved ephemeral asset download location')
    else:url=package['url'] # Offline producer fixtures keep the original mockable path.
    with urllib.request.urlopen(request(url),timeout=30) as response,target.open('wb') as output:
        for block in iter(lambda:response.read(1048576),b''):
            total+=len(block)
            if total>package['size']:raise ValueError('Installer exceeds official size')
            output.write(block)
        output.flush();os.fsync(output.fileno())
    if not package_valid(root,package):raise ValueError('Installer differs from official release: '+package['flavor'])
    target.chmod(0o644)

def main(version,mode):
    if not re.fullmatch(r'\d+\.\d+\.\d+',version) or mode not in ('check','publish'):raise ValueError('Invalid arguments')
    tag='v'+version
    if LOCAL_RELEASE is None:
        with urllib.request.urlopen(request(f'https://api.github.com/repos/{REPO}/releases/tags/{tag}'),timeout=20) as response:raw=response.read(1048577)
    else:raw=json.dumps(LOCAL_RELEASE,separators=(',',':')).encode('utf-8')
    if len(raw)>1048576:raise ValueError('Release metadata too large')
    release=json.loads(raw)
    if release.get('draft') is not False or release.get('prerelease') is not False or release.get('tag_name')!=tag:raise ValueError('Official published stable release required')
    packages=official_packages(release,version);legacy=packages[0]
    name=legacy['name'];size=legacy['size'];expected=legacy['sha256'];destination=ROOT/tag
    existing=all(package_valid(destination,package) for package in packages)
    report=[{**package,'url':f'https://bot.data-clouds.ru/clipshot/{tag}/{package["name"]}','verified':package_valid(destination,package)} for package in packages]
    if mode=='check':
        print(json.dumps({'verification':'clipshot-mirror-v1','mode':mode,'version':version,'official_release':True,'size':size,'sha256':expected,'packages':report,'mirror_present_and_valid':existing,'latest_zip_valid':valid_zip(ROOT/LATEST[1],name,size,expected)}));return
    ROOT.mkdir(mode=0o755,parents=True,exist_ok=True)
    with (ROOT/'.publish.lock').open('a') as lock:
        fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
        if destination.exists() and (destination.is_symlink() or not existing):raise ValueError('Refusing to overwrite immutable version')
        if not existing:
            staging=pathlib.Path(tempfile.mkdtemp(prefix='.publish-',dir=ROOT))
            try:
                # All three EXEs become visible in one directory rename. A failed
                # transfer cannot switch latest or expose half a package family.
                for package in packages:download_package(staging,package)
                staging.chmod(0o755);staging.rename(destination)
            finally:
                if staging.exists():
                    if staging.parent.resolve()!=ROOT.resolve() or not staging.name.startswith('.publish-'):raise ValueError('Unsafe cleanup')
                    shutil.rmtree(staging)
        if not all(package_valid(destination,package) for package in packages):raise ValueError('Incomplete OS package family')
        bundles=ROOT/'.latest';bundles.mkdir(mode=0o755,exist_ok=True);bundle=bundles/tag
        if not bundle.exists():
            temporary=pathlib.Path(tempfile.mkdtemp(prefix='.bundle-',dir=bundles))
            try:
                (temporary/LATEST[0]).write_bytes(raw)
                # Existing bootstrap clients expect a single universal EXE in ZIP.
                # OS-aware clients use versioned EXEs already verified above.
                with zipfile.ZipFile(temporary/LATEST[1],'w',compression=zipfile.ZIP_STORED) as archive:archive.write(destination/name,arcname=name)
                if not valid_zip(temporary/LATEST[1],name,size,expected):raise ValueError('Invalid latest ZIP')
                for name2 in LATEST:(temporary/name2).chmod(0o644)
                temporary.chmod(0o755);temporary.rename(bundle)
            finally:
                if temporary.exists():
                    if temporary.parent.resolve()!=bundles.resolve() or not temporary.name.startswith('.bundle-'):raise ValueError('Unsafe bundle cleanup')
                    shutil.rmtree(temporary)
        if not valid_zip(bundle/LATEST[1],name,size,expected):raise ValueError('Invalid existing bundle')
        cached=json.loads((bundle/LATEST[0]).read_bytes())
        if cached.get('tag_name')!=tag or official_packages(cached,version)!=packages:raise ValueError('Inconsistent latest metadata')
        switch_latest(bundle)
        if not valid_zip(ROOT/LATEST[1],name,size,expected) or json.loads((ROOT/LATEST[0]).read_bytes()).get('tag_name')!=tag:raise ValueError('Published latest verification failed')
        report=[{**package,'url':f'https://bot.data-clouds.ru/clipshot/{tag}/{package["name"]}','verified':True} for package in packages]
        print(json.dumps({'verification':'clipshot-mirror-v1','mode':mode,'version':version,'verified_package':True,'latest_zip_verified':True,'latest_json_tag':tag,'size':size,'sha256':expected,'packages':report,'zip_sha256':sha256(ROOT/LATEST[1]),'url':f'https://bot.data-clouds.ru/clipshot/{tag}/{name}','latest_url':'https://bot.data-clouds.ru/clipshot/ClipShot-Latest.zip'}))
if __name__=='__main__':
    try:main(sys.argv[1],sys.argv[2])
    except Exception:
        # Do not expose signed URLs through urllib exceptions/tracebacks.
        print('Mirror publication failed; no private request details disclosed.',file=sys.stderr)
        sys.exit(1)
'@
$releaseBase64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($releaseJson))
$urlsBase64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($downloadUrls|ConvertTo-Json -Compress)))
$python=$python.Replace('LOCAL_RELEASE=None',"LOCAL_RELEASE=json.loads(base64.b64decode('$releaseBase64'))")
$python=$python.Replace('DOWNLOAD_URLS={}',"DOWNLOAD_URLS=json.loads(base64.b64decode('$urlsBase64'))")
$downloadUrls=$null;$urlsBase64=$null
$mode=if($CheckOnly){'check'}else{'publish'}
$command="python3 - '$Version' '$mode' <<'CLIPSHOT_MIRROR_PUBLISH'`n"+$python.Replace("`r`n","`n")+"`nCLIPSHOT_MIRROR_PUBLISH"
try{$output=@(& $RemoteExecutorPath -Command $command)}
finally{$command=$null;$python=$null}
$executorCode=$LASTEXITCODE
if($null -ne $executorCode -and $executorCode -ne 0){throw 'Mirror executor failed.'}
$text=($output|ForEach-Object { [string]$_ }) -join [Environment]::NewLine
$sshResults=[regex]::Matches($text,'(?m)^ssh_exit\s+([^\r\n]+)')
if($sshResults.Count -eq 0){throw 'Remote SSH exit verification is missing.'}
foreach($result in $sshResults){if($result.Groups[1].Value.Trim() -ne '0'){throw 'Remote mirror command failed.'}}
$records=@([regex]::Matches($text,'(?m)^\{[^\r\n]*\}\s*$')|ForEach-Object {try{$_.Value|ConvertFrom-Json}catch{}})
$record=$records|Where-Object { $_.verification -eq 'clipshot-mirror-v1' -and $_.version -eq $Version -and $_.mode -eq $mode }|Select-Object -Last 1
if(-not $record -or $record.sha256 -notmatch '^[a-f0-9]{64}$'){throw 'Remote mirror verification result is missing.'}
$hasUniversal=@((Installer-Names ($releaseJson|ConvertFrom-Json) $Version)).Count -eq 4
if(@($record.packages).Count -ne $(if($hasUniversal){4}else{3})){throw 'Mirror verification must include all published installers.'}
foreach($flavor in 'legacy','win10','win11'){
    $prefix=switch($flavor){'legacy'{'ClipShot'};'win10'{'ClipShot-Win10'};'win11'{'ClipShot-Win11'}}
    $matching=@($record.packages | Where-Object flavor -eq $flavor)
    if($matching.Count -ne 1 -or $matching[0].name -cne "$prefix-Setup-$Version.exe" -or $matching[0].sha256 -notmatch '^[a-f0-9]{64}$' -or $matching[0].size -lt 1024 -or $matching[0].size -gt 262144000 -or $matching[0].url -cne "https://bot.data-clouds.ru/clipshot/v$Version/$prefix-Setup-$Version.exe"){throw "Invalid mirror OS package: $flavor"}
    if(-not $CheckOnly -and -not $matching[0].verified){throw "Unverified mirrored OS package: $flavor"}
}
if($hasUniversal){
    $alias=@($record.packages|Where-Object flavor -eq 'universal');$legacy=@($record.packages|Where-Object flavor -eq 'legacy')[0]
    if($alias.Count -ne 1 -or $alias[0].name -cne "ClipShot-$Version.exe" -or $alias[0].size -ne $legacy.size -or $alias[0].sha256 -cne $legacy.sha256 -or $alias[0].url -cne "https://bot.data-clouds.ru/clipshot/v$Version/ClipShot-$Version.exe" -or (-not $CheckOnly -and -not $alias[0].verified)){throw 'Unverified universal installer alias.'}
}
if(-not $CheckOnly -and (-not $record.verified_package -or -not $record.latest_zip_verified -or $record.latest_json_tag -ne "v$Version")){throw 'Published installer/latest pair is not verified.'}
$record|ConvertTo-Json -Compress
