using System.Diagnostics;

if (args.Length != 2 || !int.TryParse(args[0], out var processId) || !int.TryParse(args[1], out var seconds) || seconds < 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project benchmarks/AeroIsland.Benchmarks -- <pid> <seconds>");
    return 1;
}
using var process = Process.GetProcessById(processId);
Console.WriteLine("utc,private_mib,working_mib,cpu_ms,threads,handles");
var end = DateTimeOffset.UtcNow.AddSeconds(seconds);
while (DateTimeOffset.UtcNow < end && !process.HasExited)
{
    process.Refresh();
    Console.WriteLine($"{DateTimeOffset.UtcNow:O},{process.PrivateMemorySize64 / 1048576.0:F2},{process.WorkingSet64 / 1048576.0:F2},{process.TotalProcessorTime.TotalMilliseconds:F0},{process.Threads.Count},{process.HandleCount}");
    await Task.Delay(1000);
}
return 0;
