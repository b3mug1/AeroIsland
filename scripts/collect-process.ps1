param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [ValidateRange(1, 86400)][int]$Seconds = 300,
    [string]$Output = 'process-samples.csv'
)
$targetProcess = Get-Process -Id $ProcessId -ErrorAction Stop
$samples = [System.Collections.Generic.List[object]]::new()
$logicalCpus = [Environment]::ProcessorCount
$previousCpu = $targetProcess.TotalProcessorTime.TotalSeconds
$previousTime = [DateTime]::UtcNow
for ($sampleIndex = 0; $sampleIndex -lt $Seconds; $sampleIndex++) {
    Start-Sleep -Seconds 1
    $targetProcess.Refresh()
    if ($targetProcess.HasExited) { break }
    $now = [DateTime]::UtcNow
    $cpu = $targetProcess.TotalProcessorTime.TotalSeconds
    $elapsed = ($now - $previousTime).TotalSeconds
    $normalizedCpu = if ($elapsed -gt 0) { 100 * ($cpu - $previousCpu) / ($elapsed * $logicalCpus) } else { 0 }
    $samples.Add([pscustomobject]@{
        Utc = $now.ToString('o')
        PrivateMiB = [math]::Round($targetProcess.PrivateMemorySize64 / 1MB, 2)
        WorkingMiB = [math]::Round($targetProcess.WorkingSet64 / 1MB, 2)
        CpuPercent = [math]::Round($normalizedCpu, 3)
        Threads = $targetProcess.Threads.Count
        Handles = $targetProcess.HandleCount
    })
    $previousCpu = $cpu
    $previousTime = $now
}
$samples | Export-Csv -LiteralPath $Output -NoTypeInformation -Encoding utf8
