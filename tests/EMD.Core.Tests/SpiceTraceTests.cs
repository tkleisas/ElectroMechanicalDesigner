using Emd.Core.Exporters;
using Emd.Core.Simulation;

namespace Emd.Core.Tests;

public class SpiceTraceTests
{
    [Fact]
    public void Loader_parses_wrdata_header_and_rows()
    {
        var result = SpiceTraceLoader.Default.Load(
            "time v(out) i(v1)\n0 1 0.1\n1 2 0.2\n");

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var trace = result.Trace!;
        Assert.Equal(["v_out", "i_v1"], trace.Signals);
        Assert.Equal([0d, 1d], trace.Times);
        Assert.Equal([1d, 2d], trace.Values[0]);
        Assert.Equal([0.1d, 0.2d], trace.Values[1]);
    }

    [Fact]
    public void Loader_reports_malformed_rows()
    {
        var result = SpiceTraceLoader.Default.Load("time v(out)\n0 1\n1 nope\n");

        Assert.Null(result.Trace);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-SPICE-005");
    }

    [Fact]
    public void Source_builder_emits_interpolating_python_fmu()
    {
        var trace = new SpiceTrace(
            ["v_out", "i_v1"],
            [0, 1],
            [[1, 2], [0.1, 0.2]]);

        var source = SpiceFmuSourceBuilder.Default.Build(trace);

        Assert.Equal(SpiceFmuSourceBuilder.ClassName, source.ClassName);
        Assert.Equal(["v_out", "i_v1"], source.Variables);
        Assert.Contains("TIMES = [0, 1]", source.Source);
        Assert.Contains("class SpiceTraceSlave(Fmi2Slave):", source.Source);
        Assert.Contains("bisect_left", source.Source);
        Assert.Contains("\"v_out\": [1, 2]", source.Source);
    }
}
