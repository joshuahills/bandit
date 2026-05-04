using Bandit.Data.Models;
using Bandit.Platform.Windows;
using static Bandit.Platform.Windows.EtwInterop;

namespace Bandit.Data.Collectors;

public sealed class ProcessNetworkCollector : INetworkCollector, IDisposable
{
    public CircularBuffer<ProcessNetworkInfo[]> Snapshots { get; } = new(3_600);
    public bool IsAvailable { get; }

    public CollectorStatus Status { get; private set; } = CollectorStatus.NotStarted;
    public string? StartError { get; private set; }
    public long RawEventsSeen => Interlocked.Read(ref _rawEvents);
    public long DecodedEventsSeen => Interlocked.Read(ref _decodedEvents);

    public IReadOnlyDictionary<long, long> EventShapeHistogram
    {
        get { lock (_lock) return new Dictionary<long, long>(_shapes); }
    }

    private long _rawEvents;
    private long _decodedEvents;
    private readonly Dictionary<int, long> _runningIn = [];
    private readonly Dictionary<int, long> _runningOut = [];
    private readonly Dictionary<long, long> _shapes = [];
    private readonly Lock _lock = new();
    private readonly ProcessNameCache _names = new();

    private EtwSession? _session;
    private Dictionary<int, (long In, long Out)> _previous = [];

    public ProcessNetworkCollector(bool isElevated) => IsAvailable = isElevated;

    public Task StartAsync(CancellationToken ct)
    {
        if (!IsAvailable)
        {
            Status = CollectorStatus.NotElevated;
            return Task.CompletedTask;
        }

        _session = new EtwSession(KernelNetworkEvents.ProviderId, OnEvent);
        try
        {
            _session.Start();
            Status = CollectorStatus.Running;
        }
        catch (Exception ex)
        {
            Status = CollectorStatus.Failed;
            StartError = ex.Message;
            _session.Dispose();
            _session = null;
            return Task.CompletedTask;
        }

        return Task.Run(() => SnapshotLoop(ct), ct);
    }

    private void OnEvent(ref readonly EVENT_RECORD record)
    {
        Interlocked.Increment(ref _rawEvents);

        // Pack Id/Task/Opcode into one long for the histogram so we can see
        // which event templates we're actually receiving.
        long shape = ((long)record.EventHeader.EventDescriptor.Id << 24)
                   | ((long)record.EventHeader.EventDescriptor.Task << 8)
                   | (long)record.EventHeader.EventDescriptor.Opcode;
        lock (_lock)
        {
            _shapes.TryGetValue(shape, out var count);
            _shapes[shape] = count + 1;
        }

        if (!KernelNetworkEvents.TryDecode(in record, out var decoded)) return;
        Interlocked.Increment(ref _decodedEvents);

        lock (_lock)
        {
            var bag = decoded.Direction == KernelNetworkEvents.Direction.Received
                ? _runningIn
                : _runningOut;
            bag.TryGetValue(decoded.Pid, out var prev);
            bag[decoded.Pid] = prev + decoded.Bytes;
        }
    }

    private async Task SnapshotLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);
            Snapshots.Add(BuildSnapshot());
        }
    }

    private ProcessNetworkInfo[] BuildSnapshot()
    {
        Dictionary<int, long> snapIn, snapOut;
        lock (_lock)
        {
            snapIn = new Dictionary<int, long>(_runningIn);
            snapOut = new Dictionary<int, long>(_runningOut);
        }

        var pids = new HashSet<int>(snapIn.Keys);
        pids.UnionWith(snapOut.Keys);

        var current = new Dictionary<int, (long In, long Out)>(pids.Count);
        var deltas = new List<ProcessNetworkInfo>(pids.Count);

        foreach (var pid in pids)
        {
            long curIn = snapIn.GetValueOrDefault(pid);
            long curOut = snapOut.GetValueOrDefault(pid);
            current[pid] = (curIn, curOut);

            _previous.TryGetValue(pid, out var prev);
            long dIn = Math.Max(0, curIn - prev.In);
            long dOut = Math.Max(0, curOut - prev.Out);

            if (dIn == 0 && dOut == 0) continue;

            deltas.Add(new ProcessNetworkInfo(pid, _names.Resolve(pid), dIn, dOut));
        }

        _previous = current;
        return deltas.ToArray();
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
