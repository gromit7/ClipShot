; Снимок выбора делается до замены файлов/реестра, в окончательном INSTDIR.
; Удалённый ярлык — выбор пользователя, а не недостающий файл приложения.
Var DesktopDirectory
Var ShortcutRegistryKey
Var PreviousInstallation
Var KeepDesktopShortcut

Function SnapshotDesktopShortcut
  StrCpy $DesktopDirectory "$DESKTOP"
  StrCpy $ShortcutRegistryKey "Software\ClipNest"
  ${If} $TestRoot != ""
    StrCpy $DesktopDirectory "$TestRoot\Desktop"
    ${GetFileName} "$TestRoot" $0
    StrCpy $ShortcutRegistryKey "Software\ClipShot.InstallerTests\$0"
  ${EndIf}
  StrCpy $PreviousInstallation "0"
  ReadRegStr $0 HKCU "$ShortcutRegistryKey" "InstallDir"
  ${If} $0 != ""
    ; Регистрация сохраняет выбор и при восстановлении повреждённой установки.
    StrCpy $PreviousInstallation "1"
  ${EndIf}
  IfFileExists "$INSTDIR\ClipShot.exe" 0 +2
    StrCpy $PreviousInstallation "1"
  IfFileExists "$INSTDIR\ClipNest.exe" 0 +2
    StrCpy $PreviousInstallation "1"
  IfFileExists "$INSTDIR\Uninstall.exe" 0 +2
    StrCpy $PreviousInstallation "1"
  StrCpy $KeepDesktopShortcut "0"
  ${If} $PreviousInstallation == "0"
    StrCpy $KeepDesktopShortcut "1"
  ${EndIf}
  IfFileExists "$DesktopDirectory\ClipShot.lnk" 0 +2
    StrCpy $KeepDesktopShortcut "1"
  IfFileExists "$DesktopDirectory\ClipNest.lnk" 0 +2
    StrCpy $KeepDesktopShortcut "1"
FunctionEnd

Function ApplyDesktopShortcut
  ${If} $KeepDesktopShortcut == "1"
    CreateDirectory "$DesktopDirectory"
    ClearErrors
    CreateShortcut "$DesktopDirectory\ClipShot.lnk" "$INSTDIR\ClipShot.exe" "" "$INSTDIR\Assets\ClipShot.ico"
    ${IfNot} ${Errors}
      ; Старое имя убираем только после успешного создания нового ярлыка.
      Delete "$DesktopDirectory\ClipNest.lnk"
    ${EndIf}
  ${EndIf}
FunctionEnd
