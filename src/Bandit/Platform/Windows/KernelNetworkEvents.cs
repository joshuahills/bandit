using System.Runtime.InteropServices;
using static Bandit.Platform.Windows.EtwInterop;

namespace Bandit.Platform.Windows;

internal static class KernelNetworkEvents
{
    public static readonly Guid ProviderId = new("7DD42A49-5329-4832-8DFD-43D979153A88");

    public enum Direction { Sent, Received }

    public readonly record struct Decoded(int Pid, int Bytes, Direction Direction);

    // Microsoft-Windows-Kernel-Network event templates we care about. Tasks
    // 1 (TCP) and 2 (UDP) cover both IPv4 (opcodes 10/11) and IPv6 (opcodes
    // 26/27); the user-data layout starts with a 32-bit PID followed by a
    // 32-bit size in all four templates, so we don't need to branch by task
    // or address family for the bytes-by-process aggregation.
    public static unsafe bool TryDecode(ref readonly EVENT_RECORD record, out Decoded decoded)
    {
        decoded = default;

        if (record.EventHeader.ProviderId != ProviderId) return false;

        var task = record.EventHeader.EventDescriptor.Task;
        if (task != 1 && task != 2) return false;

        var opcode = record.EventHeader.EventDescriptor.Opcode;
        Direction direction;
        switch (opcode)
        {
            case 10: case 26: direction = Direction.Sent;     break;
            case 11: case 27: direction = Direction.Received; break;
            default: return false;
        }

        if (record.UserData == IntPtr.Zero || record.UserDataLength < 8) return false;

        var ptr = (byte*)record.UserData;
        int pid = *(int*)(ptr);
        int size = *(int*)(ptr + 4);

        if (pid <= 0 || size <= 0) return false;

        decoded = new Decoded(pid, size, direction);
        return true;
    }
}
