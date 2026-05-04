namespace Bandit.Platform.Windows;

using static Bandit.Platform.Windows.EtwInterop;

internal static class KernelNetworkEvents
{
    public static readonly Guid ProviderId = new("7DD42A49-5329-4832-8DFD-43D979153A88");

    public enum Direction { Sent, Received }

    public readonly record struct Decoded(int Pid, int Bytes, Direction Direction);

    // Microsoft-Windows-Kernel-Network data-send / data-recv event IDs. We
    // verified these against the canonical manifest with
    //   wevtutil gp Microsoft-Windows-Kernel-Network /ge:true
    // — the provider's tasks are 10 (TCP) and 11 (UDP), NOT 1 / 2, so a
    // task-based match against 1 / 2 is silently always false. The Id field
    // is the right discriminator and is unique per event template:
    //   10 / 11 — TCP IPv4 send / recv
    //   26 / 27 — TCP IPv6 send / recv
    //   42 / 43 — UDP IPv4 send / recv
    //   58 / 59 — UDP IPv6 send / recv
    // The user-data layout (PID at offset 0, size at offset 4) is identical
    // for all eight templates, so we don't branch by protocol/family beyond
    // direction.
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
