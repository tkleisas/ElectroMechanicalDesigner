using Emd.Core.Manufacturing;

namespace Emd.Core.Tests;

public class KlipperWireTests
{
    [Fact]
    public void Vlq_round_trips_signed_values()
    {
        foreach (var value in new long[] { 0, 1, -1, 42, -32, 4096, -4096, 123456 })
        {
            var encoded = KlipperWire.EncodeVlq(value);
            var decoded = KlipperWire.DecodeVlq(encoded, 0);
            Assert.Equal(value, decoded.Value);
            Assert.Equal(encoded.Length, decoded.Bytes);
        }
    }

    [Fact]
    public void Block_parser_accepts_a_valid_frame()
    {
        var frame = KlipperWire.Frame(KlipperWire.EncodeVlqAll(4, 4), 3);
        var parser = new KlipperBlockParser();
        parser.Feed(frame);

        var block = parser.TryParse();

        Assert.NotNull(block);
        Assert.Equal(3, block!.Value.Sequence);
        Assert.Equal([4L, 4L], KlipperWire.DecodeVlqAll(block.Value.Content));
    }
}
