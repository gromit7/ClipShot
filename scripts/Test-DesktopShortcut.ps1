param([Parameter(Mandatory=$true)][string]$MakeNsis)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$root=Join-Path ([IO.Path]::GetTempPath()) ('ClipShot.Tests/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$harness=Join-Path $root 'ShortcutHarness.nsi'
$exe=Join-Path $root 'ShortcutHarness.exe'
$include=Join-Path $repo 'ClipShot/DesktopShortcut.nsh'
# Компилируем ровно функции рабочего установщика. Нет запуска ClipShot,
# сообщений другим процессам или обращения к настоящему рабочему столу.
$source=@(
    'Unicode true','!include "LogicLib.nsh"','!include "FileFunc.nsh"',
    ('OutFile "'+$exe+'"'),'RequestExecutionLevel user','SilentInstall silent',
    'Var TestRoot',('!include "'+$include+'"'),'Section',
    'SetShellVarContext current',('${GetParameters} $0'),
    '${GetOptions} $0 "/ROOT=" $TestRoot',
    '${If} $TestRoot == ""','SetErrorLevel 2','Quit','${EndIf}',
    'GetFullPathName $TestRoot $TestRoot',('GetFullPathName $1 "'+$root+'"'),'StrCpy $1 "$1\"',
    'StrLen $2 $1','StrCpy $3 $TestRoot $2',
    '${If} $3 != $1','SetErrorLevel 2','Quit','${EndIf}',
    'StrCpy $INSTDIR "$TestRoot\App"',
    'Call SnapshotDesktopShortcut','CreateDirectory "$INSTDIR"',
    'FileOpen $0 "$INSTDIR\ClipShot.exe" w','FileWrite $0 "synthetic executable"','FileClose $0',
    'Call ApplyDesktopShortcut','SectionEnd'
)
[IO.File]::WriteAllLines($harness,$source,[Text.UTF8Encoding]::new($true))
& $MakeNsis /V2 /WX /INPUTCHARSET UTF8 $harness
if($LASTEXITCODE -ne 0){throw 'Shortcut harness compilation failed'}
$shell=New-Object -ComObject WScript.Shell
$lines=[Collections.Generic.List[string]]::new()
function Run-Shortcut([string]$CaseRoot){
    New-Item -ItemType Directory -Path $CaseRoot -Force | Out-Null
    $p=Start-Process -FilePath $exe -ArgumentList ('/ROOT="'+$CaseRoot+'"') -PassThru -WindowStyle Hidden
    if(-not $p.WaitForExit(15000)){throw 'Shortcut harness timed out'}
    if($p.ExitCode -ne 0){throw 'Shortcut harness failed'}
}
function Check-Link([string]$CaseRoot){
    $link=Join-Path $CaseRoot 'Desktop/ClipShot.lnk'
    if(-not(Test-Path -LiteralPath $link)){throw 'Expected desktop shortcut is missing'}
    $shortcut=$shell.CreateShortcut($link)
    if($shortcut.TargetPath -ne (Join-Path $CaseRoot 'App/ClipShot.exe')){throw 'Shortcut targets the wrong installation'}
}
function No-Link([string]$CaseRoot){
    if(Test-Path -LiteralPath (Join-Path $CaseRoot 'Desktop/ClipShot.lnk')){throw 'Deleted shortcut was restored'}
}
try{
    $outside=$root+'-outside'
    New-Item -ItemType Directory -Path $outside | Out-Null
    foreach($invalid in @('',('/ROOT="'+$outside+'"'))){
        $start=@{FilePath=$exe;PassThru=$true;WindowStyle='Hidden'}
        if($invalid){$start.ArgumentList=$invalid}
        $p=Start-Process @start
        if(-not $p.WaitForExit(15000) -or $p.ExitCode -ne 2){throw 'Unsafe harness root was not rejected'}
    }
    if(Test-Path -LiteralPath (Join-Path $outside 'App')){throw 'Harness changed a root outside its fixture'}
    $lines.Add('PASS harness rejects missing or outside test root before touching shortcuts')
    $fresh=Join-Path $root 'fresh'
    Run-Shortcut $fresh
    Check-Link $fresh
    $lines.Add('PASS first installation creates a usable shortcut')
    $link=Join-Path $fresh 'Desktop/ClipShot.lnk'
    $shortcut=$shell.CreateShortcut($link);$shortcut.TargetPath=Join-Path $fresh 'old.exe';$shortcut.Save()
    Run-Shortcut $fresh
    Check-Link $fresh
    $lines.Add('PASS existing shortcut is refreshed to the current application')
    # Сохраняем тестовый ярлык вместо удаления. Для установщика он отсутствует.
    Move-Item -LiteralPath $link -Destination (Join-Path $fresh 'removed-shortcut.lnk')
    Run-Shortcut $fresh
    No-Link $fresh
    Run-Shortcut $fresh
    No-Link $fresh
    $lines.Add('PASS two consecutive upgrades preserve removal of the shortcut')
    foreach($withLink in $false,$true){
        $legacy=Join-Path $root ('legacy-'+$withLink)
        New-Item -ItemType Directory -Path (Join-Path $legacy 'App'),(Join-Path $legacy 'Desktop') | Out-Null
        Set-Content -LiteralPath (Join-Path $legacy 'App/ClipNest.exe') -Value 'synthetic old installation'
        if($withLink){$shortcut=$shell.CreateShortcut((Join-Path $legacy 'Desktop/ClipNest.lnk'));$shortcut.TargetPath=Join-Path $legacy 'App/ClipNest.exe';$shortcut.Save()}
        Run-Shortcut $legacy
        if($withLink){Check-Link $legacy}else{No-Link $legacy}
        if(Test-Path -LiteralPath (Join-Path $legacy 'Desktop/ClipNest.lnk')){throw 'Legacy shortcut was not migrated'}
        $lines.Add("PASS legacy ClipNest migration; existing shortcut=$withLink")
    }
    $repair=Join-Path $root ([guid]::NewGuid().ToString('N'))
    $key='HKCU:\Software\ClipShot.InstallerTests\'+(Split-Path $repair -Leaf)
    try{
        New-Item -Path $key -Force | Out-Null
        New-ItemProperty -LiteralPath $key -Name InstallDir -Value (Join-Path $repair 'old-install') -PropertyType String | Out-Null
        Run-Shortcut $repair
        No-Link $repair
        $lines.Add('PASS registered installation repair preserves the missing shortcut')
    }finally{if(Test-Path -LiteralPath $key){Remove-Item -LiteralPath $key}}
    $reports=Join-Path $repo 'TestResults'
    New-Item -ItemType Directory -Path $reports -Force | Out-Null
    $lines | Set-Content -LiteralPath (Join-Path $reports 'desktop-shortcut-checks.txt') -Encoding utf8
    $lines
}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
