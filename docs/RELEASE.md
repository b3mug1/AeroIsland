# Release

Minimum Windows build: Windows 10 version 2004 (19041), x64. Media session support began in Windows 10 1809, but this app targets 19041 for the .NET Windows SDK projection. Normal operation and the Inno Setup installer run per user without elevation.

Release gates completed for version 0.2.0: .NET 10 SDK build/test, framework-dependent publish, Inno Setup build, process-start/single-instance checks, silent install/uninstall, and inspection of offscreen WPF renders. Interactive UI animation, real media and clipboard integration, mixed-DPI, and three-repeat resource measurements remain open. The updated installer is `installer/output/AeroIsland-Setup-0.2.0-win-x64.exe`; code signing is optional and SmartScreen may warn.

The Inno Setup script uses the framework-dependent publish output. User data is under LocalAppData outside the installation directory and is preserved by uninstall. There is no auto-updater. Updates require a new installer package.
