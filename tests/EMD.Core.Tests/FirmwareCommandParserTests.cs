using Emd.Core.Manufacturing;

namespace Emd.Core.Tests;

public class FirmwareCommandParserTests
{
    [Fact]
    public void Parses_motor_run_and_stop_commands()
    {
        Assert.True(FirmwareCommandParser.TryParse("MOTOR motor_left 1.5", out var motor, out _));
        Assert.Equal("motor", motor!.Kind);
        Assert.Equal("motor_left", motor.Target);
        Assert.Equal(1.5, motor.Value);

        Assert.True(FirmwareCommandParser.TryParse("RUN 0.25", out var run, out _));
        Assert.Equal("run", run!.Kind);
        Assert.Equal(0.25, run.Value);

        Assert.True(FirmwareCommandParser.TryParse("STOP motor_left", out var stop, out _));
        Assert.Equal("stop", stop!.Kind);
    }

    [Fact]
    public void Rejects_malformed_commands()
    {
        Assert.False(FirmwareCommandParser.TryParse("MOTOR", out _, out var error));
        Assert.NotNull(error);
    }
}
