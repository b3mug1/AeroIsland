# AeroIsland

AeroIsland is a Windows 10/11 x64 desktop capsule built with C#, WPF and .NET 10. It stays near the top of the selected display and opens a compact panel for media sessions, local clipboard history and settings. No account, browser extension or telemetry is used.

## Status

Version 0.3.0 redesigns the overlay with compact, expanded music, and live-activity presentations. The Release build and eleven automated tests passed. Static WPF renders were inspected. Battery and volume have Windows adapters; microphone, recording, download, and notification presentations require events from an integration. See [design and integration details](docs/DESIGN.md).

Local release packages are `installer/output/AeroIsland-Setup-0.3.0-win-x64.exe` and `installer/output/AeroIsland-0.3.0-win-x64-portable.zip`. Both include the .NET runtime. Download the [AeroIsland 0.3.0 release](https://github.com/b3mug1/AeroIsland/releases/tag/v0.3.0), including the installer, portable archive, and SHA-256 checksums.


## Build

Install the .NET 10 SDK on Windows 10 19041+ or Windows 11. From the repository root:

```powershell
dotnet restore AeroIsland.slnx
dotnet build AeroIsland.slnx -c Release
dotnet test AeroIsland.slnx -c Release
dotnet publish src/AeroIsland.App/AeroIsland.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/release/0.3.0/win-x64
```

Run `artifacts/release/0.3.0/win-x64/AeroIsland.App.exe`. The self-contained package does not require a separate .NET Desktop Runtime installation. Trimming, AOT, and single-file publishing are not enabled.

To build the per-user installer, install [Inno Setup 6](https://jrsoftware.org/isdl.php), then run its compiler from the repository root:

```powershell
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' installer\AeroIsland.iss
```

Adjust the compiler path if Inno Setup is installed elsewhere. The current unsigned installer is `installer/output/AeroIsland-Setup-0.3.0-win-x64.exe`.

## Publishing on GitHub

The repository contains source code and build instructions. Build outputs, local NuGet packages, runtime data and the installer are excluded by `.gitignore`. Upload the installer as an asset of a GitHub Release if you want people to download it; it is not included when they clone the repository. Version 0.3.0 packages include the .NET runtime.

No `LICENSE` file is included. Select a license before presenting this repository as open source.

## Use

- Click the capsule or press Ctrl+Alt+Space to expand. Escape or clicking elsewhere collapses it.
- The compact capsule follows the active focus timer or playing media. Clicking it opens the related controls.
- Start a 5, 25 or 50 minute focus session on the Focus page. Click the compact capsule to pause or resume on the Focus page.
- Music controls operate only on compatible Windows media sessions and only when the session enables the command.
- Clipboard history starts **off**. Enable it on the Clipboard page. Pause, clear, pin, search and copy saved items there. Press Ctrl+Alt+V for a compact view of the three newest items; searching reaches older items.
- A saved clipboard item or finished focus session briefly expands the capsule with a short status message. The clipboard contents are not shown in the always-visible compact state.
- The tray icon offers Open, Settings, Pause Clipboard History, Start with Windows and Exit.
- Data lives in `%LOCALAPPDATA%\AeroIsland`. Uninstall leaves this user data intact.

## Architecture

`Core` contains validated settings, focus timing, and history rules. `Infrastructure` owns atomic JSON persistence. `Windows` contains the media, clipboard and Win32 adapters. `App` owns the WPF overlay, activity presentation, and tray UI. JSON history is used because the bounded item count is small; image payloads are separate PNG files. See [architecture](docs/ARCHITECTURE.md).

## Privacy and limits

Clipboard history may include sensitive content. The private-key marker filter and Windows exclusion signal are best-effort only; pause history before copying secrets. Clipboard contents never leave the device. Media support depends on the source application's Windows media session. Fullscreen detection uses the foreground window bounds; exclusive fullscreen may prevent overlays. See [privacy](docs/PRIVACY.md) and [testing](docs/TESTING.md).

## Screenshots

After a successful Windows build, launch the app, click the capsule, and use Win+Shift+S to capture Island, Music, Clipboard and Settings. Do not include private clipboard contents in screenshots.
