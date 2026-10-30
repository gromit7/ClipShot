#Requires -Version 5.1
param(
    [string]$PublicationPath=(Join-Path (Split-Path $PSScriptRoot) 'ClipShot-app'),
    [string]$ReportDirectory,
    [ValidateRange(10,600)][int]$TimeoutSeconds=90,
    [switch]$CheckOnly
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'TestWorkspace.ps1')
$fixtureScope=New-ClipShotTestScope
$fixtureGate=$null
try {
    $fixtureGate=Enter-ClipShotTestGate

$repo=Split-Path $PSScriptRoot

function Get-RuntimePlatform {
    $windows=[Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    $build=0;$revision=0;$edition='';$display='';$product=''
    if($windows){
        # Только общие сведения об ОС. Имя ПК, пользователь и пути не собираются.
        $os=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
        $build=[int]$os.CurrentBuildNumber
        if($null -ne $os.UBR){$revision=[int]$os.UBR}
        $edition=[string]$os.EditionID;$product=[string]$os.ProductName
        $display=[string]$os.DisplayVersion;if(-not $display){$display=[string]$os.ReleaseId}
    }
    # Системная архитектура из HKLM, а не окружение x64-процесса под эмуляцией ARM64.
    $native=''
    if($windows){$native=[string](Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' -Name PROCESSOR_ARCHITECTURE).PROCESSOR_ARCHITECTURE}
    $architecture=switch -Regex ($native){'^(AMD64|X64)$'{'X64';break};'^ARM64$'{'Arm64';break};default{'X86'}}
    $family=if(-not $windows){'Другая ОС'}elseif($build -ge 22000){'Windows 11'}else{'Windows 10'}
    return [ordered]@{windows=$windows;family=$family;edition=$edition;product=$product;displayVersion=$display;build=$build;revision=$revision;architecture=$architecture;powerShell=$PSVersionTable.PSVersion.ToString()}
}
function Assert-RuntimePlatform($Platform){
    if(-not $Platform.windows){throw 'unsupported-platform'}
    if($Platform.architecture -ne 'X64'){throw 'unsupported-architecture'}
    if($Platform.build -lt 17763){throw 'unsupported-build'}
}
function Get-RuntimeCases {
    # Только флаги из TestEnvironment и существующие отчёты Test-Release.
    foreach($theme in 'Gradient','Transparent','White','Black','Dark','Windows98','WindowsXP','CatPink'){
        [pscustomobject]@{Id=('theme-'+$theme);Name=('Тема '+$theme);Arguments=('--smoke-test --theme='+$theme);Report='ui-ok.txt';Theme=$theme}
    }
    [pscustomobject]@{Id='interaction';Name='Поведение окна, поиск и шаблоны';Arguments='--interaction-self-test';Report='interaction-checks.txt';Theme=''}
    [pscustomobject]@{Id='capture';Name='Синтетическое выделение и PNG';Arguments='--capture-self-test';Report='capture-checks.txt';Theme=''}
    [pscustomobject]@{Id='editor';Name='Редактор синтетического снимка';Arguments='--editor-self-test';Report='editor-checks.txt';Theme=''}
}
function Assert-RuntimeCase($Case){
    $valid=($Case.Arguments -cmatch '^--smoke-test --theme=(Gradient|Transparent|White|Black|Dark|Windows98|WindowsXP|CatPink)$') -or
        ($Case.Arguments -cin @('--interaction-self-test','--capture-self-test','--editor-self-test'))
    if(-not $valid -or $Case.Report -cnotin @('ui-ok.txt','interaction-checks.txt','capture-checks.txt','editor-checks.txt')){throw 'unsafe-test-contract'}
}
function Assert-NoReparsePoint([string]$Path){
    $item=Get-Item -LiteralPath $Path -Force
    if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'unsafe-test-directory'}
}
function New-RuntimeDataDirectory { return (New-ClipShotTestRoot $fixtureScope) }
function Get-VerifiedPublication([string]$Path){
    $root=[IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    if(-not(Test-Path -LiteralPath $root -PathType Container)){throw 'missing-publication'}
    foreach($name in 'ClipShot.exe','ClipShot.dll','ClipShot.pri','ClipShot.runtimeconfig.json','FileHashes.json','FileInventory.json','Updater/ClipShot.Updater.exe','Assets/ClipShot.ico','Assets/Selection.cur'){
        if(-not(Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)){throw 'incomplete-publication'}
    }
    $manifest=Get-Item -LiteralPath (Join-Path $root 'FileHashes.json')
    if($manifest.Length -gt 1048576){throw 'invalid-publication-manifest'}
    $hashes=Get-Content -LiteralPath $manifest.FullName -Raw -Encoding UTF8|ConvertFrom-Json
    $entries=@($hashes.PSObject.Properties)
    if($entries.Count -lt 3 -or $entries.Count -gt 5000){throw 'invalid-publication-manifest'}
    foreach($required in 'ClipShot.exe','ClipShot.dll','ClipShot.pri','Updater\ClipShot.Updater.exe'){
        if(-not($entries|Where-Object {$_.Name.Replace('/','\') -ceq $required})){throw 'invalid-publication-manifest'}
    }
    foreach($entry in $entries){
        if([IO.Path]::IsPathRooted($entry.Name) -or $entry.Name -match ':' -or [string]$entry.Value -notmatch '^[a-fA-F0-9]{64}$'){throw 'invalid-publication-manifest'}
        $file=[IO.Path]::GetFullPath((Join-Path $root $entry.Name))
        if(-not $file.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'invalid-publication-manifest'}
        # Не следуем перенаправлениям из манифеста за пределы каталога публикации.
        $ancestor=$file
        while($ancestor.Length -ge $root.Length){Assert-NoReparsePoint $ancestor;if($ancestor -eq $root){break};$ancestor=[IO.Path]::GetDirectoryName($ancestor)}
        if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine [string]$entry.Value){throw 'publication-hash-mismatch'}
    }
    $app=Join-Path $root 'ClipShot.exe'
    $version=(Get-Item -LiteralPath $app).VersionInfo.ProductVersion.Split('+')[0]
    if($version -notmatch '^\d+\.\d+\.\d+$' -or [version]$version -lt [version]'0.7.4'){throw 'unsupported-publication-version'}
    foreach($name in 'ClipShot.dll','Updater/ClipShot.Updater.exe'){
        if((Get-Item -LiteralPath (Join-Path $root $name)).VersionInfo.ProductVersion.Split('+')[0] -cne $version){throw 'mixed-publication-version'}
    }
    return [pscustomobject]@{Root=$root;App=$app;Version=$version;Sha256=(Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash.ToLowerInvariant()}
}
function Assert-NoOtherTestInstance {
    $mutex=$null
    if([Threading.Mutex]::TryOpenExisting('Local\ClipNest.Test.v1',[ref]$mutex)){$mutex.Dispose();throw 'another-test-instance'}
}
function Stop-OwnRuntimeProcess([Diagnostics.Process]$Process){
    # Только объект процесса, созданного этим сценарием; нет поиска/завершения по имени.
    if($null -ne $Process){try{if(-not $Process.HasExited){$Process.Kill();$null=$Process.WaitForExit(5000)}}catch{}}
}
function Test-RuntimeReport([string]$Directory,$Case,[datetime]$Started){
    if(Test-Path -LiteralPath (Join-Path $Directory 'test-error.txt')){throw 'unhandled-test-error'}
    $path=Join-Path $Directory $Case.Report
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw 'missing-test-report'}
    $file=Get-Item -LiteralPath $path
    if($file.Length -lt 1 -or $file.Length -gt 1048576 -or $file.LastWriteTimeUtc -lt $Started.AddSeconds(-2)){throw 'invalid-test-report'}
    $text=Get-Content -LiteralPath $path -Raw -Encoding UTF8
    $lines=@($text -split '\r?\n'|Where-Object {$_.Length -gt 0})
    if($lines.Count -eq 0 -or @($lines|Where-Object {$_ -cnotmatch '^PASS '}).Count -gt 0){throw 'failed-test-report'}
    if($Case.Theme -and $text -cnotmatch ('ThemeId='+[regex]::Escape($Case.Theme)+';')){throw 'wrong-theme'}
    if($Case.Theme -eq 'Transparent' -and $text -cnotmatch 'Acrylic=True'){throw 'missing-acrylic-backdrop'}
    return $lines.Count
}
function Invoke-RuntimeCase($Publication,$Case,[int]$Timeout){
    Assert-RuntimeCase $Case
    Assert-NoOtherTestInstance
    $directory=New-RuntimeDataDirectory
    $started=[datetime]::UtcNow;$watch=[Diagnostics.Stopwatch]::StartNew();$process=$null
    $result=[ordered]@{id=$Case.Id;name=$Case.Name;status='ошибка';seconds=0;exitCode=$null;passLines=0;errorCode=$null}
    try{
        $start=New-Object Diagnostics.ProcessStartInfo
        $start.FileName=$Publication.App;$start.WorkingDirectory=$Publication.Root
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.Arguments=$Case.Arguments
        # Окружение родительского PowerShell вообще не меняется.
        $start.EnvironmentVariables['CLIPSHOT_TEST_ROOT']=$directory
        $process=New-Object Diagnostics.Process;$process.StartInfo=$start
        if(-not $process.Start()){throw 'test-process-start-failed'}
        Register-ClipShotTestProcess $fixtureScope $process
        if(-not $process.WaitForExit($Timeout*1000)){throw 'test-process-timeout'}
        $result.exitCode=$process.ExitCode
        if($process.ExitCode -ne 0){throw 'test-process-exit-code'}
        $result.passLines=Test-RuntimeReport $directory $Case $started
        $result.status='пройдено'
    }catch{
        $result.errorCode=Get-RuntimeErrorCode $_
    }finally{
        Stop-OwnRuntimeProcess $process
        if($null -ne $process){$process.Dispose()}
        $result.seconds=[Math]::Round($watch.Elapsed.TotalSeconds,3)
    }
    return [pscustomobject]$result
}
function Get-RuntimeErrorCode($ErrorRecord){
    $allowed=@('unsupported-platform','unsupported-architecture','unsupported-build','unsafe-test-contract','unsafe-test-directory','missing-publication','incomplete-publication','invalid-publication-manifest','publication-hash-mismatch','unsupported-publication-version','mixed-publication-version','another-test-instance','unhandled-test-error','missing-test-report','invalid-test-report','failed-test-report','wrong-theme','missing-acrylic-backdrop','test-process-start-failed','test-process-timeout','test-process-exit-code','unsafe-report-directory')
    $message=[string]$ErrorRecord.Exception.Message
    if($message -cin $allowed){return $message}
    # Системная ошибка может содержать личный путь: наружу отдаётся только тип.
    return 'system-error-'+$ErrorRecord.Exception.GetType().Name
}

$report=[ordered]@{
    schema=1;startedUtc=[datetime]::UtcNow.ToString('o');mode=$(if($CheckOnly){'проверка готовности без запуска'}else{'изолированные встроенные тесты'})
    status='ошибка';platform=$null;publication=$null;checks=@();errorCode=$null
    limitations=@(
        'Результат относится только к указанным ОС, сборке, архитектуре и версии программы.'
        'Встроенные тесты используют синтетические данные. Работа с настоящим буфером обмена не проверяется.'
        'Реальные горячие клавиши, кнопки мыши, переключение физических микрофонов и автозапуск не проверяются.'
        'Разные DPI и мониторы, HDR, защищённые поверхности, RDP, сон и восстановление Explorer требуют отдельных проверок.'
        'Установка, удаление, самообновление и откат этим сценарием не проверяются.'
        'Граница сборки не является заявлением об актуальном сроке поддержки Microsoft.'
    )
}
$exitCode=1
try{
    $report.platform=Get-RuntimePlatform
    Assert-RuntimePlatform $report.platform
    $publication=Get-VerifiedPublication $PublicationPath
    $report.publication=[ordered]@{version=$publication.Version;exeSha256=$publication.Sha256}
    $cases=@(Get-RuntimeCases);foreach($case in $cases){Assert-RuntimeCase $case}
    if($CheckOnly){$report.status='готово к запуску; функциональные тесты не запускались';$exitCode=0}
    else{
        foreach($case in $cases){$report.checks+=Invoke-RuntimeCase $publication $case $TimeoutSeconds}
        if(@($report.checks|Where-Object {$_.status -ne 'пройдено'}).Count -eq 0){$report.status='встроенные тесты пройдены';$exitCode=0}
    }
}catch{$report.errorCode=Get-RuntimeErrorCode $_}
finally{
    $report.completedUtc=[datetime]::UtcNow.ToString('o')
    if(-not $ReportDirectory){$ReportDirectory=Join-Path $repo ('TestResults/WindowsRuntime/'+[datetime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N'))}
    $destination=[IO.Path]::GetFullPath($ReportDirectory)
    $source=[IO.Path]::GetFullPath($PublicationPath).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
    if($destination -eq $source -or $destination.StartsWith($source+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'unsafe-report-directory'}
    New-Item -ItemType Directory -Path $destination -Force|Out-Null
    $report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $destination 'runtime-checks.json') -Encoding UTF8
    $summary=@('ClipShot — проверка Windows',('Режим: '+$report.mode),('Результат: '+$report.status))
    if($report.platform){$summary+=('ОС: '+$report.platform.family+' '+$report.platform.edition+'; сборка '+$report.platform.build+'.'+$report.platform.revision+'; '+$report.platform.architecture)}
    if($report.publication){$summary+=('Версия ClipShot: '+$report.publication.version)}
    foreach($check in $report.checks){$summary+=($check.name+': '+$check.status+$(if($check.errorCode){' ('+$check.errorCode+')'}else{''}))}
    if($report.errorCode){$summary+=('Ошибка: '+$report.errorCode)}
    $summary+='Ограничения:';$summary+=@($report.limitations|ForEach-Object {'- '+$_})
    $summary|Set-Content -LiteralPath (Join-Path $destination 'runtime-checks.txt') -Encoding UTF8
    Write-Output ($summary -join [Environment]::NewLine)
    Write-Output 'Отчёты: runtime-checks.json и runtime-checks.txt в каталоге результатов.'
}
if($exitCode -ne 0){throw 'Проверка не пройдена. Причина указана обезличенным кодом в runtime-checks.json.'}

} finally {
    try { Complete-ClipShotTestScope $fixtureScope }
    finally { if($fixtureGate){$fixtureGate.ReleaseMutex();$fixtureGate.Dispose()} }
}
