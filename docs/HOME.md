# Разработка ClipShot на другом компьютере

Windows 11 x64, Git, PowerShell 7 и .NET SDK 10.

```powershell
git clone https://github.com/gromit7/ClipShot.git
cd ClipShot
./scripts/Setup.ps1
./scripts/Build.ps1 -Test -Publish
./ClipShot-app/ClipShot.exe
```

Setup устанавливает локальный SDK при необходимости, восстанавливает пакеты и не меняет аккаунты Git/GitHub.

При одновременных правках использовать разные ветки и pull request. Перед началом получить последние изменения, не перезаписывая незакоммиченные файлы.

Запрос для Codex:

> Продолжаем ClipShot — буфер обмена и скриншоты для Windows 11. Прочитай AGENTS.md и docs/STATUS.md, проверь git status. Не читай и не копируй личную базу буфера. Проверяй на синтетических данных по docs/TESTING.md. Выполни задачу до проверенного результата; перед выдачей установщика запусти scripts/Test-Release.ps1.

Изолированная проверка интерфейса: `./ClipShot-app/ClipShot.exe --demo --theme=Dark`. Обычный запуск без флагов работает с личной историей. Git не синхронизирует буфер между ПК.
