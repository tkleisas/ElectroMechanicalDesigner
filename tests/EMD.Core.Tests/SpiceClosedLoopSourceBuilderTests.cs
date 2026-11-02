using Emd.Core.Exporters;

namespace Emd.Core.Tests;

public class SpiceClosedLoopSourceBuilderTests
{
    [Fact]
    public void Builder_emits_inputs_outputs_and_template()
    {
        var source = SpiceClosedLoopSourceBuilder.Default.Build(
            "V1 in 0 {{vin}}\nR1 in out 1k\n",
            ["vin"],
            ["v(out)"]);

        Assert.Equal("ClosedLoopSpiceSlave", source.ClassName);
        Assert.Contains("V1 in 0 {{vin}}", source.Source);
        Assert.Contains("INPUTS = [\"vin\"]", source.Source);
        Assert.Contains("VARIABLES = [\"v_out\"]", source.Source);
    }
}
