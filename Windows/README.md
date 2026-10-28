# Veyron v1 for Windows

The Windows version of the Cyclop panel. End users install Veyron once, then open it from Start or the notification area; they do not need a terminal.

## Install and run without a terminal

Click **[Download Veyron v1 for Windows (.zip)](https://github.com/akaiboldiyev/Veyron/releases/latest/download/Veyron-v1-Installer.zip)**. On the [latest release page](https://github.com/akaiboldiyev/Veyron/releases/latest), the same download is listed under **Assets** as `Veyron-v1-Installer.zip`. Do not download `Install-Veyron.vbs` by itself or choose **Source code**.

1. Open your browser's **Downloads** folder in File Explorer.
2. Right-click `Veyron-v1-Installer.zip` → **Extract All…** → **Extract**.
3. Open the new `Veyron-v1-Installer` folder.
4. Double-click `Install-Veyron.vbs`. Keep the `.msix` and `.cer` files beside it.
5. Launch Veyron v1 from **Start** after the installer confirms success.

If the installer says `Veyron-v1.msix` is missing, extract the complete ZIP and run the script from the extracted folder.

Veyron starts when you sign in to Windows. To disable that, clear **Запускать при входе в Windows** in the Veyron tray menu or app settings, or turn it off under **Windows Settings → Apps → Startup**. **Выход из Veyron** closes it until you launch it again.

Uninstall with `Uninstall-Veyron.vbs` or **Windows Settings → Apps → Installed apps**. User settings and content remain in `%LOCALAPPDATA%\Cyclop`.

## Build

Windows 10 version 1809 or newer, PowerShell, and the .NET 8 SDK are required:

```powershell
.\Windows\Build-Windows.ps1
```

The script builds a self-contained x64 MSIX, checks that it can be unpacked, and creates `build/Windows/Veyron-v1-Installer.zip`. Local builds use a development signing certificate. Public distribution requires a trusted publisher certificate.

The original macOS reference source remains in `Sources/Cyclop`. User data stays under `%LOCALAPPDATA%\Cyclop` to preserve existing settings and history.
