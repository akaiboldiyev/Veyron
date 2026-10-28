# Veyron v1

## ⬇️ [Скачать Veyron v1 для Windows — установщик](https://github.com/akaiboldiyev/Veyron/releases/latest/download/Veyron-v1-Installer.zip)

Эта ссылка загружает **один ZIP установщика**. Не выбирайте **Code → Download ZIP**: это архив исходников, не установщик.

A Windows desktop panel based on the Cyclop project. Veyron opens from the top edge of the screen and stays in the notification area, ready when you need it.

*English · [Русский](README.ru.md)*

## Install without a terminal

### Download

Click **[Download Veyron v1 for Windows (.zip)](https://github.com/akaiboldiyev/Veyron/releases/latest/download/Veyron-v1-Installer.zip)** to download the installer archive.

Or open the [latest release page](https://github.com/akaiboldiyev/Veyron/releases/latest), find **Assets**, and click **Veyron-v1-Installer.zip**. Do not download `Install-Veyron.vbs` by itself or click **Source code**.

### Install

1. Open your browser's **Downloads** folder in File Explorer.
2. Right-click `Veyron-v1-Installer.zip`, choose **Extract All…**, then click **Extract**.
3. Open the new `Veyron-v1-Installer` folder.
4. Double-click **Install-Veyron.vbs**. Leave the `App` folder beside it and wait for the success message.
5. The installer copies Veyron into your user profile, creates a Start menu shortcut, enables launch at sign-in, and starts the app. No terminal or certificate selection is needed.
6. To open it later, press **Start**, type **Veyron v1**, and select it. Its icon also appears beside the clock.

If Windows asks whether to open the downloaded script, check that it is inside the extracted `Veyron-v1-Installer` folder, then choose **Open**. If the installer says the `App` folder or `Veyron.exe` is missing, repeat step 2 and run the script from the extracted folder.

Veyron starts when you sign in to Windows. To turn that off, right-click its notification-area icon and clear **Запускать при входе в Windows**. Choose **Выход из Veyron** to close it for the current session. You can also turn startup off from **Veyron → Settings**, or in **Windows Settings → Apps → Startup**.

To uninstall, run `Uninstall-Veyron.vbs` from the extracted installer folder or use **Windows Settings → Apps → Installed apps**. Your saved data stays in `%LOCALAPPDATA%\Cyclop`.

The installer copies the ordinary app files into `%LOCALAPPDATA%\Programs\Veyron`. It does not install an MSIX package or add certificates to Windows.

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
