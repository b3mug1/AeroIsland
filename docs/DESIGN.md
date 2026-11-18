# Dynamic Island interface

The WPF overlay uses a black, borderless capsule (200 × 44 DIPs by default), with an empty idle state, artwork and a waveform during playback, and an orange countdown during focus sessions. The expanded music player uses artwork, metadata, elapsed and remaining time, vector transport controls, and the existing Windows media-session commands. Its overflow button opens the utility pages. Multiple media sessions retain a source selector.

Expansion uses a critically damped spring curve for both dimensions. Placement stays centered on the selected monitor. The rounded visual clip and native hit-test radius follow the actual window size. Reduced motion disables the spring, content fades, and waveform animation. Hover expansion and initial display do not request activation; explicit clicks and shortcuts can activate interactive controls.

Existing light-theme utility pages, accent choices, clipboard history, tray actions, focus timer, media source selection, and monitor settings remain available. The overlay and player stay black. A stopwatch is available on the Focus page.

## Activity sources

- Music: Windows global media sessions.
- Timer and stopwatch: in-app controls.
- Clipboard events: existing local history integration.
- Battery: Windows power-status changes on devices with a battery.
- Volume and mute: default multimedia render endpoint, sampled every 750 ms. No audio is recorded or changed.
- Microphone, screen recording, downloads, and notifications: presentation support through `MainWindow.ReportActivity`; automatic discovery of other applications' events is not implemented. These states should only be reported by an integration that knows the operation is active. The microphone view is not a system privacy indicator.

Adapters may call the following API from any thread:

```csharp
window.ReportActivity(new LiveActivity(
    LiveActivityKind.Download, "Downloading report", "report.pdf", 0.62));
// Update with another report as progress changes; remove on completion.
window.DismissLiveActivity();
```

Battery, volume, and notification presentations expire after four seconds. Other activity kinds persist until dismissed or replaced. One external activity is displayed at a time. Text lengths and progress values are bounded before presentation. No external message listener or notification-access permission is introduced.

## Validation

Run `dotnet test AeroIsland.slnx -c Release --no-restore`. To export static WPF renders, set `AEROISLAND_PREVIEW_DIR` to an absolute output directory before running tests. Layout coverage includes utility pages, compact playback, focus and clipboard states, expanded playback, and every live-activity kind. Static renders do not validate real-time animation, mixed-DPI transitions, or physical audio/power changes; those require desktop acceptance testing.
