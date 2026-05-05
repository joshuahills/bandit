using System.Text;
using Bandit.Data;
using Bandit.Data.Collectors;
using Bandit.Data.Models;
using Bandit.Platform.Windows;
using Bandit.UI.Rendering;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Bandit.UI.Screens;

public sealed class ProcessScreen(AppState state, ProcessNetworkCollector collector) : IScreen
{
    public int Index => 1;
    public string Title => "Processes";

    private View? _container;
    private ProcessTable? _table;
    private ProcessDetail? _detail;
    private int? _detailPid;

    public View Build()
    {
        _container = new View
        {
            X = 0, Y = 0,
            Width = Dim.Fill(), Height = Dim.Fill(),
            CanFocus = true,
        };

        if (!collector.IsAvailable)
        {
            BuildElevationBanner();
            return _container;
        }

        if (_detailPid is { } pid) BuildDetail(pid);
        else BuildTable();
        return _container;
    }

    public void Refresh()
    {
        if (_table is not null) RefreshTable();
        if (_detail is not null) RefreshDetail();
    }

    /// <summary>
    /// Open the detail view for a specific PID. Used by the command palette.
    /// Returns false if the PID isn't currently visible in the snapshot history.
    /// </summary>
    public bool OpenDetail(int pid)
    {
        _detailPid = pid;
        if (_container is not null) BuildDetail(pid);
        return true;
    }

    /// <summary>Resolve a process name fragment to a PID via the snapshot history.</summary>
    public int? FindPidByName(string fragment) =>
        ProcessAggregator.FindPidByName(collector.Snapshots.TailN(state.TimescaleSeconds), fragment);

    private void BuildElevationBanner()
    {
        if (_container is null) return;
        (string text, Attribute attr)[] lines =
        [
            ("", Theme.StatusAttr),
            ("  Per-process network monitoring requires administrator privileges.", Theme.WarningAttr),
            ("", Theme.StatusAttr),
            ("  Re-launch Bandit from an elevated terminal:", Theme.StatusAttr),
            ("", Theme.StatusAttr),
            ("    Run as Administrator → bandit.exe", Theme.DimAttr),
        ];
        for (int i = 0; i < lines.Length; i++)
        {
            _container.Add(new ColoredLabel(lines[i].text, lines[i].attr)
            {
                X = 0, Y = i, Width = Dim.Fill(),
            });
        }
    }

    private void BuildTable()
    {
        if (_container is null) return;
        _container.RemoveAll();
        _detail = null;

        _table = new ProcessTable(collector)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _table.Activated += OnTableActivated;
        _container.Add(_table);
        _table.SetFocus();
        RefreshTable();
    }

    private void OnTableActivated(object? sender, int pid)
    {
        _detailPid = pid;
        BuildDetail(pid);
    }

    private void BuildDetail(int pid)
    {
        if (_container is null) return;
        _container.RemoveAll();
        _table = null;

        _detail = new ProcessDetail(pid)
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _detail.Back += OnDetailBack;
        _container.Add(_detail);
        _detail.SetFocus();
        RefreshDetail();
    }

    private void OnDetailBack(object? sender, EventArgs e)
    {
        _detailPid = null;
        BuildTable();
    }

    private void RefreshTable()
    {
        if (_table is null) return;
        _table.Rows = BuildRows(state.TimescaleSeconds);
        _table.WindowLabel = state.TimescaleLabel;
        _table.SetNeedsDraw();
    }

    private void RefreshDetail()
    {
        if (_detail is null) return;
        var pid = _detail.Pid;
        var window = collector.Snapshots.TailN(state.TimescaleSeconds);

        // Find the most recent name for this PID.
        string? name = null;
        for (int i = window.Length - 1; i >= 0 && name is null; i--)
        {
            foreach (var p in window[i])
            {
                if (p.Pid == pid) { name = p.Name; break; }
            }
        }

        long liveIn = 0, liveOut = 0;
        if (window.Length > 0)
        {
            foreach (var p in window[^1])
            {
                if (p.Pid == pid) { liveIn = p.BytesIn; liveOut = p.BytesOut; break; }
            }
        }

        long totalIn = 0, totalOut = 0;
        var history = new NetworkSample[window.Length];
        for (int i = 0; i < window.Length; i++)
        {
            long bIn = 0, bOut = 0;
            foreach (var p in window[i])
            {
                if (p.Pid == pid) { bIn = p.BytesIn; bOut = p.BytesOut; break; }
            }
            totalIn += bIn;
            totalOut += bOut;
            history[i] = new NetworkSample(0, bIn, bOut, 0, 0);
        }

        var connections = ConnectionTable.SnapshotForPid(pid);

        _detail.UpdateData(
            name ?? $"pid:{pid}",
            liveIn, liveOut, totalIn, totalOut,
            history, state.TimescaleSeconds, state.TimescaleLabel,
            connections);
    }

    private ProcessNetworkRow[] BuildRows(int seconds) =>
        ProcessAggregator.Aggregate(collector.Snapshots.TailN(seconds));
}

internal sealed class ProcessTable : View
{
    private const int PidWidth       = 6;
    private const int LiveWidth      = 10;
    private const int TotalWidth     = 10;
    private const int SparklineWidth = 16;
    private const int NameMin        = 12;

    public ProcessNetworkRow[] Rows { get; set; } = [];
    public string WindowLabel { get; set; } = "";
    public int? SelectedPid { get; set; }

    public event EventHandler<int>? Activated;

    private readonly ProcessNetworkCollector _collector;
    private ProcessSortKey _sortKey = ProcessSortKey.TotalCombined;
    private bool _sortDesc = true;

    public ProcessTable(ProcessNetworkCollector collector)
    {
        _collector = collector;
        CanFocus = true;
        MouseEvent += OnTableMouse;
        KeyDown += OnTableKey;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        int height = Viewport.Height;
        if (width <= 0 || height <= 0) return true;

        int fixedBlock = SparklineWidth + LiveWidth * 2 + TotalWidth * 2 + 5;
        int nameWidth = Math.Max(NameMin, width - PidWidth - fixedBlock - 2);

        DrawHeader(nameWidth);

        if (Rows.Length == 0)
        {
            DrawDiagnostic();
            return true;
        }

        var sorted = ProcessSorter.Sort(Rows, _sortKey, _sortDesc)
            .Take(height - 2)
            .ToArray();

        // Default selection to the first row if nothing is selected, or if
        // the previously selected PID has fallen out of the visible set.
        if (SelectedPid is null || Array.FindIndex(sorted, r => r.Pid == SelectedPid) < 0)
            SelectedPid = sorted.Length > 0 ? sorted[0].Pid : null;

        for (int i = 0; i < sorted.Length; i++)
            DrawRow(2 + i, sorted[i], nameWidth, sorted[i].Pid == SelectedPid);

        return true;
    }

    private void OnTableKey(object? sender, Key key)
    {
        if (Rows.Length == 0) return;
        var sorted = ProcessSorter.Sort(Rows, _sortKey, _sortDesc).ToArray();
        if (sorted.Length == 0) return;

        int idx = SelectedPid is { } pid
            ? Math.Max(0, Array.FindIndex(sorted, r => r.Pid == pid))
            : 0;

        int newIdx = key.KeyCode switch
        {
            KeyCode.CursorUp   => Math.Max(0, idx - 1),
            KeyCode.CursorDown => Math.Min(sorted.Length - 1, idx + 1),
            KeyCode.Home       => 0,
            KeyCode.End        => sorted.Length - 1,
            KeyCode.PageUp     => Math.Max(0, idx - 10),
            KeyCode.PageDown   => Math.Min(sorted.Length - 1, idx + 10),
            _                  => -1,
        };

        if (newIdx >= 0)
        {
            SelectedPid = sorted[newIdx].Pid;
            SetNeedsDraw();
            key.Handled = true;
            return;
        }

        if (key.KeyCode == KeyCode.Enter && SelectedPid is { } selected)
        {
            Activated?.Invoke(this, selected);
            key.Handled = true;
        }
    }

    private void OnTableMouse(object? sender, Mouse e)
    {
        if (e.Position is not { } pos) return;

        // Header click → sort.
        if (e.IsSingleClicked && pos.Y == 0)
        {
            var clicked = ColumnAt(pos.X);
            if (clicked is null) return;

            if (_sortKey == clicked.Value) _sortDesc = !_sortDesc;
            else { _sortKey = clicked.Value; _sortDesc = clicked.Value is not (ProcessSortKey.Pid or ProcessSortKey.Name); }
            SetNeedsDraw();
            e.Handled = true;
            return;
        }

        // Row click → select; double-click → activate.
        if (pos.Y < 2) return;
        int rowIdx = pos.Y - 2;
        var sorted = ProcessSorter.Sort(Rows, _sortKey, _sortDesc).Take(Viewport.Height - 2).ToArray();
        if (rowIdx >= sorted.Length) return;

        SelectedPid = sorted[rowIdx].Pid;
        SetNeedsDraw();
        e.Handled = true;

        if (e.IsDoubleClicked)
            Activated?.Invoke(this, sorted[rowIdx].Pid);
    }

    private ProcessSortKey? ColumnAt(int x)
    {
        int width = Viewport.Width;
        int fixedBlock = SparklineWidth + LiveWidth * 2 + TotalWidth * 2 + 5;
        int nameWidth = Math.Max(NameMin, width - PidWidth - fixedBlock - 2);

        int start = 1;
        if (x >= start && x < start + PidWidth + 1) return ProcessSortKey.Pid;
        start += PidWidth + 1;
        if (x >= start && x < start + nameWidth) return ProcessSortKey.Name;
        start += nameWidth + 1;
        // Sparkline column — visual only, no sort key.
        if (x >= start && x < start + SparklineWidth) return null;
        start += SparklineWidth + 1;
        if (x >= start && x < start + LiveWidth) return ProcessSortKey.LiveUp;
        start += LiveWidth + 1;
        if (x >= start && x < start + LiveWidth) return ProcessSortKey.LiveDown;
        start += LiveWidth + 1;
        if (x >= start && x < start + TotalWidth) return ProcessSortKey.TotalUp;
        start += TotalWidth + 1;
        if (x >= start && x < start + TotalWidth) return ProcessSortKey.TotalDown;
        return null;
    }

    private void DrawDiagnostic()
    {
        if (_collector.Status == CollectorStatus.Failed && !string.IsNullOrEmpty(_collector.StartError))
        {
            SetAttribute(Theme.WarningAttr);
            DrawString(2, 2, $" ETW session failed: {_collector.StartError} ");
            return;
        }

        SetAttribute(Theme.DimAttr);
        DrawString(2, 2, " Waiting for traffic… ");
    }

    private void DrawHeader(int nameWidth)
    {
        int x = 1;
        DrawHeaderCell(x, " PID", PidWidth + 1, leftAlign: true,  ProcessSortKey.Pid);
        x += PidWidth + 1;

        DrawHeaderCell(x, "PROCESS", nameWidth, leftAlign: true, ProcessSortKey.Name);
        x += nameWidth + 1;

        // Sparkline column header — non-sortable, plain status colour.
        SetAttribute(Theme.StatusAttr);
        DrawString(x, 0, "TRAFFIC".PadRight(SparklineWidth));
        x += SparklineWidth + 1;

        DrawHeaderCell(x, "↑/s", LiveWidth, leftAlign: false, ProcessSortKey.LiveUp);
        x += LiveWidth + 1;

        DrawHeaderCell(x, "↓/s", LiveWidth, leftAlign: false, ProcessSortKey.LiveDown);
        x += LiveWidth + 1;

        string upLabel = string.IsNullOrEmpty(WindowLabel) ? "↑" : $"↑ {WindowLabel}";
        DrawHeaderCell(x, upLabel, TotalWidth, leftAlign: false, ProcessSortKey.TotalUp);
        x += TotalWidth + 1;

        string downLabel = string.IsNullOrEmpty(WindowLabel) ? "↓" : $"↓ {WindowLabel}";
        DrawHeaderCell(x, downLabel, TotalWidth, leftAlign: false, ProcessSortKey.TotalDown);
    }

    private void DrawHeaderCell(int x, string label, int width, bool leftAlign, ProcessSortKey key)
    {
        bool active = _sortKey == key;
        string content = active
            ? (leftAlign ? $"{label} {(_sortDesc ? '▾' : '▴')}" : $"{(_sortDesc ? '▾' : '▴')} {label}")
            : label;
        string padded = leftAlign ? content.PadRight(width) : content.PadLeft(width);
        if (padded.Length > width) padded = padded[..width];

        SetAttribute(active ? Theme.ActiveTabAttr : Theme.StatusAttr);
        DrawString(x, 0, padded);
    }

    private void DrawRow(int y, ProcessNetworkRow row, int nameWidth, bool selected)
    {
        string pid = row.Pid.ToString().PadLeft(PidWidth);
        string name = Truncate(row.Name, nameWidth);
        string liveUp = $"{BandwidthChart.FormatBytesPerSec(row.LiveBytesOut)}/s".PadLeft(LiveWidth);
        string liveDown = $"{BandwidthChart.FormatBytesPerSec(row.LiveBytesIn)}/s".PadLeft(LiveWidth);
        string totalUp = BandwidthChart.FormatBytesPerSec(row.TotalBytesOut).PadLeft(TotalWidth);
        string totalDown = BandwidthChart.FormatBytesPerSec(row.TotalBytesIn).PadLeft(TotalWidth);

        var pidAttr  = selected ? Theme.SelectedPidAttr      : Theme.StatusAttr;
        var nameAttr = selected ? Theme.SelectedNameAttr     : Theme.AccentAttr;
        var upAttr   = selected ? Theme.SelectedUploadAttr   : Theme.UploadAttr;
        var downAttr = selected ? Theme.SelectedDownloadAttr : Theme.DownloadAttr;

        // Paint the row background to the right edge so the highlight looks
        // contiguous rather than just behind the printed text.
        if (selected)
        {
            SetAttribute(Theme.SelectedPidAttr);
            DrawString(0, y, new string(' ', Viewport.Width));
        }

        int x = 1;
        SetAttribute(pidAttr);
        DrawString(x, y, pid);
        x += PidWidth + 1;

        SetAttribute(nameAttr);
        DrawString(x, y, name);
        x += nameWidth + 1;

        DrawSparkline(x, y, row, selected);
        x += SparklineWidth + 1;

        SetAttribute(upAttr);
        DrawString(x, y, liveUp);
        x += LiveWidth + 1;

        SetAttribute(downAttr);
        DrawString(x, y, liveDown);
        x += LiveWidth + 1;

        SetAttribute(upAttr);
        DrawString(x, y, totalUp);
        x += TotalWidth + 1;

        SetAttribute(downAttr);
        DrawString(x, y, totalDown);
    }

    private void DrawSparkline(int x, int y, ProcessNetworkRow row, bool selected)
    {
        var historyIn = row.HistoryIn;
        var historyOut = row.HistoryOut;
        if (historyIn.Length == 0) return;

        var canvas = new BrailleCanvas(SparklineWidth, 1);
        int dotW = canvas.DotWidth;
        int dotH = canvas.DotHeight;

        // Scale to the per-row peak so each row's sparkline shows its own
        // shape — we don't want one bursty process to flatten everyone else
        // visually.
        long max = 1;
        for (int i = 0; i < historyIn.Length; i++)
        {
            long total = historyIn[i] + historyOut[i];
            if (total > max) max = total;
        }

        for (int dotX = 0; dotX < dotW; dotX++)
        {
            double frac = dotW <= 1 ? 0 : (double)dotX / (dotW - 1);
            double sampleF = frac * (historyIn.Length - 1);
            int idx0 = (int)Math.Floor(sampleF);
            int idx1 = Math.Min(idx0 + 1, historyIn.Length - 1);
            double t = sampleF - idx0;
            double total = (historyIn[idx0] + historyOut[idx0]) * (1 - t)
                         + (historyIn[idx1] + historyOut[idx1]) * t;

            double valueFrac = total / max;
            if (valueFrac < 0) valueFrac = 0;
            if (valueFrac > 1) valueFrac = 1;
            int dotFromBottom = (int)Math.Round(valueFrac * dotH);
            int dotYTop = dotH - dotFromBottom;
            if (dotYTop < 0) dotYTop = 0;
            if (dotYTop >= dotH) continue;

            canvas.FillBelow(dotX, dotYTop);
        }

        SetAttribute(selected ? Theme.SelectedUploadAttr : Theme.UploadAttr);
        for (int cx = 0; cx < SparklineWidth; cx++)
        {
            char? cell = canvas.GetCell(cx, 0);
            if (cell is not null)
            {
                Move(x + cx, y);
                AddRune(new System.Text.Rune(cell.Value));
            }
        }
    }

    private static string Truncate(string value, int width)
    {
        if (value.Length <= width) return value.PadRight(width);
        return value[..(width - 1)] + "…";
    }

    private void DrawString(int x, int y, string text)
    {
        Move(x, y);
        foreach (var rune in text.EnumerateRunes())
            AddRune(rune);
    }
}

internal sealed class ProcessDetail : View
{
    private const int ConnectionsBlock = 12;  // header + 1 spacer + ~10 rows
    private const int ConnectionVisibleRows = ConnectionsBlock - 2;

    public int Pid { get; }
    public event EventHandler? Back;

    private string _name = "";
    private long _liveIn, _liveOut, _totalIn, _totalOut;
    private string _windowLabel = "";
    private IReadOnlyList<ProcessConnection> _connections = Array.Empty<ProcessConnection>();
    private int _scrollOffset;
    private readonly BandwidthChart _chart;

    public ProcessDetail(int pid)
    {
        Pid = pid;
        CanFocus = true;
        _chart = new BandwidthChart
        {
            X = 0, Y = 6,
            Width = Dim.Fill(),
            // Leave room for the connections block at the bottom.
            Height = Dim.Fill(ConnectionsBlock),
        };
        Add(_chart);
        KeyDown += OnDetailKey;
    }

    public void UpdateData(
        string name,
        long liveIn, long liveOut,
        long totalIn, long totalOut,
        NetworkSample[] history,
        int timescaleSeconds,
        string windowLabel,
        IReadOnlyList<ProcessConnection> connections)
    {
        _name = name;
        _liveIn = liveIn;
        _liveOut = liveOut;
        _totalIn = totalIn;
        _totalOut = totalOut;
        _windowLabel = windowLabel;
        _connections = connections;

        // Re-clamp scroll position in case the new snapshot has fewer rows.
        int max = Math.Max(0, _connections.Count - ConnectionVisibleRows);
        if (_scrollOffset > max) _scrollOffset = max;

        _chart.Samples = history;
        _chart.TimescaleSeconds = timescaleSeconds;
        _chart.SetNeedsDraw();
        SetNeedsDraw();
    }

    private void OnDetailKey(object? sender, Key key)
    {
        if (key.KeyCode == KeyCode.Esc || key.KeyCode == KeyCode.Backspace)
        {
            Back?.Invoke(this, EventArgs.Empty);
            key.Handled = true;
            return;
        }

        int max = Math.Max(0, _connections.Count - ConnectionVisibleRows);
        int? newOffset = key.KeyCode switch
        {
            KeyCode.CursorUp   => Math.Max(0,   _scrollOffset - 1),
            KeyCode.CursorDown => Math.Min(max, _scrollOffset + 1),
            KeyCode.PageUp     => Math.Max(0,   _scrollOffset - ConnectionVisibleRows),
            KeyCode.PageDown   => Math.Min(max, _scrollOffset + ConnectionVisibleRows),
            KeyCode.Home       => 0,
            KeyCode.End        => max,
            _                  => null,
        };

        if (newOffset is { } offset && offset != _scrollOffset)
        {
            _scrollOffset = offset;
            SetNeedsDraw();
            key.Handled = true;
        }
        else if (newOffset is not null)
        {
            // Already at limit — still consume the key so it doesn't bubble.
            key.Handled = true;
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        SetAttribute(Theme.AccentAttr);
        DrawString(2, 0, $" ◈ {_name}  ");
        SetAttribute(Theme.StatusAttr);
        DrawString(2 + 5 + _name.Length + 2, 0, $"PID {Pid}");
        SetAttribute(Theme.DimAttr);
        DrawString(2, 1, " [Esc] back ");

        SetAttribute(Theme.UploadAttr);
        DrawString(2, 3, $" ↑ live: {BandwidthChart.FormatBytesPerSec(_liveOut)}/s   ↑ over {_windowLabel}: {BandwidthChart.FormatBytesPerSec(_totalOut)}");
        SetAttribute(Theme.DownloadAttr);
        DrawString(2, 4, $" ↓ live: {BandwidthChart.FormatBytesPerSec(_liveIn)}/s   ↓ over {_windowLabel}: {BandwidthChart.FormatBytesPerSec(_totalIn)}");

        DrawConnections();

        return base.OnDrawingContent(context);
    }

    private void DrawConnections()
    {
        int viewportH = Viewport.Height;
        int viewportW = Viewport.Width;

        // Header takes rows 0–4, the bandwidth chart starts at Y=6 and reserves
        // ConnectionsBlock rows at the bottom for us. If the terminal is too
        // short, the connections block would crash up into the header/chart
        // area. Hide it entirely below this threshold rather than overdraw.
        const int HeaderRows = 6;       // 5 header lines + 1 gap before chart
        const int MinChartRows = 1;
        if (viewportH < HeaderRows + MinChartRows + ConnectionsBlock) return;

        int startY = viewportH - ConnectionsBlock + 1;

        // Header row.
        SetAttribute(Theme.AccentAttr);
        DrawString(2, startY, $" CONNECTIONS ({_connections.Count}) ");

        if (_connections.Count == 0)
        {
            SetAttribute(Theme.DimAttr);
            DrawString(2, startY + 2, " (none) ");
            return;
        }

        // Order: TCP first (sorted by state then port), then UDP.
        var ordered = _connections
            .OrderBy(c => c.Protocol)
            .ThenBy(c => c.State == TcpState.Established ? 0 : 1)
            .ThenBy(c => c.LocalPort)
            .ToArray();

        // Re-clamp here too — Sort might surface this without a refresh having
        // run, and we want to never index past the end.
        int maxOffset = Math.Max(0, ordered.Length - ConnectionVisibleRows);
        if (_scrollOffset > maxOffset) _scrollOffset = maxOffset;

        int shown = Math.Min(ConnectionVisibleRows, ordered.Length - _scrollOffset);

        for (int i = 0; i < shown; i++)
        {
            var c = ordered[_scrollOffset + i];
            int y = startY + 1 + i;

            string proto = $"{(c.Protocol == Protocol.Tcp ? "TCP" : "UDP")}{(c.Family == Bandit.Data.Models.AddressFamily.IPv6 ? "6" : "4")}";
            string local = FormatEndpoint(c.Local, c.LocalPort);
            string state = c.State == TcpState.None ? "" : FormatState(c.State);

            SetAttribute(Theme.StatusAttr);
            DrawString(2, y, $"  {proto,-5}");

            SetAttribute(Theme.AccentAttr);
            DrawString(9, y, Truncate(local, 30));

            // UDP rows have no remote endpoint — the kernel only tracks the
            // bound local address. Skip the arrow + remote column for them
            // so the row reads as just "UDP4  0.0.0.0:5353".
            if (c.Remote is not null)
            {
                SetAttribute(Theme.DimAttr);
                DrawString(40, y, " → ");

                SetAttribute(Theme.AccentAttr);
                DrawString(43, y, Truncate(FormatEndpoint(c.Remote, c.RemotePort), 30));
            }

            SetAttribute(c.State == TcpState.Established ? Theme.UploadAttr : Theme.DimAttr);
            DrawString(74, y, state);

            _ = viewportW; // Dynamic-width layout lands in PR #22.
        }

        // Scroll indicators replace the old 'and N more' line.
        SetAttribute(Theme.DimAttr);
        int hidden_above = _scrollOffset;
        int hidden_below = ordered.Length - (_scrollOffset + shown);

        if (hidden_above > 0 || hidden_below > 0)
        {
            string headerSuffix;
            if (hidden_above > 0 && hidden_below > 0)
                headerSuffix = $"   ↑ {hidden_above} above · ↓ {hidden_below} below ";
            else if (hidden_above > 0)
                headerSuffix = $"   ↑ {hidden_above} above ";
            else
                headerSuffix = $"   ↓ {hidden_below} below ";

            int suffixX = 2 + $" CONNECTIONS ({_connections.Count}) ".Length;
            DrawString(suffixX, startY, headerSuffix);
        }
    }

    private static string FormatEndpoint(System.Net.IPAddress addr, int port)
    {
        bool isV6 = addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
        return isV6 ? $"[{addr}]:{port}" : $"{addr}:{port}";
    }

    private static string FormatState(TcpState state) => state switch
    {
        TcpState.Established => "ESTABLISHED",
        TcpState.Listening   => "LISTENING",
        TcpState.SynSent     => "SYN_SENT",
        TcpState.SynReceived => "SYN_RCVD",
        TcpState.FinWait1    => "FIN_WAIT1",
        TcpState.FinWait2    => "FIN_WAIT2",
        TcpState.CloseWait   => "CLOSE_WAIT",
        TcpState.Closing     => "CLOSING",
        TcpState.LastAck     => "LAST_ACK",
        TcpState.TimeWait    => "TIME_WAIT",
        TcpState.Closed      => "CLOSED",
        TcpState.DeleteTcb   => "DELETE_TCB",
        _                    => "",
    };

    private static string Truncate(string value, int width)
    {
        if (value.Length <= width) return value;
        return value[..(width - 1)] + "…";
    }

    private void DrawString(int x, int y, string text)
    {
        Move(x, y);
        foreach (var rune in text.EnumerateRunes())
            AddRune(rune);
    }
}
