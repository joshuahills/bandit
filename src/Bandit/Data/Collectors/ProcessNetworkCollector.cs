using Bandit.Data.Models;

namespace Bandit.Data.Collectors;

// ETW-based per-process collector — requires admin elevation.
// Full ETW implementation is a future milestone; this stub surfaces the
// elevation requirement cleanly to the UI.
public sealed class ProcessNetworkCollector : INetworkCollector
{
    public CircularBuffer<ProcessNetworkInfo[]> Snapshots { get; } = new(3_600);
    public bool IsAvailable { get; }

    public ProcessNetworkCollector(bool isElevated) => IsAvailable = isElevated;

    public Task StartAsync(CancellationToken ct)
    {
        if (!IsAvailable) return Task.CompletedTask;

        // TODO: start ETW session (Microsoft-Windows-Kernel-Network provider)
        // and aggregate per-PID byte counts into Snapshots.
        return Task.CompletedTask;
    }
}
