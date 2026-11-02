using System.Text;

namespace Emd.Core.Manufacturing;

public static class KlipperWire
{
    public const byte SyncByte = 0x7e;
    public const int MinBlockSize = 5;
    public const int MaxBlockSize = 64;

    public static readonly IReadOnlyDictionary<int, string> CommandNames =
        new Dictionary<int, string>
        {
            [1] = "identify",
            [2] = "get_config",
            [3] = "get_clock",
            [4] = "allocate_oids",
            [5] = "config_stepper",
            [6] = "config_endstop",
            [7] = "endstop_set_stepper",
            [8] = "finalize_config",
            [9] = "reset_step_clock",
            [10] = "set_next_step_dir",
            [11] = "queue_step",
            [12] = "stepper_get_position",
            [13] = "endstop_home",
            [14] = "endstop_query_state",
            [15] = "config_pwm_out",
            [16] = "set_pwm_out"
        };

    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xffff;
        foreach (var value in data)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
        }
        return crc;
    }

    public static byte[] Frame(ReadOnlySpan<byte> content, byte sequence)
    {
        var length = MinBlockSize + content.Length;
        if (length > MaxBlockSize)
            throw new ArgumentException($"Klipper block exceeds {MaxBlockSize} bytes.");
        var block = new byte[length];
        block[0] = (byte)length;
        block[1] = (byte)(0x10 | (sequence & 0x0f));
        content.CopyTo(block.AsSpan(2));
        var crc = Crc16(block.AsSpan(0, 2 + content.Length));
        block[^3] = (byte)(crc >> 8);
        block[^2] = (byte)(crc & 0xff);
        block[^1] = SyncByte;
        return block;
    }

    public static byte[] EncodeVlq(long value)
    {
        var bytes = new List<byte>(5);
        if (value >= 0xc000000 || value < -0x4000000)
            bytes.Add((byte)(((value >> 28) & 0x7f) | 0x80));
        if (value >= 0x180000 || value < -0x80000)
            bytes.Add((byte)(((value >> 21) & 0x7f) | 0x80));
        if (value >= 0x3000 || value < -0x1000)
            bytes.Add((byte)(((value >> 14) & 0x7f) | 0x80));
        if (value >= 0x60 || value < -0x20)
            bytes.Add((byte)(((value >> 7) & 0x7f) | 0x80));
        bytes.Add((byte)(value & 0x7f));
        return bytes.ToArray();
    }

    public static (long Value, int Bytes) DecodeVlq(IReadOnlyList<byte> data, int offset)
    {
        var current = data[offset];
        long value = current & 0x7f;
        if ((current & 0x60) == 0x60)
            value |= -0x20;
        var count = 1;
        while ((current & 0x80) != 0)
        {
            current = data[offset + count];
            value = (value << 7) | (long)(current & 0x7f);
            count++;
        }
        return (value, count);
    }

    public static byte[] EncodeVlqAll(params long[] values) =>
        values.SelectMany(EncodeVlq).ToArray();

    public static IReadOnlyList<long> DecodeVlqAll(byte[] data)
    {
        var values = new List<long>();
        var offset = 0;
        while (offset < data.Length)
        {
            var (value, bytes) = DecodeVlq(data, offset);
            values.Add(value);
            offset += bytes;
        }
        return values;
    }
}

public sealed class KlipperBlockParser
{
    private readonly List<byte> _buffer = [];
    private bool _needSync;

    public int CorruptBlocks { get; private set; }

    public void Feed(IEnumerable<byte> bytes) => _buffer.AddRange(bytes);

    public (byte Sequence, byte[] Content)? TryParse()
    {
        while (true)
        {
            if (_needSync)
            {
                var sync = _buffer.IndexOf(KlipperWire.SyncByte);
                if (sync < 0)
                {
                    _buffer.Clear();
                    return null;
                }
                _buffer.RemoveRange(0, sync + 1);
                _needSync = false;
            }

            if (_buffer.Count < KlipperWire.MinBlockSize)
                return null;
            var length = _buffer[0];
            if (length is < KlipperWire.MinBlockSize or > KlipperWire.MaxBlockSize)
            {
                if (_buffer[0] == KlipperWire.SyncByte)
                    _buffer.RemoveAt(0);
                else
                    _needSync = true;
                CorruptBlocks++;
                continue;
            }
            if ((_buffer[1] & 0xf0) != 0x10)
            {
                _needSync = true;
                CorruptBlocks++;
                continue;
            }
            if (_buffer.Count < length)
                return null;
            if (_buffer[length - 1] != KlipperWire.SyncByte)
            {
                _needSync = true;
                CorruptBlocks++;
                continue;
            }

            var contentLength = length - KlipperWire.MinBlockSize;
            var expected = (ushort)((_buffer[length - 3] << 8) | _buffer[length - 2]);
            var actual = KlipperWire.Crc16(_buffer.GetRange(0, 2 + contentLength).ToArray());
            if (actual != expected)
            {
                _needSync = true;
                CorruptBlocks++;
                continue;
            }

            var content = _buffer.GetRange(2, contentLength).ToArray();
            var sequence = (byte)(_buffer[1] & 0x0f);
            _buffer.RemoveRange(0, length);
            return (sequence, content);
        }
    }
}
