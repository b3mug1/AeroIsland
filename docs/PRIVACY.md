# Privacy

AeroIsland has no telemetry, account or network synchronization. Clipboard history is off on first run. When enabled, text and downscaled images are stored in `%LOCALAPPDATA%\AeroIsland`. Settings and history metadata are JSON; images are PNG files. Pinned entries remain until explicitly deleted or cleared. Pause stops new captures; Clear All deletes saved entries and their image files.

Private-key markers and the `ExcludeClipboardContentFromMonitorProcessing` clipboard format are filtered where available. This cannot reliably identify all passwords or confidential data. Copying a history entry restores it to the Windows clipboard, where other local applications may read it. The user should pause history before copying secrets.

Uninstall preserves the data directory. To erase it, exit AeroIsland and delete `%LOCALAPPDATA%\AeroIsland` manually.
