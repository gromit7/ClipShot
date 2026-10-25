import contextlib, hashlib, io, json, pathlib, shutil, sys, tempfile, types
shim=types.ModuleType('fcntl');shim.LOCK_EX=1;shim.LOCK_NB=2;shim.flock=lambda *args:None
sys.modules['fcntl']=shim
source=pathlib.Path(sys.argv[1]).read_text(encoding='utf-8')
module=types.ModuleType('clipshot_mirror_fixture');exec(compile(source,'embedded-mirror.py','exec'),module.__dict__)
root=pathlib.Path(tempfile.mkdtemp(prefix='mirror-fixture-',dir=pathlib.Path(sys.argv[1]).parent));module.ROOT=root
# Windows fixture exercises package publication/verification, not Linux flock or
# symbolic-link privileges. The production atomic symlink code is not modified.
switches=[]
def fake_switch(bundle):
    switches.append(bundle.name)
    for name in module.LATEST:shutil.copyfile(bundle/name,root/name)
module.switch_latest=fake_switch
payloads={};release={};requests=[]
def make_release(version):
    result={'draft':False,'prerelease':False,'tag_name':'v'+version,'assets':[]}
    for flavor,prefix in [('legacy','ClipShot'),('win10','ClipShot-Win10'),('win11','ClipShot-Win11')]:
        name=f'{prefix}-Setup-{version}.exe';payload=(flavor.encode()*4096)[:4096];payloads[name]=payload
        result['assets'].append({'name':name,'state':'uploaded','size':len(payload),'digest':'sha256:'+hashlib.sha256(payload).hexdigest(),'browser_download_url':f'https://github.com/gromit7/ClipShot/releases/download/v{version}/{name}'})
    return result
def response(request,timeout):
    url=request.full_url;requests.append(url)
    if url.startswith('https://api.github.com/'):return io.BytesIO(json.dumps(release).encode())
    return io.BytesIO(payloads[url.rsplit('/',1)[1]])
module.urllib.request.urlopen=response
count=0
def check(condition,message):
    global count
    if not condition:raise AssertionError(message)
    count+=1;print('PASS '+message)
def rejects(run,message):
    try:run()
    except ValueError:check(True,message)
    else:raise AssertionError(message)
def run(version,mode='publish'):
    stream=io.StringIO()
    with contextlib.redirect_stdout(stream):module.main(version,mode)
    return json.loads(stream.getvalue())
release=make_release('9.8.7')
check(len(module.official_packages(release,'9.8.7'))==3,'Mirror reads three exact official package names')
original=json.loads(json.dumps(release));release['assets'].pop(1)
rejects(lambda:module.official_packages(release,'9.8.7'),'Missing OS asset rejected before publication')
release=json.loads(json.dumps(original));release['assets'][1]['browser_download_url']='https://example.org/foreign.exe'
rejects(lambda:module.official_packages(release,'9.8.7'),'Foreign OS package URI rejected')
release=json.loads(json.dumps(original));release['assets'][1]['digest']='bad'
rejects(lambda:module.official_packages(release,'9.8.7'),'Invalid OS digest rejected')
release=original;record=run('9.8.7')
check(len(record['packages'])==3 and all(p['verified'] for p in record['packages']),'Publisher verifies all three mirrored packages')
check(all(module.package_valid(root/'v9.8.7',p) for p in module.official_packages(release,'9.8.7')),'Version directory contains all three matching SHA256 files')
check(module.valid_zip(root/'ClipShot-Latest.zip','ClipShot-Setup-9.8.7.exe',4096,record['sha256']),'Latest ZIP retains only universal installer')
check(json.loads((root/'latest.json').read_bytes())['tag_name']=='v9.8.7','Latest JSON changes after package family verified')
previous=(root/'latest.json').read_bytes();switched=len(switches);release=make_release('9.8.6');release['assets'][2]['digest']='sha256:'+'0'*64
rejects(lambda:run('9.8.6'),'Hash failure on final OS asset rejects complete publication')
check(not (root/'v9.8.6').exists() and not list(root.glob('.publish-*')),'Failed family leaves no public directory or staging files')
check((root/'latest.json').read_bytes()==previous and len(switches)==switched,'Failed family leaves current latest untouched')
(root/'v9.8.5').mkdir();(root/'v9.8.5'/'keep.txt').write_text('existing immutable bytes');release=make_release('9.8.5')
rejects(lambda:run('9.8.5'),'Incomplete immutable version is never overwritten')
check((root/'v9.8.5'/'keep.txt').read_text()=='existing immutable bytes','Immutable version contents preserved on rejection')
release=original;before=len([url for url in requests if '/releases/download/' in url]);run('9.8.7');after=len([url for url in requests if '/releases/download/' in url])
check(before==after,'Re-running verified family avoids downloading packages again')
record=run('9.8.7','check')
check(len(record['packages'])==3 and record['mirror_present_and_valid'] and record['latest_zip_valid'],'Check-only reports three verified packages without publication')
print(f'{count} mirror fixture checks passed; no network access')
