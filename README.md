# AeroIsland

AeroIsland is a Windows 10/11 x64 desktop capsule built with C#, WPF and .NET 10. It stays near the top of the selected display and opens a compact panel for media sessions, local clipboard history and settings. No account, browser extension or telemetry is used.

## Status

Release x64 build and nine automated tests passed on the authoring Windows host with .NET SDK 10.0.403. Version 0.2.0 adds a contextual capsule, a focus timer, compact music artwork and progress, quick clipboard access, and short activity events. Expansion uses longer, smoother easing; reduced motion disables transitions. Static WPF renders of the main, focus, clipboard, music, settings, and compact states were inspected. Process-start, single-instance, and silent install/uninstall smoke checks passed. Live desktop interaction, mixed-DPI, real media playback, clipboard notifications, and performance acceptance scenarios remain unverified.

Download the [AeroIsland 0.2.0 installer](https://github.com/b3mug1/AeroIsland/releases/download/v0.2.0/AeroIsland-Setup-0.2.0-win-x64.exe). It requires the .NET 10 Desktop Runtime.

## Build

Install the .NET 10 SDK on Windows 10 19041+ or Windows 11. From the repository root:

```powershell
dotnet restore AeroIsland.slnx
dotnet build AeroIsland.slnx -c Release
dotnet test AeroIsland.slnx -c Release
dotnet publish src/AeroIsland.App/AeroIsland.App.csproj -c Release -r win-x64 --self-contained false
```

Run `src/AeroIsland.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/AeroIsland.App.exe`. The framework-dependent build needs the .NET 10 Desktop Runtime. For a larger self-contained package, publish with `--self-contained true`; trimming, AOT and single-file publishing are deliberately not enabled.

To build the per-user installer, install [Inno Setup 6](https://jrsoftware.org/isdl.php), then run its compiler from the repository root:

```powershell
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' installer\AeroIsland.iss
```

Adjust the compiler path if Inno Setup is installed elsewhere. The current installer is `installer/output/AeroIsland-Setup-0.2.0-win-x64.exe` (SHA-256 `4E630F95A000E18E8EB8E8A984C59BA6ED50534F4B4032EC87F94C700C7E0733`). The installer is unsigned.

## Publishing on GitHub

The repository contains source code and build instructions. Build outputs, local NuGet packages, runtime data and the installer are excluded by `.gitignore`. Upload the installer as an asset of a GitHub Release if you want people to download it; it is not included when they clone the repository. The installer requires the .NET 10 Desktop Runtime.

No `LICENSE` file is included. Select a license before presenting this repository as open source.

## Use

- Click the capsule or press Ctrl+Alt+Space to expand. Escape or clicking elsewhere collapses it.
- The compact capsule follows the active focus timer or playing media. Clicking it opens the related controls.
- Start a 5, 25 or 50 minute focus session on the Focus page. Pause or resume from the compact capsule.
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
