# Veyron v1 for Windows

The Windows version of the Cyclop panel. End users install Veyron once, then open it from Start or the notification area; they do not need a terminal.

## Install and run

Download `Veyron-v1-Installer.zip` from GitHub Releases, extract it, and double-click `Install-Veyron.vbs`. The installer shows a result window, installs Veyron for the current Windows account, and opens it. The app appears in Start as **Veyron v1**.

Veyron starts when you sign in to Windows. To disable that, clear **Запускать при входе в Windows** in the Veyron tray menu or app settings, or turn it off under **Windows Settings → Apps → Startup**. **Выход из Veyron** closes it until you launch it again.

Uninstall with `Uninstall-Veyron.vbs` or **Windows Settings → Apps → Installed apps**. User settings and content remain in `%LOCALAPPDATA%\Cyclop`.

## Build

Windows 10 version 1809 or newer, PowerShell, and the .NET 8 SDK are required:

```powershell
.\Windows\Build-Windows.ps1
```

The script builds a self-contained x64 MSIX, checks that it can be unpacked, and creates `build/Windows/Veyron-v1-Installer.zip`. Local builds use a development signing certificate. Public distribution requires a trusted publisher certificate.

The original macOS reference source remains in `Sources/Cyclop`. User data stays under `%LOCALAPPDATA%\Cyclop` to preserve existing settings and history.
