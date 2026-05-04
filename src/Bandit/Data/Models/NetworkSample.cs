namespace Bandit.Data.Models;

public readonly record struct NetworkSample(
    long Timestamp,
    long BytesIn,
    long BytesOut,
    long PacketsIn,
    long PacketsOut)
{
    public long TotalBytes => BytesIn + BytesOut;
    public double TotalMbps => TotalBytes * 8.0 / 1_000_000.0;
    public double UploadMbps => BytesOut * 8.0 / 1_000_000.0;
    public double DownloadMbps => BytesIn * 8.0 / 1_000_000.0;
}