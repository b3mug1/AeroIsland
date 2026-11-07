# Testing

On the authoring Windows host with .NET SDK 10.0.403, `dotnet build AeroIsland.slnx -c Release --no-restore` passed with zero warnings/errors. `dotnet test AeroIsland.slnx -c Release --no-restore` passed: 7 unit tests, media discovery, and one STA WPF layout test covering every page, quick clipboard, focus, compact music, and transient events, zero failures/skips. Static WPF renders were inspected, including synthetic media artwork and three synthetic clipboard items. A framework-dependent `win-x64` publish succeeded. The version 0.2.0 EXE remained running after an Open signal from a second launch; the second process exited. Inno Setup 6.7.4 built the 0.2.0 installer. Silent install and uninstall each returned exit code 0, and the installed EXE was absent afterward. Live UI animation inspection could not run because the Computer Use native pipe was unavailable.

Unit tests cover settings clamping, history expiry/deduplication, private-key marker filtering, corrupt settings recovery and atomic round-trip persistence. The integration test checks that media discovery tolerates zero sessions. Broader Windows integration coverage remains to be added.

Manual acceptance checklist:

- [ ] Start/exit, tray actions, second-instance Open signal
- [ ] Capsule click, keyboard shortcut, Escape, outside-click collapse, hover
- [ ] Keyboard focus and screen reader labels
- [ ] Media discovery, play/pause, next/previous, seek, artwork, missing session
- [ ] Clipboard text, URL and image capture, pause, clear, pin, search, copy back
- [ ] Settings save/load and corruption recovery
- [ ] Multiple monitors, mixed DPI, resolution changes, disconnected monitor
- [ ] Fullscreen hide and exclusive fullscreen limitation
- [ ] Installer install, optional shortcuts, uninstall, preserved user data
- [ ] Performance scenarios in `PERFORMANCE.md`
