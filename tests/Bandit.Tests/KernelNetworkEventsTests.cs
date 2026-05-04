using Bandit.Platform.Windows;
using Xunit;
using static Bandit.Platform.Windows.EtwInterop;

namespace Bandit.Tests;

public class KernelNetworkEventsTests
{
    private static readonly Guid Other = new("11111111-2222-3333-4444-555555555555");

    [Theory]
    [InlineData(10, true)]   // TCP IPv4 send
    [InlineData(11, false)]  // TCP IPv4 recv
    [InlineData(26, true)]   // TCP IPv6 send
    [InlineData(27, false)]  // TCP IPv6 recv
    [InlineData(42, true)]   // UDP IPv4 send
    [InlineData(43, false)]  // UDP IPv4 recv
    [InlineData(58, true)]   // UDP IPv6 send
    [InlineData(59, false)]  // UDP IPv6 recv
    public unsafe void TryDecode_accepts_all_eight_data_event_ids(int eventId, bool expectSent)
    {
        Span<byte> payload = stackalloc byte[8];
        WritePidAndSize(payload, pid: 1234, size: 512);

        fixed (byte* p = payload)
        {
            var record = MakeRecord(KernelNetworkEvents.ProviderId, (ushort)eventId, p, 8);
            bool ok = KernelNetworkEvents.TryDecode(in record, out var decoded);

            Assert.True(ok);
            Assert.Equal(1234, decoded.Pid);
            Assert.Equal(512, decoded.Bytes);
            var expected = expectSent ? KernelNetworkEvents.Direction.Sent : KernelNetworkEvents.Direction.Received;
            Assert.Equal(expected, decoded.Direction);
        }
    }

    [Theory]
    [InlineData(12)]   // TCP connect
    [InlineData(13)]   // TCP disconnect
    [InlineData(15)]   // TCP fail
    [InlineData(18)]   // TCPCOPY (high-volume noise event)
    [InlineData(49)]   // UDP fail
    [InlineData(0)]
    [InlineData(999)]
    public unsafe void TryDecode_rejects_non_data_event_ids(int eventId)
    {
        Span<byte> payload = stackalloc byte[8];
        WritePidAndSize(payload, 1234, 512);

        fixed (byte* p = payload)
        {
            var record = MakeRecord(KernelNetworkEvents.ProviderId, (ushort)eventId, p, 8);
            Assert.False(KernelNetworkEvents.TryDecode(in record, out _));
        }
    }

    [Fact]
    public unsafe void TryDecode_rejects_other_providers_even_with_matching_id()
    {
        Span<byte> payload = stackalloc byte[8];
        WritePidAndSize(payload, 1234, 512);

        fixed (byte* p = payload)
        {
            var record = MakeRecord(Other, 10, p, 8);
            Assert.False(KernelNetworkEvents.TryDecode(in record, out _));
        }
    }

    [Fact]
    public unsafe void TryDecode_rejects_short_user_data()
    {
        Span<byte> payload = stackalloc byte[7]; // < 8
        fixed (byte* p = payload)
        {
            var record = MakeRecord(KernelNetworkEvents.ProviderId, 10, p, 7);
            Assert.False(KernelNetworkEvents.TryDecode(in record, out _));
        }
    }

    [Fact]
    public unsafe void TryDecode_rejects_zero_user_data_pointer()
    {
        var record = MakeRecord(KernelNetworkEvents.ProviderId, 10, null, 8);
        Assert.False(KernelNetworkEvents.TryDecode(in record, out _));
    }

    [Theory]
    [InlineData(0,    100)]   // pid <= 0
    [InlineData(-1,   100)]
    [InlineData(1234, 0)]     // size <= 0
    [InlineData(1234, -1)]
    public unsafe void TryDecode_rejects_non_positive_pid_or_size(int pid, int size)
    {
        Span<byte> payload = stackalloc byte[8];
        WritePidAndSize(payload, pid, size);

        fixed (byte* p = payload)
        {
            var record = MakeRecord(KernelNetworkEvents.ProviderId, 10, p, 8);
            Assert.False(KernelNetworkEvents.TryDecode(in record, out _));
        }
    }

    private static void WritePidAndSize(Span<byte> destination, int pid, int size)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(destination, pid);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(destination[4..], size);
    }

    private static unsafe EVENT_RECORD MakeRecord(Guid providerId, ushort id, byte* userData, ushort userDataLength)
    {
        return new EVENT_RECORD
        {
            EventHeader = new EVENT_HEADER
            {
                ProviderId = providerId,
                EventDescriptor = new EVENT_DESCRIPTOR { Id = id },
            },
            UserData = userData == null ? IntPtr.Zero : (IntPtr)userData,
            UserDataLength = userDataLength,
        };
    }
}
