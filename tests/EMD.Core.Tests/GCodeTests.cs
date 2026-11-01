using Emd.Core.Manufacturing;

namespace Emd.Core.Tests;

public class GCodeTests
{
    [Fact]
    public void Parser_tracks_moves_bounds_and_lengths()
    {
        var result = GCodeParser.Default.Parse("""
            G21
            G90
            G0 X10 Y0 F1000
            G1 X10 Y10 F500
            """);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var program = result.Program!;
        Assert.Equal(2, program.Moves.Count);
        Assert.Equal(10, program.RapidLengthMm, 6);
        Assert.Equal(10, program.CutLengthMm, 6);
        Assert.Equal(new double[] { 0, 0, 0 }, program.Min);
        Assert.Equal(new double[] { 10, 10, 0 }, program.Max);
        Assert.Equal(500, program.Moves[^1].FeedMmMin);
    }

    [Fact]
    public void Parser_converts_inches_and_relative_moves_to_millimetres()
    {
        var result = GCodeParser.Default.Parse("G20\nG91\nG1 X1 Y2 F10\n");

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var move = Assert.Single(result.Program!.Moves);
        Assert.Equal(25.4, move.End[0], 6);
        Assert.Equal(50.8, move.End[1], 6);
        Assert.Equal(254, move.FeedMmMin, 6);
    }

    [Fact]
    public void Validator_reports_envelope_and_feed_errors()
    {
        var parsed = GCodeParser.Default.Parse("G21\nG90\nG1 X20 Y0 F0\n");
        Assert.NotNull(parsed.Program);

        var diagnostics = GCodeValidator.Default.Validate(
            parsed.Program!,
            [0, 0, 0],
            [10, 10, 10],
            maxFeedMmMin: 1000);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-CAM-001");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-CAM-002");
    }

    [Fact]
    public void Postprocessor_normalises_line_endings_and_adds_header()
    {
        var output = GCodePostProcessor.Default.Process("G1 X1\r\nG1 X2\r\n", "klipper");

        Assert.StartsWith("; EMD postprocessor: Klipper\n", output);
        Assert.Contains("G1 X1\nG1 X2", output);
    }

    [Fact]
    public void Postprocessor_adds_flavor_footer()
    {
        var output = GCodePostProcessor.Default.Process("G1 X1\n", "grbl");

        Assert.StartsWith("; EMD postprocessor: GRBL", output);
        Assert.EndsWith("M2\n", output);
    }
}
