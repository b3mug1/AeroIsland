# Release

Minimum Windows build: Windows 10 version 2004 (19041), x64. Media session support began in Windows 10 1809, but this app targets 19041 for the .NET Windows SDK projection. Normal operation and the Inno Setup installer run per user without elevation.

Release gates completed for version 0.2.0: .NET 10 SDK build/test, framework-dependent publish, Inno Setup build, process-start/single-instance checks, silent install/uninstall, and inspection of offscreen WPF renders. Interactive UI animation, real media and clipboard integration, mixed-DPI, and three-repeat resource measurements remain open. The updated installer is `installer/output/AeroIsland-Setup-0.2.0-win-x64.exe`; code signing is optional and SmartScreen may warn.

The 0.2.0 Inno Setup package used framework-dependent publish output. Version 0.3.0 uses self-contained output. User data is under LocalAppData outside the installation directory and is preserved by uninstall. There is no auto-updater. Updates require a new installer package.

## Version 0.3.0 packages

The application and installer versions are 0.3.0. Publish with:

```powershell
dotnet publish src/AeroIsland.App/AeroIsland.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/release/0.3.0/win-x64
& '.\.tools\Inno Setup 6\ISCC.exe' installer/AeroIsland.iss
Compress-Archive -Path 'artifacts/release/0.3.0/win-x64/*' -DestinationPath 'installer/output/AeroIsland-0.3.0-win-x64-portable.zip'
```

The installer reads the version-specific publish directory. Both packages include the .NET 10 runtime. Outputs remain excluded from Git. Published packages and SHA-256 checksums are available on the [0.3.0 release page](https://github.com/b3mug1/AeroIsland/releases/tag/v0.3.0). See `docs/DESIGN.md` for behavior, integrations, and remaining desktop acceptance checks.
