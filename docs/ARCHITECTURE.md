# Architecture

The WPF `MainWindow` creates a transparent, topmost, taskbar-free overlay. It uses event-driven Win32 clipboard notifications, global shortcuts for the panel and quick clipboard, display change events and Windows Global System Media Transport Controls sessions. Clipboard events trigger bounded retries. Media events refresh snapshot state; timeline-only updates do not rebuild the visual tree. A one-second slider timer runs while the music panel is visible and a session is playing. A 100 ms activity timer updates compact media and focus progress only while one of those activities is running. Short local events use a separate expiry timer.

`AppSettings.Validate` clamps persisted values before use. The focus timer uses UTC deadlines and stores paused remaining time in memory; it is not restored after app restart. `LocalStorage` writes JSON to a temporary file and replaces the target on the same volume. History images are PNG files under `%LOCALAPPDATA%\AeroIsland\images`; history JSON holds only references. Image count and total bytes are bounded during persistence. No server, database or helper process is required.

Single-instance coordination uses a local named mutex and a current-user-only named pipe. The second process sends an Open signal and exits. Native window hooks and WinRT subscriptions are removed on shutdown.

Current implementation gaps: theme follows an explicit light/dark toggle rather than Windows theme changes; the Settings page exposes only a subset of all validated settings; screen-reader coverage and click-through behavior need hands-on validation; the current programmatic view code should be split into view models before declaring full MVVM compliance. These are tracked as release blockers rather than described as complete.
