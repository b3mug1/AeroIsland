# Performance

No representative performance results are available. The Release executable has been built and briefly started, but no warm-up, repeat runs, frame traces or long soak have been completed. The limits in the source prompt remain **targets**, not observed outcomes.

On a Windows 11 x64 reference machine, build a Release x64 publish outside a debugger. Record OS build, CPU, RAM, display DPI/monitor configuration, .NET runtime, configuration and sample duration. Run at least three repeats with warm-up, report median and range for Private Bytes (MiB), Working Set (MiB), CPU percent normalized to logical processor count, managed heap, GC counts, handles and threads.

Use `scripts/collect-process.ps1 -ProcessId <pid> -Seconds 600 -Output collapsed.csv` for one-second process samples. Capture collapsed idle for 10 minutes, expanded idle for 5 minutes, music with artwork for 5 minutes, 100 text and 20 image clipboard updates, 500 expand/collapse cycles and a long soak. Use WPR/PerfView for frame pacing and startup if available. This document should be updated only with real results from those runs.
