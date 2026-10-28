# Veyron v1 for Windows

## Download

**[Download Veyron v1 — Windows installer](https://github.com/akaiboldiyev/Veyron/releases/latest/download/Veyron-v1-Installer.zip)**

This is the only download button you need. The ZIP will appear in your **Downloads** folder.

If GitHub opens a release page, scroll to **Assets** and click **Veyron-v1-Installer.zip**. Do not choose **Code → Download ZIP** or **Source code**; those are source archives, not the app.

## Install

1. Open **Downloads** in File Explorer.
2. Right-click `Veyron-v1-Installer.zip` → **Extract All…** → **Extract**.
3. Open the new `Veyron-v1-Installer` folder.
4. Double-click `Install-Veyron.vbs`. Keep the `App` folder beside this file.
5. If Windows asks whether to open the file, click **Open**. You do not need a terminal, certificate, or administrator access.
6. Wait for Veyron to launch. Later, open it from **Start → Veyron v1**. It also starts when you sign in and stays in the notification area by the clock.

The installer closes an already-running Veyron while it updates the files, then starts it again. Installing does not erase your settings or app data.

## Troubleshooting

- **“The file is in use by another process”**: download the latest installer from the button above. If the message appears again, right-click the Veyron icon by the clock → **Exit Veyron**, then run `Install-Veyron.vbs` again.
- **The ZIP has no `Install-Veyron.vbs` or `App` folder**: you downloaded the source archive. Return to the **Download Veyron v1** button above and repeat steps 2–4.
- **Windows shows a warning when opening the script**: make sure it is inside the extracted `Veyron-v1-Installer` folder, then click **Open**.

## Uninstall

Open the `Veyron-v1-Installer` folder and run `Uninstall-Veyron.vbs`. This removes the app, Start shortcut, and auto-start entry. Settings and user data in `%LOCALAPPDATA%\Cyclop` stay on your computer.

## About

Veyron is a compact Windows panel based on Cyclop. It opens from the top edge of the screen and controls Yandex Music, the file shelf, clipboard history, text snippets, and offline RU ↔ EN translation.

Requires Windows 10 version 1809 or newer, or Windows 11, x64. The installer includes the app runtime; you do not need to install .NET separately.

### Build from source

Developers need PowerShell and the .NET 8 SDK:

```powershell
.\Windows\Build-Windows.ps1
```

The installer is created at `build/Windows/Veyron-v1-Installer.zip`. The original macOS sources are in `Sources/Cyclop`.

English · [Русский](README.ru.md)
