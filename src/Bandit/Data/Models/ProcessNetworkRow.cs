namespace Bandit.Data.Models;

public sealed record ProcessNetworkRow(
    int Pid,
    string Name,
    long LiveBytesIn,
    long LiveBytesOut,
    long TotalBytesIn,
    long TotalBytesOut);