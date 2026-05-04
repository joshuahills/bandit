namespace Bandit;

public sealed class AppState
{
    public int ActiveScreenIndex { get; set; }
    public int TimescaleIndex { get; set; } = 1;
    public bool IsElevated { get; init; }

    public static readonly int[] Timescales = [30, 60, 300, 900, 3600, 21600, 86400];
    public static readonly string[] TimescaleLabels = ["30s", "1m", "5m", "15m", "1h", "6h", "24h"];

    public int TimescaleSeconds => Timescales[TimescaleIndex];
    public string TimescaleLabel => TimescaleLabels[TimescaleIndex];

    public void CycleTimescaleDown() =>
        TimescaleIndex = (TimescaleIndex - 1 + Timescales.Length) % Timescales.Length;

    public void CycleTimescaleUp() =>
        TimescaleIndex = (TimescaleIndex + 1) % Timescales.Length;
}