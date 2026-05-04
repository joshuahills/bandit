using System.Net.NetworkInformation;
using Bandit.Data.Models;

namespace Bandit.Data.Collectors;

public sealed class SystemNetworkCollector : INetworkCollector
{
    public CircularBuffer<NetworkSample> Samples { get; } = new(86_400);
    public bool IsAvailable => true;

    private readonly Dictionary<string, (long bIn, long bOut, long pIn, long pOut)> _prev = new();

    public async Task StartAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            long deltaIn = 0, deltaOut = 0, deltaPktsIn = 0, deltaPktsOut = 0;

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var stats = nic.GetIPStatistics();
                    long bIn = stats.BytesReceived;
                    long bOut = stats.BytesSent;
                    long pIn = stats.UnicastPacketsReceived;
                    long pOut = stats.UnicastPacketsSent;

                    if (_prev.TryGetValue(nic.Id, out var prev))
                    {
                        deltaIn += Math.Max(0, bIn - prev.bIn);
                        deltaOut += Math.Max(0, bOut - prev.bOut);
                        deltaPktsIn += Math.Max(0, pIn - prev.pIn);
                        deltaPktsOut += Math.Max(0, pOut - prev.pOut);
                    }
                    _prev[nic.Id] = (bIn, bOut, pIn, pOut);
                }
            }
            catch { /* interface enumeration can fail transiently */ }

            if (_prev.Count > 0)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Samples.Add(new NetworkSample(now, deltaIn, deltaOut, deltaPktsIn, deltaPktsOut));
            }

            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
    }
}
