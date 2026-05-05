using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Bandit.Platform.Net;

/// <summary>
/// Minimal reader for the MaxMind DB binary format (spec:
/// https://maxmind.github.io/MaxMind-DB/). Hand-rolled because the official
/// MaxMind.Db package leans on reflection-based deserialisation, which trips
/// IL2026/IL3050 under our Native-AOT publish.
///
/// Supports the subset Bandit actually needs: walking the binary search tree
/// for an IP, decoding map/string/pointer values, and ignoring everything
/// else. Lookups are allocation-light on the hot path (one record map per hit).
/// </summary>
internal sealed class MmdbReader
{
    private readonly byte[] _buf;
    private readonly int _nodeCount;
    private readonly int _recordSize;       // 24, 28, or 32 bits
    private readonly int _nodeByteSize;     // bytes per node = recordSize * 2 / 8
    private readonly int _treeSizeBytes;
    private readonly int _dataSectionStart; // = treeSize + 16-byte separator
    private readonly int _ipVersion;        // 4 or 6

    private static ReadOnlySpan<byte> MetadataMarker =>
        [0xAB, 0xCD, 0xEF, (byte)'M', (byte)'a', (byte)'x', (byte)'M', (byte)'i', (byte)'n', (byte)'d', (byte)'.', (byte)'c', (byte)'o', (byte)'m'];

    public MmdbReader(byte[] bytes)
    {
        _buf = bytes;

        int markerPos = -1;
        for (int i = bytes.Length - MetadataMarker.Length; i >= 0; i--)
        {
            if (bytes.AsSpan(i, MetadataMarker.Length).SequenceEqual(MetadataMarker))
            {
                markerPos = i;
                break;
            }
        }
        if (markerPos < 0) throw new InvalidDataException("MMDB metadata marker not found.");

        int metaStart = markerPos + MetadataMarker.Length;

        // Metadata is decoded as if the data section started at metaStart so
        // its internal pointers resolve correctly.
        var metadata = (Dictionary<string, object?>)Decode(metaStart, metaStart, out _)!;

        _nodeCount    = (int)AsLong(metadata["node_count"]!);
        _recordSize   = (int)AsLong(metadata["record_size"]!);
        _ipVersion    = (int)AsLong(metadata["ip_version"]!);
        _nodeByteSize = _recordSize * 2 / 8;
        _treeSizeBytes = _nodeCount * _nodeByteSize;
        _dataSectionStart = _treeSizeBytes + 16;
    }

    /// <summary>
    /// Returns the value at the deepest map path for the given IP. e.g.
    /// <c>FindString(ip, "country", "iso_code")</c>. Null if no match or any
    /// path key is missing.
    /// </summary>
    public string? FindString(IPAddress ip, params string[] path)
    {
        var record = Find(ip);
        if (record is null) return null;

        object? cur = record;
        foreach (var key in path)
        {
            if (cur is not Dictionary<string, object?> map || !map.TryGetValue(key, out var next)) return null;
            cur = next;
        }
        return cur as string;
    }

    public Dictionary<string, object?>? Find(IPAddress ip)
    {
        byte[] bytes;

        if (_ipVersion == 6)
        {
            // IPv4 inputs are walked as ::ffff:a.b.c.d through the full v6
            // tree. A precomputed start-node optimisation is possible but
            // 128 bit-walks per lookup is already cheap.
            bytes = ip.AddressFamily == AddressFamily.InterNetworkV6
                ? ip.GetAddressBytes()
                : ToV4MappedV6Bytes(ip.GetAddressBytes());
        }
        else
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork) return null;
            bytes = ip.GetAddressBytes();
        }

        int totalBits = bytes.Length * 8;
        int node = 0;
        for (int bitIdx = 0; bitIdx < totalBits; bitIdx++)
        {
            if (node >= _nodeCount) break;
            int bit = (bytes[bitIdx >> 3] >> (7 - (bitIdx & 7))) & 1;
            node = ReadNodeRecord(node, bit);
        }

        if (node == _nodeCount) return null;          // explicit "no data" terminator
        if (node < _nodeCount) return null;           // reached a node without consuming all bits

        // Spec: data offset = (record - nodeCount) - 16, expressed relative
        // to the start of the data section (which itself sits 16 bytes after
        // the search tree thanks to the zero-padded separator).
        int dataOffset = node - _nodeCount - 16;
        int absOffset = _dataSectionStart + dataOffset;
        return Decode(absOffset, _dataSectionStart, out _) as Dictionary<string, object?>;
    }

    private int ReadNodeRecord(int node, int side)
    {
        int baseByte = node * _nodeByteSize;
        switch (_recordSize)
        {
            case 24:
            {
                int off = baseByte + (side == 0 ? 0 : 3);
                return (_buf[off] << 16) | (_buf[off + 1] << 8) | _buf[off + 2];
            }
            case 28:
                // Middle byte is split: high nibble belongs to left record,
                // low nibble to right record (both as the high 4 bits of a
                // 28-bit value).
                if (side == 0)
                    return ((_buf[baseByte + 3] & 0xF0) << 20)
                         | (_buf[baseByte] << 16)
                         | (_buf[baseByte + 1] << 8)
                         |  _buf[baseByte + 2];
                return ((_buf[baseByte + 3] & 0x0F) << 24)
                     | (_buf[baseByte + 4] << 16)
                     | (_buf[baseByte + 5] << 8)
                     |  _buf[baseByte + 6];
            case 32:
            {
                int off = baseByte + (side == 0 ? 0 : 4);
                return (_buf[off] << 24) | (_buf[off + 1] << 16) | (_buf[off + 2] << 8) | _buf[off + 3];
            }
            default:
                throw new InvalidDataException($"Unsupported record size: {_recordSize}");
        }
    }

    /// <summary>
    /// Decodes the value at <paramref name="offset"/>. <paramref name="dataSectionStart"/>
    /// is added to relative pointer values; for the metadata block this is the
    /// metadata start, for record lookups it's <see cref="_dataSectionStart"/>.
    /// </summary>
    private object? Decode(int offset, int dataSectionStart, out int next)
    {
        byte ctrl = _buf[offset];
        int type = ctrl >> 5;
        int size = ctrl & 0x1F;
        int cur = offset + 1;

        if (type == 0)
        {
            type = _buf[cur] + 7;
            cur++;
        }

        if (type == 1) // pointer
        {
            int sizeClass = (ctrl >> 3) & 0x03;
            int pointerValue;
            switch (sizeClass)
            {
                case 0:
                    pointerValue = ((ctrl & 0x07) << 8) | _buf[cur];
                    cur += 1;
                    break;
                case 1:
                    pointerValue = ((ctrl & 0x07) << 16) | (_buf[cur] << 8) | _buf[cur + 1];
                    pointerValue += 2048;
                    cur += 2;
                    break;
                case 2:
                    pointerValue = ((ctrl & 0x07) << 24) | (_buf[cur] << 16) | (_buf[cur + 1] << 8) | _buf[cur + 2];
                    pointerValue += 526336;
                    cur += 3;
                    break;
                default:
                    pointerValue = (_buf[cur] << 24) | (_buf[cur + 1] << 16) | (_buf[cur + 2] << 8) | _buf[cur + 3];
                    cur += 4;
                    break;
            }
            next = cur;
            return Decode(dataSectionStart + pointerValue, dataSectionStart, out _);
        }

        // Resolve extended size encodings.
        if (size == 29)      { size = 29   + _buf[cur]; cur += 1; }
        else if (size == 30) { size = 285  + ((_buf[cur] << 8) | _buf[cur + 1]); cur += 2; }
        else if (size == 31) { size = 65821 + ((_buf[cur] << 16) | (_buf[cur + 1] << 8) | _buf[cur + 2]); cur += 3; }

        switch (type)
        {
            case 2: // utf8 string
            {
                var s = Encoding.UTF8.GetString(_buf, cur, size);
                next = cur + size;
                return s;
            }
            case 3: // double (big-endian IEEE 754)
                next = cur + size;
                return BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(_buf.AsSpan(cur, 8)));
            case 4: // bytes
            {
                var bytes = _buf.AsSpan(cur, size).ToArray();
                next = cur + size;
                return bytes;
            }
            case 5: case 6: case 9: // u16, u32, u64 — promote to long
            {
                long u = 0;
                for (int i = 0; i < size; i++) u = (u << 8) | _buf[cur + i];
                next = cur + size;
                return u;
            }
            case 7: // map
            {
                var map = new Dictionary<string, object?>(size);
                int c = cur;
                for (int i = 0; i < size; i++)
                {
                    var key = (string)Decode(c, dataSectionStart, out int kn)!;
                    var val = Decode(kn, dataSectionStart, out int vn);
                    map[key] = val;
                    c = vn;
                }
                next = c;
                return map;
            }
            case 8: // i32 (sign-extend)
            {
                int si = 0;
                for (int i = 0; i < size; i++) si = (si << 8) | _buf[cur + i];
                if (size > 0 && size < 4 && (_buf[cur] & 0x80) != 0)
                    si |= -1 << (size * 8);
                next = cur + size;
                return (long)si;
            }
            case 10: // u128 — represent as raw bytes; we don't read these
                next = cur + size;
                return _buf.AsSpan(cur, size).ToArray();
            case 11: // array
            {
                var arr = new List<object?>(size);
                int c = cur;
                for (int i = 0; i < size; i++)
                {
                    var v = Decode(c, dataSectionStart, out int nn);
                    arr.Add(v);
                    c = nn;
                }
                next = c;
                return arr;
            }
            case 14: // bool (size encodes the value 0/1; no payload bytes)
                next = cur;
                return size != 0;
            case 15: // float (big-endian IEEE 754)
                next = cur + size;
                return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(_buf.AsSpan(cur, 4)));
            default: // 12 (cache container), 13 (end marker), unknown — skip
                next = cur + size;
                return null;
        }
    }

    private static long AsLong(object o) => o switch
    {
        long l => l,
        int i  => i,
        _      => throw new InvalidDataException($"Expected integer in metadata, got {o?.GetType().Name ?? "null"}"),
    };

    private static byte[] ToV4MappedV6Bytes(byte[] v4)
    {
        var v6 = new byte[16];
        v6[10] = 0xFF;
        v6[11] = 0xFF;
        v6[12] = v4[0];
        v6[13] = v4[1];
        v6[14] = v4[2];
        v6[15] = v4[3];
        return v6;
    }
}
