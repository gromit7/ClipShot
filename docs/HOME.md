# Разработка ClipShot на другом компьютере

Целевые системы приложения: Windows 10 x64 от 1809/LTSC 2019 и Windows 11 x64. Для подготовки рабочего места разработки нужны Git, PowerShell 7 и .NET SDK 10; ограничения фактической проверки ОС — [WINDOWS-PACKAGES.md](WINDOWS-PACKAGES.md).

При продолжении сначала читать [STATUS.md](STATUS.md), затем [CHAT-CONTEXT.md](CHAT-CONTEXT.md) для решений из чатов и [FEATURE-QUEUE.md](FEATURE-QUEUE.md) для актуальной очереди. Git синхронизирует исходники, а не пользовательскую историю буфера.

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
