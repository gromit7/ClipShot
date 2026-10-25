@echo off
chcp 65001 >nul
title ClipShot — обновление и восстановление
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-ClipShot.ps1"
if errorlevel 1 echo Обновление не завершено. Сохраните текст ошибки для диагностики.
pause
