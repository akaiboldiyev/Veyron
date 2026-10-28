# Veyron v1

A Windows desktop panel based on the Cyclop project. Veyron opens from the top edge of the screen and stays in the notification area, ready when you need it.

*English · [Русский](README.ru.md)*

## Install without a terminal

1. Download `Veyron-v1-Installer.zip` from the [Veyron v1 release](https://github.com/akaiboldiyev/Veyron/releases/latest) and extract the whole archive.
2. Double-click `Install-Veyron.vbs`.
3. Veyron installs for your Windows account, opens, and appears in Start as **Veyron v1**.

Veyron starts when you sign in to Windows. To turn that off, right-click its notification-area icon and clear **Запускать при входе в Windows**. Choose **Выход из Veyron** to close it for the current session. You can also turn startup off from **Veyron → Settings**, or in **Windows Settings → Apps → Startup**.

To uninstall, run `Uninstall-Veyron.vbs` from the extracted installer folder or use **Windows Settings → Apps → Installed apps**. Your saved data stays in `%LOCALAPPDATA%\Cyclop`.

> The downloadable package is signed with a per-build development certificate. The one-click installer trusts that certificate for your Windows account. Public distribution requires a publisher certificate trusted by Windows.

## Features

- Yandex Music player controls and track details.
- File shelf with drag and drop, previews, and screenshot-folder watching.
- Persistent clipboard history and reusable text snippets.
- Offline Russian ↔ English translation with Argos Translate.
- Optional utilities and settings in a compact floating panel.

## Requirements

- Windows 10 version 1809 or newer, or Windows 11.
- x64 Windows.

The installer includes the app runtime; end users do not need .NET or a terminal.

## Build from source

For contributors with PowerShell and the .NET 8 SDK:

```powershell
.\Windows\Build-Windows.ps1
```

The installer ZIP is written to `build/Windows/Veyron-v1-Installer.zip`. The project also retains the original macOS reference sources in `Sources/Cyclop`.
