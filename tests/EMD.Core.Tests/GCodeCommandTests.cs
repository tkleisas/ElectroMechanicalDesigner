using Emd.Cli;

namespace Emd.Core.Tests;

public class GCodeCommandTests
{
    [Fact]
    public void Check_reports_bounds_and_validity()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "part.gcode");
        File.WriteAllText(path, "G21\nG90\nG0 X1 Y1 F1000\nG1 X5 Y5 F500\n");

        var (exitCode, stdout, stderr) = Run(
            "check", "--gcode", path, "--envelope", "0,0,0,10,10,10");

        Assert.Equal(0, exitCode);
        Assert.Contains("bounds", stdout);
        Assert.Contains("valid", stdout);
        Assert.DoesNotContain("EMD-CAM", stderr);
    }

    [Fact]
    public void Check_fails_outside_the_envelope()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "part.gcode");
        File.WriteAllText(path, "G21\nG90\nG1 X20 Y0 F500\n");

        var (exitCode, _, stderr) = Run(
            "check", "--gcode", path, "--envelope", "0,0,0,10,10,10");

        Assert.Equal(1, exitCode);
        Assert.Contains("EMD-CAM-001", stderr);
    }

    [Fact]
    public void Post_writes_a_flavoured_header()
    {
        using var temp = new TempDirectory();
        var input = Path.Combine(temp.Path, "part.gcode");
        var output = Path.Combine(temp.Path, "part.klipper.gcode");
        File.WriteAllText(input, "G1 X1\r\n");

        var (exitCode, stdout, _) = Run(
            "post", "--gcode", input, "--out", output, "--flavor", "klipper");

        Assert.Equal(0, exitCode);
        Assert.Contains("posted gcode", stdout);
        var text = File.ReadAllText(output);
        Assert.StartsWith("; EMD postprocessor: Klipper", text);
        Assert.Contains("G1 X1", text);
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = GCodeCommand.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }
}
