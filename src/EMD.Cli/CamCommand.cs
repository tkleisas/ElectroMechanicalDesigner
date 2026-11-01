using System.Globalization;
using Emd.Adapters;
using Emd.Core.Manufacturing;
using Emd.Core.Projects;

namespace Emd.Cli;

public static class CamCommand
{
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CamoticsAdapter camotics)
    {
        if (args.Length == 0)
            return Usage(stderr, "cam expects 'verify' or 'simulate'.");
        if (string.Equals(args[0], "simulate", StringComparison.OrdinalIgnoreCase))
            return Simulate(args, stdout, stderr);

        if (!string.Equals(args[0], "verify", StringComparison.OrdinalIgnoreCase))
            return Usage(stderr, "cam expects 'verify' or 'simulate'.");

        var gcode = GetOption(args, "--gcode");
        if (string.IsNullOrWhiteSpace(gcode))
            return Usage(stderr, "cam verify requires --gcode <file>.");
        if (!File.Exists(gcode))
        {
            stderr.WriteLine($"emd: G-code file '{gcode}' was not found.");
            return 1;
        }

        var parsed = GCodeParser.Default.Parse(File.ReadAllText(gcode), gcode);
        foreach (var diagnostic in parsed.Diagnostics)
            stderr.WriteLine(diagnostic);
        if (parsed.Program is null)
            return 1;

        var envelope = ParseVec(GetOption(args, "--envelope"));
        if (envelope is null && GetOption(args, "--project") is { } projectPath)
        {
            var loaded = ProjectLoader.Default.LoadFile(projectPath);
            foreach (var diagnostic in loaded.Diagnostics)
                stderr.WriteLine(diagnostic);
            if (loaded.Project is null)
                return 1;
            var process = loaded.Project.Manufacturing?.Processes
                .FirstOrDefault(candidate => candidate.Key == "path_job");
            if (process?.Parameters is { } parameters &&
                parameters["stock_min_mm"] is { } minNode &&
                parameters["stock_max_mm"] is { } maxNode)
            {
                var min = ParseCsvVec(minNode.GetValue<string>());
                var max = ParseCsvVec(maxNode.GetValue<string>());
                if (min is not null && max is not null)
                    envelope = [min[0], min[1], min[2], max[0], max[1], max[2]];
            }
        }
        var maxFeed = ParseDouble(GetOption(args, "--max-feed"));
        var diagnostics = GCodeValidator.Default.Validate(
            parsed.Program, envelope?[..3], envelope?[3..], maxFeed);
        foreach (var diagnostic in diagnostics)
            stderr.WriteLine(diagnostic);
        if (diagnostics.Any(diagnostic =>
                diagnostic.Severity == Emd.Core.Diagnostics.DiagnosticSeverity.Error))
            return 1;

        var output = GetOption(args, "--out");
        var executable = GetOption(args, "--camotics");
        if (!string.IsNullOrWhiteSpace(executable))
        {
            if (string.IsNullOrWhiteSpace(output))
                return Usage(stderr, "cam verify with --camotics requires --out <surface.stl>.");
            var result = camotics.VerifyAsync(gcode, output, executable).GetAwaiter().GetResult();
            foreach (var diagnostic in result.Diagnostics)
                stderr.WriteLine(diagnostic);
            if (!result.Success)
                return 1;
            stdout.WriteLine($"camotics surface: {output}");
        }

        stdout.WriteLine(
            $"cam: {parsed.Program.Moves.Count} move(s), bounds " +
            $"{Format(parsed.Program.Min)} .. {Format(parsed.Program.Max)}");
        stdout.WriteLine("cam verification: valid");
        return 0;
    }

    private static int Simulate(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var gcode = GetOption(args, "--gcode");
        var target = GetOption(args, "--target");
        var stock = ParseVec(GetOption(args, "--stock"));
        var output = GetOption(args, "--out");
        if (string.IsNullOrWhiteSpace(gcode))
            return Usage(stderr, "cam simulate requires --gcode <file>.");
        if (string.IsNullOrWhiteSpace(target))
            return Usage(stderr, "cam simulate requires --target <design.stl>.");
        if (stock is null)
            return Usage(stderr, "cam simulate requires --stock xmin,ymin,zmin,xmax,ymax,zmax.");
        if (string.IsNullOrWhiteSpace(output))
            return Usage(stderr, "cam simulate requires --out <report.json>.");
        if (!double.TryParse(GetOption(args, "--tool-diameter") ?? "3.175",
                NumberStyles.Float, CultureInfo.InvariantCulture, out var toolDiameter) ||
            toolDiameter <= 0)
            return Usage(stderr, "cam simulate requires a positive --tool-diameter.");
        if (!double.TryParse(GetOption(args, "--cell") ?? "1",
                NumberStyles.Float, CultureInfo.InvariantCulture, out var cell) ||
            cell <= 0)
            return Usage(stderr, "cam simulate requires a positive --cell.");

        var parsed = GCodeParser.Default.Parse(File.ReadAllText(gcode), gcode);
        foreach (var diagnostic in parsed.Diagnostics)
            stderr.WriteLine(diagnostic);
        if (parsed.Program is null)
            return 1;
        var mesh = StlMeshLoader.Default.LoadFile(target);
        foreach (var diagnostic in mesh.Diagnostics)
            stderr.WriteLine(diagnostic);
        if (mesh.Triangles is null || mesh.Triangles.Count == 0)
            return 1;

        var min = new[] { stock[0], stock[1], stock[2] };
        var max = new[] { stock[3], stock[4], stock[5] };
        Heightmap heightmap;
        try
        {
            heightmap = Heightmap.FromStl(mesh.Triangles, min, max, cell);
        }
        catch (InvalidOperationException exception)
        {
            stderr.WriteLine($"emd: {exception.Message}");
            return 1;
        }
        var stockMap = Heightmap.Stock(min, max, cell);
        stockMap.Simulate(parsed.Program, toolDiameter);
        var report = stockMap.Compare(heightmap, ParseDouble(GetOption(args, "--tolerance")) ?? 0.01);

        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(output, heightmap.ToJson(report).ToJsonString());
        stdout.WriteLine(
            $"cam simulation: gouge {report.GougeCells} cell(s), " +
            $"max {report.MaxGougeMm:0.###} mm; remaining {report.RemainingCells} cell(s), " +
            $"max {report.MaxRemainingMm:0.###} mm");
        return report.GougeCells == 0 && report.RemainingCells == 0 ? 0 : 1;
    }

    private static double[]? ParseCsvVec(string value)
    {
        var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length != 3)
            return null;
        var result = new double[3];
        for (var index = 0; index < 3; index++)
        {
            if (!double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out result[index]))
                return null;
        }
        return result;
    }

    private static double[]? ParseVec(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length != 6)
            return null;
        var result = new double[6];
        for (var index = 0; index < values.Length; index++)
        {
            if (!double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out result[index]))
                return null;
        }
        return result;
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static string Format(double[] values) =>
        string.Join(",", values.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)));

    private static string? GetOption(string[] args, string name)
    {
        var prefix = name + "=";
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith(prefix, StringComparison.Ordinal))
                return args[index][prefix.Length..];
            if (args[index] == name && index + 1 < args.Length)
                return args[index + 1];
        }
        return null;
    }

    private static int Usage(TextWriter stderr, string message)
    {
        stderr.WriteLine($"emd: {message}");
        return 2;
    }
}
