using Emd.Adapters.MechMaker;

namespace Emd.Adapters.Tests;

public class MechMakerStatusParserTests
{
    [Fact]
    public void Parses_time_motors_and_endstops()
    {
        const string text = """
            t = 0.5 s
            x_motor: commanded 0.25 rev / actual 0.24 rev, velocity 1 rev/s (target 1 rev/s), missed steps 2, STALLED
            endstop x_carriage: PRESSED
            servo s1: target 10 deg, at 9 deg
            """;

        var success = MechMakerStatusParser.TryParse(text, out var status, out var error);

        Assert.True(success, error);
        Assert.Equal(0.5, status!.Time);
        var motor = Assert.Single(status.Motors);
        Assert.Equal("x_motor", motor.Id);
        Assert.Equal(0.25, motor.CommandedRev);
        Assert.Equal(0.24, motor.ActualRev);
        Assert.Equal(1, motor.VelocityRevPerSec);
        Assert.Equal(2, motor.MissedSteps);
        Assert.True(motor.Stalled);
        Assert.True(Assert.Single(status.Endstops).Pressed);
    }

    [Fact]
    public void Parses_disabled_motors()
    {
        const string text = """
            t = 0.1 s
            y_motor (disabled): commanded 0 rev / actual 0 rev, velocity 0 rev/s (target 0 rev/s), missed steps 0
            """;

        var success = MechMakerStatusParser.TryParse(text, out var status, out var error);

        Assert.True(success, error);
        Assert.True(Assert.Single(status!.Motors).Disabled);
    }

    [Fact]
    public void Missing_time_is_reported()
    {
        var success = MechMakerStatusParser.TryParse("x_motor: nonsense", out var status, out var error);

        Assert.False(success);
        Assert.Null(status);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
