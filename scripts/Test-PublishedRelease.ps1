[CmdletBinding()]
param(
    [string]$Tag,
    [uri]$MirrorRoot,
    [switch]$DownloadInstaller,
    [switch]$SelfTest
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

function Assert-Audit([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Audit: $Message" }
}
function Test-Metadata($Release, $Build, $Checks, [string]$Commit) {
    Assert-Audit ($Release.draft -eq $false -and $Release.prerelease -eq $false) 'Not a stable release'
    Assert-Audit ($Release.tag_name -cmatch '^v\d+\.\d+\.\d+$') 'Invalid tag'
    $v = $Release.tag_name.Substring(1)
    Assert-Audit ($Build.version -ceq $v -and $Checks.version -ceq $v) 'Version mismatch'
    Assert-Audit ($Commit -match '^[a-fA-F0-9]{40}$') 'Invalid commit'
    Assert-Audit ($Build.sourceCommit -eq $Commit -and $Checks.sourceCommit -eq $Commit) 'Commit mismatch'
    Assert-Audit ($Build.sourceTag -ceq $Release.tag_name) 'Build tag mismatch'
    $assets = @($Release.assets | Where-Object { $_.name -match '^ClipShot-Setup-.*\.exe$' })
    Assert-Audit ($assets.Count -eq 1) 'Expected exactly one versioned installer'
    $a = $assets[0]
    $name = "ClipShot-Setup-$v.exe"
    Assert-Audit ($a.name -ceq $name -and $Build.installer.file -ceq $name) 'Installer name mismatch'
    Assert-Audit ($a.size -ge 1024 -and $a.size -le 262144000 -and $a.size -eq $Build.installer.bytes) 'Installer size mismatch'
    Assert-Audit ($Build.installer.sha256 -match '^[a-fA-F0-9]{64}$') 'Invalid build digest'
    Assert-Audit ($a.digest -match '^sha256:[a-fA-F0-9]{64}$') 'Invalid asset digest'
    Assert-Audit ($a.digest.Substring(7) -eq $Build.installer.sha256) 'Digest mismatch'
    Assert-Audit ($a.browser_download_url -ceq "https://github.com/gromit7/ClipShot/releases/download/v$v/$name") 'Unexpected asset URL'
    Assert-Audit ($Checks.unitTests.passed -gt 0 -and $Checks.unitTests.failed -eq 0 -and $Checks.unitTests.total -eq $Checks.unitTests.passed) 'Unit tests failed or empty'
    $local = @($Checks.localReleaseChecks)
    Assert-Audit ($local.Count -gt 0) 'Local checks empty'
    foreach ($check in $local) { Assert-Audit ($check.Passed -is [bool] -and $check.Passed) 'Local check failed' }
    return $a
}
function Test-RangeMetadata([int]$Status, [string]$Range, [long]$Length, [long]$Expected) {
    Assert-Audit ($Status -eq 206) 'Range status must be 206 (redirects forbidden)'
    Assert-Audit ($Range -ceq "bytes 0-1023/$Expected") 'Invalid Content-Range'
    Assert-Audit ($Length -eq 1024) 'Range length mismatch'
}
function Read-Audit($Stream, [byte[]]$Buffer, [int]$Offset, [int]$Count) {
    $pending = $Stream.ReadAsync($Buffer, $Offset, $Count)
    if (-not $pending.Wait(15000)) { throw 'Audit: body read timed out' }
    return $pending.GetAwaiter().GetResult()
}
function Get-Json([uri]$Url, [int]$Limit = 1048576) {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $Url)
    $response = $null
    try {
        $response = $script:client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        Assert-Audit ([int]$response.StatusCode -eq 200) 'Metadata HTTP status'
        $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $memory = [IO.MemoryStream]::new()
        try {
            $buffer = New-Object byte[] 8192
            while (($n = (Read-Audit $stream $buffer 0 $buffer.Length)) -gt 0) {
                Assert-Audit ($memory.Length + $n -le $Limit) 'Metadata too large'
                $memory.Write($buffer, 0, $n)
            }
            return ([Text.Encoding]::UTF8.GetString($memory.ToArray()) | ConvertFrom-Json)
        } finally { $memory.Dispose(); $stream.Dispose() }
    } finally { if ($null -ne $response) { $response.Dispose() }; $request.Dispose() }
}
function Get-Evidence($Release, [string]$Name) {
    $items = @($Release.assets | Where-Object { $_.name -ceq $Name })
    Assert-Audit ($items.Count -eq 1) "Missing or duplicate evidence: $Name"
    $expected = "https://github.com/gromit7/ClipShot/releases/download/$($Release.tag_name)/$Name"
    Assert-Audit ($items[0].browser_download_url -ceq $expected) 'Unexpected evidence URL'
    return Get-Json ([uri]$expected)
}
function Invoke-SelfTest {
    $h = 'a' * 64; $c = 'b' * 40
    $r = @{ draft=$false; prerelease=$false; tag_name='v1.2.3'; assets=@(@{name='ClipShot-Setup-1.2.3.exe';size=2048;digest="sha256:$h";browser_download_url='https://github.com/gromit7/ClipShot/releases/download/v1.2.3/ClipShot-Setup-1.2.3.exe'}) }
    $b = @{version='1.2.3';sourceCommit=$c;sourceTag='v1.2.3';installer=@{file='ClipShot-Setup-1.2.3.exe';bytes=2048;sha256=$h}}
    $k = @{version='1.2.3';sourceCommit=$c;unitTests=@{passed=1;failed=0;total=1};localReleaseChecks=@(@{Passed=$true})}
    $null = Test-Metadata $r $b $k $c
    Test-RangeMetadata 206 'bytes 0-1023/2048' 1024 2048
    $cases = @(
        { $r.assets[0].digest='bad'; try { Test-Metadata $r $b $k $c } finally { $r.assets[0].digest="sha256:$h" } },
        { $b.sourceCommit='c'*40; try { Test-Metadata $r $b $k $c } finally { $b.sourceCommit=$c } },
        { $k.version='0.0.1'; try { Test-Metadata $r $b $k $c } finally { $k.version='1.2.3' } },
        { $k.unitTests.failed=1; try { Test-Metadata $r $b $k $c } finally { $k.unitTests.failed=0 } },
        { $k.localReleaseChecks[0].Passed=$false; try { Test-Metadata $r $b $k $c } finally { $k.localReleaseChecks[0].Passed=$true } },
        { Test-RangeMetadata 200 'bytes 0-1023/2048' 1024 2048 },
        { Test-RangeMetadata 302 '' 0 2048 },
        { Test-RangeMetadata 206 'bytes 0-1023/9999' 1024 2048 },
        { Test-RangeMetadata 206 'bytes 0-1023/2048' 5 2048 }
    )
    foreach ($case in $cases) {
        $rejected = $false
        try { $null = & $case } catch { $rejected = $true }
        Assert-Audit $rejected 'Synthetic invalid input accepted'
    }
    [pscustomobject]@{Result='PASS';SyntheticPositive=2;SyntheticNegative=$cases.Count}
}
# Dot sourcing exposes pure validators for an external test harness without network calls.
if ($MyInvocation.InvocationName -eq '.') { return }
if ($SelfTest) { Invoke-SelfTest; return }
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $true # GitHub release assets redirect to their download host.
$script:client = [Net.Http.HttpClient]::new($handler)
$script:client.Timeout = [TimeSpan]::FromSeconds(30)
$script:client.DefaultRequestHeaders.UserAgent.ParseAdd('ClipShot-PublishedReleaseAudit/1.0')
try {
    $endpoint = 'https://api.github.com/repos/gromit7/ClipShot/releases/latest'
    if ($Tag) {
        Assert-Audit ($Tag -cmatch '^v\d+\.\d+\.\d+$') 'Invalid requested tag'
        $endpoint = "https://api.github.com/repos/gromit7/ClipShot/releases/tags/$Tag"
    }
    $release = Get-Json ([uri]$endpoint)
    if ($Tag) { Assert-Audit ($release.tag_name -ceq $Tag) 'Requested tag mismatch' }
    Assert-Audit ($release.tag_name -cmatch '^v\d+\.\d+\.\d+$') 'Invalid release tag'
    $v = $release.tag_name.Substring(1)
    $build = Get-Evidence $release "ClipShot-$v-buildinfo.json"
    $checks = Get-Evidence $release "ClipShot-$v-checks.json"
    $reference = Get-Json ([uri]"https://api.github.com/repos/gromit7/ClipShot/git/ref/tags/$($release.tag_name)")
    $obj = $reference.object
    for ($depth = 0; $obj.type -eq 'tag' -and $depth -lt 5; $depth++) {
        Assert-Audit ($obj.sha -match '^[a-fA-F0-9]{40}$') 'Invalid annotated tag SHA'
        $obj = (Get-Json ([uri]"https://api.github.com/repos/gromit7/ClipShot/git/tags/$($obj.sha)")).object
    }
    Assert-Audit ($obj.type -eq 'commit') 'Tag does not resolve to commit'
    $asset = Test-Metadata $release $build $checks $obj.sha
    $mirrorResult = 'NotRequested'
    if ($MirrorRoot) {
        Assert-Audit ($MirrorRoot.Scheme -ceq 'https' -and $MirrorRoot.IsDefaultPort -and -not $MirrorRoot.UserInfo -and -not $MirrorRoot.Query -and -not $MirrorRoot.Fragment) 'Invalid mirror root'
        $mirrorUri = [uri]($MirrorRoot.AbsoluteUri.TrimEnd('/') + "/v$v/$($asset.name)")
        $mh = [Net.Http.HttpClientHandler]::new(); $mh.AllowAutoRedirect=$false
        $mc = [Net.Http.HttpClient]::new($mh); $mc.Timeout=[TimeSpan]::FromSeconds(15)
        $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,$mirrorUri)
        $request.Headers.Range=[Net.Http.Headers.RangeHeaderValue]::new(0,1023)
        $response=$null
        try {
            $response=$mc.SendAsync($request,[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            Test-RangeMetadata ([int]$response.StatusCode) ([string]$response.Content.Headers.ContentRange) ([long]$response.Content.Headers.ContentLength) ([long]$asset.size)
            $stream=$response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
            try {
                $buffer=New-Object byte[] 1025; $total=0
                while ($total -lt 1025 -and ($n=(Read-Audit $stream $buffer $total (1025-$total))) -gt 0) { $total+=$n }
                Assert-Audit ($total -eq 1024) 'Actual Range body length mismatch'
            } finally { $stream.Dispose() }
            $mirrorResult='RangeMetadataOnly'
        } finally { if($null -ne $response){$response.Dispose()};$request.Dispose();$mc.Dispose() }
    }
    $downloadPath=$null
    if ($DownloadInstaller) {
        # Always create a new directory: never accept an overwrite destination.
        $directory=Join-Path ([IO.Path]::GetTempPath()) ('ClipShot.ReleaseAudit-'+[guid]::NewGuid().ToString('N'))
        $null=[IO.Directory]::CreateDirectory($directory)
        $downloadPath=Join-Path $directory $asset.name
        $response=$client.GetAsync([uri]$asset.browser_download_url,[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try {
            Assert-Audit ([int]$response.StatusCode -eq 200) 'Installer HTTP status'
            $inputStream=$response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
            $output=[IO.File]::Open($downloadPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try {
                $buffer=New-Object byte[] 81920; [long]$total=0
                while (($n=(Read-Audit $inputStream $buffer 0 $buffer.Length)) -gt 0) {
                    $total+=$n; Assert-Audit ($total -le $asset.size) 'Installer exceeds expected size'
                    $output.Write($buffer,0,$n)
                }
                Assert-Audit ($total -eq $asset.size) 'Incomplete installer'
            } finally { $output.Dispose();$inputStream.Dispose() }
        } finally { $response.Dispose() }
        Assert-Audit ((Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash -eq $build.installer.sha256) 'Downloaded SHA mismatch'
    }
    [pscustomobject]@{Result='PASS';Version=$v;SourceCommit=$obj.sha;InstallerBytes=$asset.size;Sha256=$build.installer.sha256;Mirror=$mirrorResult;FullDownloadVerified=[bool]$DownloadInstaller;DownloadPath=$downloadPath}
} finally { $client.Dispose() }
