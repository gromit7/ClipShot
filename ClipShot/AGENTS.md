# ClipShot

Windows 11 clipboard manager. Stack: C#, .NET 10, WinUI 3, SQLite. Russian UI.

- Follow ../AGENTS.md and read ../docs/STATUS.md when resuming.
- Build from repository root: `powershell -ExecutionPolicy Bypass -File scripts/Build.ps1 -Test -Publish`.
- Installer: `./build-installer.ps1 -MakeNsis <absolute-path-to-makensis.exe>`. Installed app is per-user; no elevation required.
- UI smoke check: run published `ClipShot.exe --smoke-test`; writes only to `%LOCALAPPDATA%/ClipNest.Smoke`, does not monitor clipboard, register hotkeys, create tray icon or change autostart.
- Storage checks: `dotnet test ../ClipShot.Checks -c Release` (or the SDK installed by scripts/Setup.ps1).
- Screen selection checks: published `ClipShot.exe --capture-self-test`; `--capture-demo` for pointer selection on synthetic pixels. Never save real desktop images or clipboard contents into test output.
- Publishing must include ClipShot.pri; the csproj target copies it. A build-only launch does not verify publication.
- Normal app stores potentially sensitive user content in `%LOCALAPPDATA%/ClipNest/clips.db`. Never print, commit or copy that database into deliverables.
- Sync is out of scope for v0.1. Keep storage independent of UI.
- See README.md for current behavior and limitations.

- Release validation and test coverage: ../docs/TESTING.md. Local gate: ../scripts/Test-Release.ps1. Test installer only with /TESTROOT under TEMP/ClipShot.Tests; never run normal installer as an automated test on the working PC.
