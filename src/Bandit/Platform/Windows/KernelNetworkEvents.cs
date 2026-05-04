using System.Runtime.InteropServices;
using static Bandit.Platform.Windows.EtwInterop;

namespace Bandit.Platform.Windows;

internal static class KernelNetworkEvents
{
    public static readonly Guid ProviderId = new("7DD42A49-5329-4832-8DFD-43D979153A88");

    public enum Direction { Sent, Received }

    public readonly record struct Decoded(int Pid, int Bytes, Direction Direction);

    // Microsoft-Windows-Kernel-Network data-send / data-recv event IDs. The
    // user-data layout for all eight templates starts with a 32-bit PID
    // followed by a 32-bit size, so we don't branch by protocol or address
    // family — just match the Id and read PID + size.
    //
    //   10 / 11 — TCP IPv4 send / recv
    //   26 / 27 — TCP IPv6 send / recv
    //   42 / 43 — UDP IPv4 send / recv
    //   58 / 59 — UDP IPv6 send / recv
    public static unsafe bool TryDecode(ref readonly EVENT_RECORD record, out Decoded decoded)
    {
        decoded = default;

        if (record.EventHeader.ProviderId != ProviderId) return false;

        var id = record.EventHeader.EventDescriptor.Id;
        Direction direction;
        switch (id)
        {
            case 10: case 26: case 42: case 58: direction = Direction.Sent;     break;
            case 11: case 27: case 43: case 59: direction = Direction.Received; break;
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
