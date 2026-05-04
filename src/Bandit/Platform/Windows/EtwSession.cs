namespace Bandit.Platform.Windows;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Bandit.Platform.Windows.EtwInterop;

internal sealed unsafe class EtwSession : IDisposable
{
    private const string SessionName = "Bandit-Network";

    public delegate void EventHandler(ref readonly EVENT_RECORD record);

    private readonly Guid _providerId;
    private readonly EventHandler _handler;

    private GCHandle _selfHandle;
    private IntPtr _propertiesBuffer = IntPtr.Zero;
    private ulong _sessionHandle;
    private ulong _traceHandle = INVALID_PROCESS_TRACE_HANDLE;
    private Thread? _processThread;
    private volatile bool _stopRequested;

    public EtwSession(Guid providerId, EventHandler handler)
    {
        _providerId = providerId;
        _handler = handler;
    }

    public void Start()
    {
        TryStopExistingSession();

        _propertiesBuffer = AllocPropertiesBuffer();

        var startResult = StartTrace(out _sessionHandle, SessionName, _propertiesBuffer);
        if (startResult != ERROR_SUCCESS)
            throw new InvalidOperationException($"StartTrace failed: 0x{startResult:X}");

        var enableResult = EnableTraceEx2(
            _sessionHandle,
            _providerId,
            controlCode: 1, // EVENT_CONTROL_CODE_ENABLE_PROVIDER
            level: 0xFF,
            matchAnyKeyword: 0xFFFFFFFFFFFFFFFFul,
            matchAllKeyword: 0,
            timeout: 0,
            enableParameters: IntPtr.Zero);

        if (enableResult != ERROR_SUCCESS)
            throw new InvalidOperationException($"EnableTraceEx2 failed: 0x{enableResult:X}");

        _selfHandle = GCHandle.Alloc(this);
        var loggerNamePtr = Marshal.StringToHGlobalUni(SessionName);

        var logfile = new EVENT_TRACE_LOGFILE
        {
            LoggerName = loggerNamePtr,
            ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD,
            EventRecordCallback = (IntPtr)(delegate* unmanaged[Stdcall]<EVENT_RECORD*, void>)&StaticEventCallback,
            BufferCallback = (IntPtr)(delegate* unmanaged[Stdcall]<EVENT_TRACE_LOGFILE*, uint>)&StaticBufferCallback,
            Context = GCHandle.ToIntPtr(_selfHandle),
        };

        _traceHandle = OpenTrace(ref logfile);
        Marshal.FreeHGlobal(loggerNamePtr);

        if (_traceHandle == INVALID_PROCESS_TRACE_HANDLE)
            throw new InvalidOperationException("OpenTrace failed");

        _processThread = new Thread(ProcessTraceLoop)
        {
            Name = "Bandit-ETW",
            IsBackground = true,
        };
        _processThread.Start();
    }

    private void ProcessTraceLoop() =>
        ProcessTrace(in _traceHandle, 1, IntPtr.Zero, IntPtr.Zero);

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void StaticEventCallback(EVENT_RECORD* record)
    {
        // EVENT_RECORD.UserContext holds the EVENT_TRACE_LOGFILE.Context we
        // passed to OpenTrace — i.e. our pinned GCHandle.
        var ptr = record->UserContext;
        if (ptr == IntPtr.Zero) return;
        if (GCHandle.FromIntPtr(ptr).Target is not EtwSession session) return;
        session._handler(in *record);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint StaticBufferCallback(EVENT_TRACE_LOGFILE* logfile)
    {
        var ptr = logfile->Context;
        if (ptr == IntPtr.Zero) return 1;
        if (GCHandle.FromIntPtr(ptr).Target is not EtwSession session) return 1;
        return session._stopRequested ? 0u : 1u;
    }

    private void TryStopExistingSession()
    {
        var buf = AllocPropertiesBuffer();
        try { ControlTrace(0, SessionName, buf, EVENT_TRACE_CONTROL_STOP); }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static IntPtr AllocPropertiesBuffer()
    {
        int structSize = sizeof(EVENT_TRACE_PROPERTIES);
        int nameBytes = (SessionName.Length + 1) * sizeof(char);
        int total = structSize + nameBytes;

        var buffer = Marshal.AllocHGlobal(total);
        new Span<byte>((void*)buffer, total).Clear();

        var props = new EVENT_TRACE_PROPERTIES
        {
            Wnode = new WNODE_HEADER
            {
                BufferSize = (uint)total,
                Guid = Guid.NewGuid(),
                ClientContext = 1, // QPC clock
                Flags = WNODE_FLAG_TRACED_GUID,
            },
            BufferSize = 64,
            MinimumBuffers = 4,
            MaximumBuffers = 64,
            LogFileMode = EVENT_TRACE_REAL_TIME_MODE,
            FlushTimer = 1,
            LogFileNameOffset = 0,
            LoggerNameOffset = (uint)structSize,
        };

        *(EVENT_TRACE_PROPERTIES*)buffer = props;
        return buffer;
    }

    public void Dispose()
    {
        _stopRequested = true;

        if (_propertiesBuffer != IntPtr.Zero)
        {
            ControlTrace(_sessionHandle, null, _propertiesBuffer, EVENT_TRACE_CONTROL_STOP);
        }

        if (_traceHandle != INVALID_PROCESS_TRACE_HANDLE)
        {
            CloseTrace(_traceHandle);
            _traceHandle = INVALID_PROCESS_TRACE_HANDLE;
        }

        _processThread?.Join(TimeSpan.FromSeconds(2));
        _processThread = null;

        if (_selfHandle.IsAllocated) _selfHandle.Free();

        if (_propertiesBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_propertiesBuffer);
            _propertiesBuffer = IntPtr.Zero;
        }
    }
}
