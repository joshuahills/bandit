namespace Bandit.Data.Collectors;

public interface INetworkCollector
{
    bool IsAvailable { get; }
    Task StartAsync(CancellationToken ct);
}
