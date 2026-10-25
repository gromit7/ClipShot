param(
 [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
 [string]$RemoteExecutorPath=$env:CLIPSHOT_MIRROR_EXECUTOR,
 [switch]$CheckOnly
)
$ErrorActionPreference='Stop'
if(-not $RemoteExecutorPath -or -not(Test-Path -LiteralPath $RemoteExecutorPath -PathType Leaf)){throw 'Set CLIPSHOT_MIRROR_EXECUTOR to the private, configured Gateway executor.'}
# Публичный файл не содержит SSH-реквизитов. Исполнитель получает только команду.
# Latest JSON/ZIP переключаются одним symlink после проверки официального SHA.
$python=@'
#!/usr/bin/env python3
"""Publish a verified versioned package and atomically switch the latest pair."""
import fcntl, hashlib, json, os, pathlib, re, shutil, sys, tempfile, urllib.request, uuid, zipfile
ROOT=pathlib.Path('/var/www/clipshot')
REPO='gromit7/ClipShot'
LATEST=('latest.json','ClipShot-Latest.zip')
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
    return packages

def package_valid(root,package):
    path=root/package['name']
    return path.is_file() and not path.is_symlink() and path.stat().st_size==package['size'] and sha256(path)==package['sha256']

def download_package(root,package):
    target=root/package['name'];total=0
    with urllib.request.urlopen(request(package['url']),timeout=30) as response,target.open('wb') as output:
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
    with urllib.request.urlopen(request(f'https://api.github.com/repos/{REPO}/releases/tags/{tag}'),timeout=20) as response:raw=response.read(1048577)
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
if __name__=='__main__':main(sys.argv[1],sys.argv[2])
'@
$mode=if($CheckOnly){'check'}else{'publish'}
$command="python3 - '$Version' '$mode' <<'CLIPSHOT_MIRROR_PUBLISH'`n"+$python.Replace("`r`n","`n")+"`nCLIPSHOT_MIRROR_PUBLISH"
$output=@(& $RemoteExecutorPath -Command $command)
$executorCode=$LASTEXITCODE
if($null -ne $executorCode -and $executorCode -ne 0){throw 'Mirror executor failed.'}
$text=($output|ForEach-Object { [string]$_ }) -join [Environment]::NewLine
$sshResults=[regex]::Matches($text,'(?m)^ssh_exit\s+([^\r\n]+)')
if($sshResults.Count -eq 0){throw 'Remote SSH exit verification is missing.'}
foreach($result in $sshResults){if($result.Groups[1].Value.Trim() -ne '0'){throw 'Remote mirror command failed.'}}
$records=@([regex]::Matches($text,'(?m)^\{[^\r\n]*\}\s*$')|ForEach-Object {try{$_.Value|ConvertFrom-Json}catch{}})
$record=$records|Where-Object { $_.verification -eq 'clipshot-mirror-v1' -and $_.version -eq $Version -and $_.mode -eq $mode }|Select-Object -Last 1
if(-not $record -or $record.sha256 -notmatch '^[a-f0-9]{64}$'){throw 'Remote mirror verification result is missing.'}
if(@($record.packages).Count -ne 3){throw 'Mirror verification must include all three OS packages.'}
foreach($flavor in 'legacy','win10','win11'){
    $prefix=switch($flavor){'legacy'{'ClipShot'};'win10'{'ClipShot-Win10'};'win11'{'ClipShot-Win11'}}
    $matching=@($record.packages | Where-Object flavor -eq $flavor)
    if($matching.Count -ne 1 -or $matching[0].name -cne "$prefix-Setup-$Version.exe" -or $matching[0].sha256 -notmatch '^[a-f0-9]{64}$' -or $matching[0].size -lt 1024 -or $matching[0].size -gt 262144000 -or $matching[0].url -cne "https://bot.data-clouds.ru/clipshot/v$Version/$prefix-Setup-$Version.exe"){throw "Invalid mirror OS package: $flavor"}
    if(-not $CheckOnly -and -not $matching[0].verified){throw "Unverified mirrored OS package: $flavor"}
}
if(-not $CheckOnly -and (-not $record.verified_package -or -not $record.latest_zip_verified -or $record.latest_json_tag -ne "v$Version")){throw 'Published installer/latest pair is not verified.'}
$record|ConvertTo-Json -Compress
