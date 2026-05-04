namespace Bandit.Data.Models;

public sealed record ProcessNetworkInfo(
    int Pid,
    string Name,
    long BytesIn,
    long BytesOut)
{
    public long TotalBytes => BytesIn + BytesOut;
}