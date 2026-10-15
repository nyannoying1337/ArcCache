# Windows installer

The Windows x64 Setup EXE installs Arc Cache for the current Windows user under `%LOCALAPPDATA%\Programs\Arc Cache`. It adds a Start menu shortcut, offers an optional desktop shortcut, and registers an uninstaller in Windows Settings. Installing or removing Arc Cache does not change the Intel driver, enable Windows game-specific settings, or require administrator approval.

The setup does not delete the app's per-user settings, game profiles, receipts or logs when uninstalled. It removes the installed app files and shortcuts. The ZIP remains available for portable use if you do not want to install the app.

## Build the installer

Build the self-contained app distribution and place `ArcCache.exe`, its companion files, `LICENSE`, `NOTICE.md` and `GETTING-STARTED.md` together in one payload directory. Then run this PowerShell command from the repository root:

```powershell
pwsh -File packaging/build-installer.ps1 `
  -PayloadDirectory out/ArcCache-ReleasePayload `
  -Version 2026.10.15
```

Inno Setup 6 or 7 must be installed. The script verifies the required files, compiles the setup, and writes a SHA-256 checksum beside it. It does not need administrator rights. The installer source is `packaging/ArcCache.iss`.

The Setup EXE is unsigned. Windows may show a SmartScreen warning until the publisher has established reputation or the release is signed. Do not disable Windows security checks to run it.
