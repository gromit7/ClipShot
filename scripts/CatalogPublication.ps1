# Каталог — дополнительная публикация уже выпущенного stable-пакета.
# Отдельные функции позволяют проверить отказы без GitHub, HTTP и SSH.
function Get-ClipShotCatalogRelease([string]$Version) {
    $raw = & gh api "repos/gromit7/ClipShot/releases/tags/v$Version"
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось подтвердить официальный выпуск GitHub.' }
    try { $raw | ConvertFrom-Json -ErrorAction Stop } catch { throw 'GitHub вернул некорректные метаданные выпуска.' }
}

function Get-ClipShotTransportDigest([string]$Url,[long]$ExpectedSize,[string]$Executor) {
    if($Url -cnotmatch '^https://bot\.data-clouds\.ru/clipshot/v(\d+\.\d+\.\d+)/ClipShot-\1\.exe$' -or $ExpectedSize -lt 1 -or $ExpectedSize -gt 262144000){throw 'Invalid public package digest request'}
    if(-not(Test-Path -LiteralPath $Executor -PathType Leaf)){throw 'Digest executor is missing'}
    $id=[guid]::NewGuid().ToString('N')
    $request=@{url=$Url;bytes=$ExpectedSize;request_id=$id}|ConvertTo-Json -Compress
    $encoded=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($request))
    $python=@'
import base64,hashlib,json,urllib.request
REQUEST_B64='REQUEST_VALUE'
r=json.loads(base64.b64decode(REQUEST_B64))
class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args):return None
digest=hashlib.sha256();size=0
with urllib.request.build_opener(NoRedirect()).open(r['url'],timeout=90) as response:
    assert response.status==200
    while True:
        block=response.read(131072)
        if not block:break
        size+=len(block)
        assert size<=r['bytes']
        digest.update(block)
assert size==r['bytes']
print(json.dumps({'verification':'clipshot-https-digest-v1','request_id':r['request_id'],'url':r['url'],'size':size,'sha256':digest.hexdigest(),'complete':True}))
'@
    $command="python3 - <<'CLIPSHOT_HTTPS_DIGEST'`n"+$python.Replace('REQUEST_VALUE',$encoded).Replace("`r`n","`n")+"`nCLIPSHOT_HTTPS_DIGEST`n"
    $oldExit=$global:LASTEXITCODE
    try{$global:LASTEXITCODE=0;$output=@(& $Executor -Command $command);$code=$LASTEXITCODE}finally{$global:LASTEXITCODE=$oldExit}
    if($code -ne 0){throw 'Public HTTPS digest executor failed'}
    $text=($output|ForEach-Object{[string]$_}) -join "`n"
    $ssh=@([regex]::Matches($text,'(?m)^ssh_exit\s+([^\r\n]+)'))
    if($ssh.Count -eq 0 -or @($ssh|Where-Object {$_.Groups[1].Value.Trim() -ne '0'}).Count){throw 'Digest SSH completion is not verified'}
    $records=@([regex]::Matches($text,'(?m)^\{[^\r\n]*\}\s*$')|ForEach-Object{try{$_.Value|ConvertFrom-Json}catch{}}|Where-Object{$_.verification -ceq 'clipshot-https-digest-v1' -and $_.request_id -ceq $id})
    if($records.Count -ne 1 -or $records[0].url -cne $Url -or $records[0].size -ne $ExpectedSize -or $records[0].complete -ne $true -or $records[0].sha256 -cnotmatch '^[a-f0-9]{64}$'){throw 'Full HTTPS digest receipt mismatch'}
    [pscustomobject]@{Size=$records[0].size;Sha256=$records[0].sha256}
}
function Get-ClipShotPublicPackageDigest([string]$Url, [long]$ExpectedSize) {
    if($env:CLIPSHOT_MIRROR_EXECUTOR){return Get-ClipShotTransportDigest $Url $ExpectedSize $env:CLIPSHOT_MIRROR_EXECUTOR}
    Add-Type -AssemblyName System.Net.Http
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $cancel = [Threading.CancellationTokenSource]::new([TimeSpan]::FromMinutes(5))
    $response = $null; $stream = $null; $hash = [Security.Cryptography.SHA256]::Create()
    try {
        $response = $client.GetAsync($Url, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $cancel.Token).GetAwaiter().GetResult()
        if ([int]$response.StatusCode -ne 200) { throw 'Публичный пакет RF не вернул HTTP 200.' }
        $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $buffer = New-Object byte[] 131072
        $length = 0L
        while (($read = $stream.ReadAsync($buffer, 0, $buffer.Length, $cancel.Token).GetAwaiter().GetResult()) -gt 0) {
            $length += $read
            if ($length -gt $ExpectedSize) { throw 'Публичный пакет больше официального размера.' }
            [void]$hash.TransformBlock($buffer, 0, $read, $buffer, 0)
        }
        [void]$hash.TransformFinalBlock([byte[]]@(), 0, 0)
        [pscustomobject]@{ Size=$length; Sha256=([BitConverter]::ToString($hash.Hash)).Replace('-', '').ToLowerInvariant() }
    } finally {
        if ($stream) { $stream.Dispose() }; if ($response) { $response.Dispose() }
        $hash.Dispose(); $cancel.Dispose(); $client.Dispose(); $handler.Dispose()
    }
}

function Invoke-ClipShotCatalogHook([string]$Python, [string]$Hook, [string]$Version, [string]$Setup) {
    & $Python $Hook --project clipshot --version $Version --file $Setup --channel stable
    if ($LASTEXITCODE -ne 0) { throw "Hook каталога завершился с кодом $LASTEXITCODE." }
}

function Publish-ClipShotCatalog {
    param([string]$Version, [string]$Setup, [string]$Hook, [string]$Python='python', [switch]$CheckOnly)
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Нужна точная стабильная версия.' }
    $file = Get-Item -LiteralPath $Setup -ErrorAction Stop
    $name = "ClipShot-$Version.exe"
    if ($file.PSIsContainer -or $file.Name -cne $name) { throw 'В каталог разрешён только пользовательский универсальный EXE.' }
    # Python может читать/копировать пакет, но другой процесс не может заменить
    # или изменить проверяемый EXE между SHA-проверкой и импортом каталога.
    $packageLease = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
    $file.Refresh()
    if (-not (Test-Path -LiteralPath $Hook -PathType Leaf)) { throw 'Post-release hook общего каталога не найден.' }
    $release = Get-ClipShotCatalogRelease $Version
    if ($release.draft -isnot [bool] -or $release.draft -or $release.prerelease -isnot [bool] -or $release.prerelease -or
        -not $release.published_at -or $release.tag_name -cne "v$Version") { throw 'Требуется опубликованный stable release.' }
    $assets = @($release.assets | Where-Object name -CEQ $name)
    if ($assets.Count -ne 1) { throw 'Нет единственного официального пользовательского пакета.' }
    $asset = $assets[0]
    $digest = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.state -cne 'uploaded' -or $asset.size -ne $file.Length -or $asset.digest -cne "sha256:$digest" -or
        $asset.browser_download_url -cne "https://github.com/gromit7/ClipShot/releases/download/v$Version/$name") {
        throw 'Локальный пакет не совпадает с официальным выпуском.'
    }
    $public = Get-ClipShotPublicPackageDigest "https://bot.data-clouds.ru/clipshot/v$Version/$name" $file.Length
    if ($public.Size -ne $file.Length -or $public.Sha256 -cne $digest) { throw 'Публичный RF-пакет не совпадает с официальным SHA-256/размером.' }
    # Не даём изменившемуся за время HTTP-проверки файлу попасть в каталог.
    $file.Refresh()
    if ($file.Length -ne $asset.size -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -cne $digest) { throw 'Локальный пакет изменился во время проверки.' }
    if ($CheckOnly) { Write-Output "Проверен ClipShot $Version; каталог не изменялся."; return }
    Invoke-ClipShotCatalogHook $Python ([IO.Path]::GetFullPath($Hook)) $Version $file.FullName
    } finally { $packageLease.Dispose() }
}
