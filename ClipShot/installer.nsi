Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "FileFunc.nsh"
!ifndef VERSION
!define VERSION "0.7.0"
!endif
Var TestRoot
Var InstallResult
Name "ClipShot"
OutFile "..\ClipShot-Setup-${VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\ClipShot"
InstallDirRegKey HKCU "Software\ClipNest" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "ClipShot"
Icon "Assets\ClipShot.ico"
UninstallIcon "Assets\ClipShot.ico"
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1049 "ProductName" "ClipShot"
VIAddVersionKey /LANG=1049 "FileDescription" "Установка ClipShot"
VIAddVersionKey /LANG=1049 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1049 "LegalCopyright" "ClipShot"
!define MUI_ABORTWARNING
!define MUI_ICON "Assets\ClipShot.ico"
!define MUI_UNICON "Assets\ClipShot.ico"
LangString InstallerWelcomeTitle 1049 "Установка и обновление ClipShot"
LangString InstallerWelcomeText 1049 "Мастер установит ClipShot на этот компьютер. Если программа уже установлена, новая версия обновит её поверх старой. История буфера, шаблоны и настройки сохраняются. Предварительно удалять ClipShot не требуется."
!define MUI_WELCOMEPAGE_TITLE "$(InstallerWelcomeTitle)"
!define MUI_WELCOMEPAGE_TEXT "$(InstallerWelcomeText)"
!define MUI_FINISHPAGE_RUN "$INSTDIR\ClipShot.exe"
!define MUI_FINISHPAGE_RUN_PARAMETERS "--background --install-result=$InstallResult"
!define MUI_FINISHPAGE_RUN_TEXT "Запустить ClipShot"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"

Function .onInit
  SetShellVarContext current
  StrCpy $InstallResult "installed"
  IfFileExists "$INSTDIR\ClipShot.exe" 0 +2
    StrCpy $InstallResult "updated"
  IfFileExists "$INSTDIR\ClipNest.exe" 0 +2
    StrCpy $InstallResult "updated"
  ${GetParameters} $0
  ${GetOptions} $0 "/TESTROOT=" $TestRoot
  ${If} $TestRoot != ""
    GetFullPathName $TestRoot $TestRoot
    System::Call 'kernel32::GetLongPathNameW(w "$TestRoot", w.r5, i ${NSIS_MAX_STRLEN}) i.r0'
    StrCpy $TestRoot $5
    System::Call 'kernel32::GetLongPathNameW(w "$TEMP", w.r4, i ${NSIS_MAX_STRLEN}) i.r0'
    StrCpy $4 "$4\ClipShot.Tests"
    StrCpy $4 "$4\"
    StrLen $0 $4
    StrCpy $1 $TestRoot $0
    ${If} $1 != $4
      SetErrorLevel 2
      Abort
    ${EndIf}
    StrCpy $INSTDIR "$TestRoot\App"
  ${EndIf}
  ; Inspect actual compiled strings without installing or touching the working app.
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/LANGCHECK" $1
  IfErrors normal_install
    ${If} $TestRoot == ""
      SetErrorLevel 2
      Abort
    ${EndIf}
    FileOpen $0 "$TestRoot\installer-language.txt" w
    FileWriteUTF16LE /BOM $0 "$(InstallerWelcomeTitle)$\r$\n$(InstallerWelcomeText)"
    FileClose $0
    SetErrorLevel 0
    Quit
  normal_install:
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Требуется Windows 11 x64."
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "Требуется Windows 11."
    Abort
  ${EndIf}
FunctionEnd

Function CloseClipShot
  StrCpy $2 "ClipNest.Quit.v1"
  StrCpy $3 "Local\ClipNest.Local.v1"
  ${If} $TestRoot != ""
    StrCpy $2 "ClipNest.Test.Quit.v1"
    StrCpy $3 "Local\ClipNest.Test.v1"
  ${EndIf}
  System::Call 'user32::RegisterWindowMessageW(w r2) i.r0'
  System::Call 'user32::PostMessageW(p 0xffff, i r0, p 0, p 0)'
  StrCpy $1 0
  wait_for_exit:
  Sleep 100
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w r3) p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    IntOp $1 $1 + 1
    IntCmp $1 50 show_close_prompt wait_for_exit show_close_prompt
    show_close_prompt:
    MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "Закройте запущенный ClipShot через меню трея → Выход, затем нажмите ОК." IDOK retry IDCANCEL stop
    retry:
    Call CloseClipShot
    Return
    stop:
    Abort
  ${EndIf}
FunctionEnd

Section "Приложение ClipShot" SecMain
  SectionIn RO
  Call CloseClipShot
  SetOutPath "$INSTDIR"
  SetOverwrite on
  ; The previous executable name is retired only after its process has exited.
  Delete "$INSTDIR\ClipNest.exe"
  Delete "$INSTDIR\ClipNest.dll"
  Delete "$INSTDIR\ClipNest.deps.json"
  Delete "$INSTDIR\ClipNest.runtimeconfig.json"
  Delete "$INSTDIR\ClipNest.pri"
  File /r "..\ClipShot-app\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  ${If} $TestRoot != ""
    FileOpen $0 "$INSTDIR\test-install-root.txt" w
    FileWrite $0 "$TestRoot"
    FileClose $0
  ${Else}
  Delete "$SMPROGRAMS\ClipNest.lnk"
  CreateShortcut "$SMPROGRAMS\ClipShot.lnk" "$INSTDIR\ClipShot.exe" "" "$INSTDIR\Assets\ClipShot.ico"
  WriteRegStr HKCU "Software\ClipNest" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipNest" "DisplayName" "ClipShot — буфер обмена и скриншоты"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipNest" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipNest" "DisplayIcon" "$INSTDIR\ClipShot.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipNest" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipNest" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipNest" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipNest" "NoRepair" 1
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "ClipNest"
  ${If} $0 == '"$INSTDIR\ClipNest.exe" --background'
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "ClipNest" '"$INSTDIR\ClipShot.exe" --background'
  ${EndIf}
  ${EndIf}
SectionEnd

Section "Ярлык на рабочем столе" SecDesktop
  ${If} $TestRoot == ""
  Delete "$DESKTOP\ClipNest.lnk"
  CreateShortcut "$DESKTOP\ClipShot.lnk" "$INSTDIR\ClipShot.exe" "" "$INSTDIR\Assets\ClipShot.ico"
  ${EndIf}
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  StrCpy $2 "ClipNest.Quit.v1"
  IfFileExists "$INSTDIR\test-install-root.txt" 0 normal_uninstall
    StrCpy $TestRoot "test"
    StrCpy $2 "ClipNest.Test.Quit.v1"
  normal_uninstall:
  System::Call 'user32::RegisterWindowMessageW(w r2) i.r0'
  System::Call 'user32::PostMessageW(p 0xffff, i r0, p 0, p 0)'
  Sleep 500
  !include "uninstall-files.nsh"
  Delete "$INSTDIR\test-install-root.txt"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  ${If} $TestRoot == ""
  Delete "$SMPROGRAMS\ClipNest.lnk"
  Delete "$DESKTOP\ClipNest.lnk"
  Delete "$SMPROGRAMS\ClipShot.lnk"
  Delete "$DESKTOP\ClipShot.lnk"
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "ClipNest"
  ${If} $0 == '"$INSTDIR\ClipShot.exe" --background'
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "ClipNest"
  ${EndIf}
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ClipNest"
  DeleteRegKey HKCU "Software\ClipNest"
  ${EndIf}
  ; History and templates in LocalAppData\ClipNest deliberately remain intact.
SectionEnd
