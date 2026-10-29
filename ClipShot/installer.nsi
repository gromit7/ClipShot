Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "FileFunc.nsh"
!ifndef VERSION
!define VERSION "0.7.4"
!endif
!ifndef PUBLICATION_DIR
!define PUBLICATION_DIR "..\ClipShot-app"
!endif
!ifndef FLAVOR
!define FLAVOR "legacy"
!endif
; Все обёртки включают один и тот же проверенный payload. Различаются имя,
; понятное назначение установщика и обязательная проверка реальной сборки ОС.
!if "${FLAVOR}" == "win10"
!define OUTPUT_NAME "ClipShot-Win10-Setup-${VERSION}.exe"
!define OS_LABEL "Windows 10 x64 (сборки 17763–21999)"
!define MIN_BUILD 17763
!define MAX_BUILD 21999
!else if "${FLAVOR}" == "win11"
!define OUTPUT_NAME "ClipShot-Win11-Setup-${VERSION}.exe"
!define OS_LABEL "Windows 11 x64 (сборка 22000 или новее)"
!define MIN_BUILD 22000
!define MAX_BUILD 0
!else if "${FLAVOR}" == "legacy"
!define OUTPUT_NAME "ClipShot-Setup-${VERSION}.exe"
!define OS_LABEL "Windows 10/11 x64 (сборка 17763 или новее)"
!define MIN_BUILD 17763
!define MAX_BUILD 0
!else
!error "Unknown installer FLAVOR"
!endif
Var TestRoot
Var OsBuild
Var OsAllowed
Var InstallResult
Var InstallMutex
Var UpdaterToken
Var AutomaticInstall
Var StartupInstall
Var InstallBackup
Var FailedInstall
Var OriginalInstall
Var TransactionPrepared
Var TransactionCommitted
Var BridgeTrusted
Var BridgeId
Var BridgeDirectory
Var BridgeToken
Var BridgeReady
Var BridgeTokenHandle
Var BridgeReadyHandle
Var BridgePid
Var BridgeArgs
Var UpdaterDirectory
Name "ClipShot"
OutFile "..\${OUTPUT_NAME}"
InstallDir "$LOCALAPPDATA\Programs\ClipShot"
InstallDirRegKey HKCU "Software\ClipNest" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "ClipShot"
Icon "Assets\ClipShot.ico"
UninstallIcon "Assets\ClipShot.ico"
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1049 "ProductName" "ClipShot"
VIAddVersionKey /LANG=1049 "FileDescription" "Установка ClipShot — ${OS_LABEL}"
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
  StrCpy $BridgeTrusted "0"
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
  IfErrors check_test_os
    ${If} $TestRoot == ""
      SetErrorLevel 2
      Abort
    ${EndIf}
    FileOpen $0 "$TestRoot\installer-language.txt" w
    FileWriteUTF16LE /BOM $0 "$(InstallerWelcomeTitle)$\r$\n$(InstallerWelcomeText)"
    FileClose $0
    SetErrorLevel 0
    Quit
  check_test_os:
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/OSCHECK" $1
  IfErrors normal_install
    ${If} $TestRoot == ""
      SetErrorLevel 2
      Abort
    ${EndIf}
    ${GetOptions} $0 "/OSBUILD=" $OsBuild
    Call CheckOsBuild
    FileOpen $0 "$TestRoot\installer-oscheck.txt" w
    FileWriteUTF16LE /BOM $0 "Flavor=${FLAVOR}; Build=$OsBuild; Allowed=$OsAllowed"
    FileClose $0
    ${If} $OsAllowed == "1"
      SetErrorLevel 0
    ${Else}
      SetErrorLevel 2
    ${EndIf}
    Quit
  normal_install:
  ; /OSBUILD разрешён только в диагностике выше и никогда не подменяет
  ; настоящую ОС при установке. Неподходящий пакет не закрывает приложение.
  ${WinVerGetBuild} $OsBuild
  Call CheckOsBuild
  ${IfNot} ${RunningX64}
    StrCpy $OsAllowed "0"
  ${EndIf}
  ${If} $OsAllowed != "1"
    IfSilent +2
      MessageBox MB_ICONSTOP "Этот установщик предназначен для ${OS_LABEL}. Выберите пакет ClipShot для вашей версии Windows."
    SetErrorLevel 2
    Abort
  ${EndIf}
  ${GetParameters} $0
  ${GetOptions} $0 "/UPDATERTOKEN=" $UpdaterToken
  ClearErrors
  ${GetOptions} $0 "/AUTOMATIC" $1
  StrCpy $AutomaticInstall "0"
  ${IfNot} ${Errors}
    StrCpy $AutomaticInstall "1"
  ${EndIf}
  ClearErrors
  ${GetOptions} $0 "/STARTUP" $1
  StrCpy $StartupInstall "0"
  ${IfNot} ${Errors}
    StrCpy $StartupInstall "1"
  ${EndIf}
  StrCpy $OriginalInstall "0"
  StrCpy $TransactionPrepared "0"
  StrCpy $TransactionCommitted "0"
  StrCpy $3 "Local\ClipShot.Setup.v1"
  ${If} $TestRoot != ""
    StrCpy $3 "Local\ClipShot.Test.Setup.v1"
  ${EndIf}
  ; ?e сохраняет GetLastError до следующих вызовов System-плагина.
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w r3) p.r0 ?e'
  Pop $1
  StrCpy $InstallMutex $0
  ${If} $InstallMutex == 0
    SetErrorLevel 4
    Abort
  ${EndIf}
  ${If} $1 == 183
    ${If} $UpdaterToken != ""
      StrCpy $2 $UpdaterToken 29
      ${If} $2 == "Local\ClipShot.Setup.Handoff."
        System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "$UpdaterToken") p.r2'
        ${If} $2 != 0
          System::Call 'kernel32::CloseHandle(p r2)'
          StrCpy $BridgeTrusted "1"
          Goto setup_allowed
        ${EndIf}
      ${EndIf}
    ${EndIf}
    IfSilent +2
      MessageBox MB_OK "Установка ClipShot уже выполняется."
    SetErrorLevel 4
    Abort
  ${EndIf}
  setup_allowed:
  Call ApplyUpdaterDirectory
  Call BridgeLegacyUpdater
  SetOutPath "$TEMP"
FunctionEnd

; Только подтверждённый updater вправе заменить папку из старого InstallDirRegKey.
; При тестировании маршрут остаётся в отдельной ветке TestRoot\App.
Function ApplyUpdaterDirectory
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/APPDIR=" $UpdaterDirectory
  ${If} ${Errors}
    Return
  ${EndIf}
  ${If} $BridgeTrusted != "1"
    SetErrorLevel 2
    Abort
  ${EndIf}
  GetFullPathName $UpdaterDirectory $UpdaterDirectory
  ${If} $TestRoot != ""
    ${If} $UpdaterDirectory != "$TestRoot\App"
      StrCpy $1 "$TestRoot\App\"
      StrLen $0 $1
      StrCpy $2 $UpdaterDirectory $0
      ${If} $2 != $1
        SetErrorLevel 2
        Abort
      ${EndIf}
    ${EndIf}
  ${EndIf}
  IfFileExists "$UpdaterDirectory\ClipShot.exe" target_valid
    SetErrorLevel 2
    Abort
  target_valid:
  StrCpy $INSTDIR $UpdaterDirectory
  StrCpy $InstallResult "updated"
FunctionEnd
; Старый runner удерживает CWD внутри INSTDIR и мешает атомарному Rename.
; Новый helper принимает проверенный пакет до ACK, затем ждёт выхода старого
; runner. Код4 заставляет старый runner штатно отпустить блокировки и выйти.
Function BridgeLegacyUpdater
  ${If} $BridgeTrusted != "1"
    Return
  ${EndIf}
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/UPDATERREADY" $1
  ${IfNot} ${Errors}
    Return
  ${EndIf}
  System::Call 'ole32::CoCreateGuid(g .r0) i.r1'
  ${If} $1 != 0
    SetErrorLevel 4
    Quit
  ${EndIf}
  StrCpy $BridgeId $0 8 1
  StrCpy $1 $0 4 10
  StrCpy $BridgeId "$BridgeId$1"
  StrCpy $1 $0 4 15
  StrCpy $BridgeId "$BridgeId$1"
  StrCpy $1 $0 4 20
  StrCpy $BridgeId "$BridgeId$1"
  StrCpy $1 $0 12 25
  StrCpy $BridgeId "$BridgeId$1"
  StrCpy $BridgeDirectory "$LOCALAPPDATA\ClipNest\Updates\bootstrap-$BridgeId"
  ${If} $TestRoot != ""
    StrCpy $BridgeDirectory "$TestRoot\Updates\bootstrap-$BridgeId"
  ${EndIf}
  StrCpy $BridgeToken "Local\ClipShot.UpdateBridge.Token.$BridgeId"
  StrCpy $BridgeReady "Local\ClipShot.UpdateBridge.Ready.$BridgeId"
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "$BridgeToken") p.r0'
  StrCpy $BridgeTokenHandle $0
  System::Call 'kernel32::CreateEventW(p 0, i 1, i 0, w "$BridgeReady") p.r0'
  StrCpy $BridgeReadyHandle $0
  ${If} $BridgeTokenHandle == 0
  ${OrIf} $BridgeReadyHandle == 0
    SetErrorLevel 4
    Quit
  ${EndIf}
  SetOutPath "$BridgeDirectory"
  ClearErrors
  File /oname=ClipShot.Updater.exe "${PUBLICATION_DIR}\Updater\ClipShot.Updater.exe"
  ${If} ${Errors}
    SetErrorLevel 4
    Quit
  ${EndIf}
  System::Call 'kernel32::GetCurrentProcessId() i.r0'
  StrCpy $BridgePid $0
  StrCpy $BridgeArgs '"$BridgeDirectory\ClipShot.Updater.exe" --bridge "$EXEPATH" "$INSTDIR\ClipShot.exe" "$BridgeToken" "$BridgeReady" "$BridgePid"'
  ${If} $TestRoot != ""
    StrCpy $BridgeArgs '$BridgeArgs "--test-root=$TestRoot" --test-health'
  ${EndIf}
  ${If} $AutomaticInstall == "1"
    StrCpy $BridgeArgs '$BridgeArgs --automatic'
  ${EndIf}
  ${If} $StartupInstall == "1"
    StrCpy $BridgeArgs '$BridgeArgs --startup'
  ${EndIf}
  ClearErrors
  Exec $BridgeArgs
  ${IfNot} ${Errors}
    System::Call 'kernel32::WaitForSingleObject(p $BridgeReadyHandle, i 30000) i.r0'
    ${If} $0 == 0
      DetailPrint "Передача обновления совместимому обновлятору завершена."
    ${EndIf}
  ${EndIf}
  System::Call 'kernel32::CloseHandle(p $BridgeReadyHandle)'
  System::Call 'kernel32::CloseHandle(p $BridgeTokenHandle)'
  SetErrorLevel 4
  Quit
FunctionEnd
Function CheckOsBuild
  StrCpy $OsAllowed "1"
  ${If} $OsBuild < ${MIN_BUILD}
    StrCpy $OsAllowed "0"
  ${EndIf}
  !if ${MAX_BUILD} > 0
  ${If} $OsBuild > ${MAX_BUILD}
    StrCpy $OsAllowed "0"
  ${EndIf}
  !endif
FunctionEnd

; Сначала сохраняем исходный каталог целиком. Если его нельзя переименовать,
; поставляемые файлы ещё не затронуты: блокировка DLL не оставит смешанную версию.
Function PrepareInstallTransaction
  System::Call 'ole32::CoCreateGuid(g .r0) i.r1'
  ${If} $1 != 0
    SetErrorLevel 3
    Abort "Не удалось подготовить безопасное обновление. Файлы программы не изменены."
  ${EndIf}
  StrCpy $InstallBackup "$INSTDIR.ClipShot-backup-$0"
  StrCpy $FailedInstall "$INSTDIR.ClipShot-failed-$0"
  IfFileExists "$INSTDIR\." 0 create_install_directory
    ClearErrors
    Rename "$INSTDIR" "$InstallBackup"
    ${If} ${Errors}
      SetErrorLevel 3
      Abort "Не удалось сохранить предыдущую установку. Закройте ClipShot и повторите. Файлы программы не изменены."
    ${EndIf}
    StrCpy $OriginalInstall "1"
  create_install_directory:
  StrCpy $TransactionPrepared "1"
  ClearErrors
  CreateDirectory "$INSTDIR"
  IfErrors transaction_prepare_failed
  ${If} $OriginalInstall == "1"
    ; Сохраняем посторонние файлы и вложенные каталоги. Старую резервную копию
    ; не удаляем: рекурсивное удаление пользовательского каталога не требуется.
    ClearErrors
    CopyFiles /SILENT "$InstallBackup\*.*" "$INSTDIR"
    IfErrors transaction_prepare_failed
  ${EndIf}
  Return
  transaction_prepare_failed:
    Call RollbackInstallTransaction
    SetErrorLevel 3
    Abort "Не удалось подготовить новую установку. Предыдущая версия сохранена."
FunctionEnd

Function RollbackInstallTransaction
  ${If} $TransactionPrepared != "1"
    Return
  ${EndIf}
  ${If} $TransactionCommitted == "1"
    Return
  ${EndIf}
  SetOutPath "$TEMP"
  ; Частичную новую папку изолируем переименованием, без удаления её содержимого.
  IfFileExists "$INSTDIR\." 0 restore_original_install
    ClearErrors
    Rename "$INSTDIR" "$FailedInstall"
    ${If} ${Errors}
      IfSilent +2
        MessageBox MB_ICONSTOP "Не удалось убрать незавершённую установку. Предыдущая версия сохранена в $InstallBackup. Закройте программы, использующие файлы ClipShot, и повторите установку."
      SetErrorLevel 3
      Return
    ${EndIf}
  restore_original_install:
  ${If} $OriginalInstall == "1"
    ClearErrors
    Rename "$InstallBackup" "$INSTDIR"
    ${If} ${Errors}
      IfSilent +2
        MessageBox MB_ICONSTOP "Предыдущая версия сохранена в $InstallBackup, но не удалось вернуть её каталог. Повторите установку после закрытия программ, использующих ClipShot."
      SetErrorLevel 3
      Return
    ${EndIf}
  ${EndIf}
  StrCpy $TransactionPrepared "0"
FunctionEnd

Function .onInstFailed
  ${If} $TransactionPrepared == "1"
    Call RollbackInstallTransaction
    SetErrorLevel 3
  ${EndIf}
FunctionEnd

Function CloseClipShot
  StrCpy $2 "ClipNest.Quit.v1"
  StrCpy $3 "Local\ClipNest.Local.v1"
  ${If} $TestRoot != ""
    StrCpy $2 "ClipNest.Test.Quit.v1"
    StrCpy $3 "Local\ClipNest.Test.v1"
  ${EndIf}
  StrCpy $4 0
  ${If} $AutomaticInstall == "1"
  ${OrIf} $StartupInstall == "1"
    StrCpy $2 "ClipNest.UpdateQuit.v1"
    ${If} $TestRoot != ""
      StrCpy $2 "ClipNest.Test.UpdateQuit.v1"
    ${EndIf}
    System::Call 'kernel32::GetTickCount() i.r4'
    IntOp $4 $4 + 5000
  ${EndIf}
  System::Call 'user32::RegisterWindowMessageW(w r2) i.r0'
  StrCpy $5 0
  ${If} $StartupInstall == "1"
    StrCpy $5 1
  ${EndIf}
  System::Call 'user32::PostMessageW(p 0xffff, i r0, p r4, p r5)'
  StrCpy $1 0
  wait_for_exit:
  Sleep 100
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w r3) p.r0'
  ${If} $0 != 0
    System::Call 'kernel32::CloseHandle(p r0)'
    IntOp $1 $1 + 1
    IntCmp $1 50 show_close_prompt wait_for_exit show_close_prompt
    show_close_prompt:
    IfSilent 0 +3
      SetErrorLevel 4
      Abort
    MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "Закройте запущенный ClipShot через меню трея → Выход, затем нажмите ОК." IDOK retry IDCANCEL stop
    retry:
    Call CloseClipShot
    Return
    stop:
    Abort
  ${EndIf}
FunctionEnd

!include "DesktopShortcut.nsh"

Section "Приложение ClipShot" SecMain
  SectionIn RO
  Call SnapshotDesktopShortcut
  ${If} $PreviousInstallation == "1"
    StrCpy $InstallResult "updated"
  ${EndIf}
  Call CloseClipShot
  Call PrepareInstallTransaction
  SetOutPath "$INSTDIR"
  SetOverwrite on
  ; The previous executable name is retired only after its process has exited.
  Delete "$INSTDIR\ClipNest.exe"
  Delete "$INSTDIR\ClipNest.dll"
  Delete "$INSTDIR\ClipNest.deps.json"
  Delete "$INSTDIR\ClipNest.runtimeconfig.json"
  Delete "$INSTDIR\ClipNest.pri"
  ClearErrors
  File /r "${PUBLICATION_DIR}\*.*"
  IfErrors integrity_failed
  nsExec::ExecToStack '"$INSTDIR\Updater\ClipShot.Updater.exe" --verify-install "$INSTDIR"'
  Pop $0
  Pop $1
  StrCmp $0 "0" integrity_ok
  integrity_failed:
    ; NSIS сначала возвращает целый прежний каталог, затем сообщает об отказе.
    SetOutPath "$TEMP"
    Call RollbackInstallTransaction
    SetErrorLevel 3
    Abort "Файлы программы не прошли проверку. Установка не завершена."
  integrity_ok:
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
  StrCpy $TransactionCommitted "1"
SectionEnd

Section "Ярлык на рабочем столе" SecDesktop
  Call ApplyDesktopShortcut
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
