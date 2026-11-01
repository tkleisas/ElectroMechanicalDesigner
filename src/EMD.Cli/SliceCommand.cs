using System.Globalization;
using Emd.Adapters;
using Emd.Core.Manufacturing;

namespace Emd.Cli;

public static class SliceCommand
{
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        SlicerAdapter adapter)
    {
        var model = GetOption(args, "--model");
        var output = GetOption(args, "--out");
        var flavor = GetOption(args, "--slicer") ?? "orca";
        if (string.IsNullOrWhiteSpace(model))
            return Usage(stderr, "slice requires --model <file.stl>.");
        if (string.IsNullOrWhiteSpace(output))
            return Usage(stderr, "slice requires --out <file.gcode>.");

        var profiles = GetOptions(args, "--profile");
        var result = adapter.SliceAsync(model, output, flavor, profiles).GetAwaiter().GetResult();
        foreach (var diagnostic in result.Diagnostics)
            stderr.WriteLine(diagnostic);
        if (!result.Success)
            return 1;

        var parsed = GCodeParser.Default.Parse(File.ReadAllText(output), output);
        foreach (var diagnostic in parsed.Diagnostics)
            stderr.WriteLine(diagnostic);
        if (parsed.Program is null)
            return 1;

        var envelope = ParseVec(GetOption(args, "--envelope"));
        var maxFeed = ParseDouble(GetOption(args, "--max-feed"));
        var validation = GCodeValidator.Default.Validate(
            parsed.Program,
            envelope?[..3],
            envelope?[3..],
            maxFeed);
        foreach (var diagnostic in validation)
            stderr.WriteLine(diagnostic);

        stdout.WriteLine(
            $"sliced: {Path.GetFileName(model)} -> {output} " +
            $"({parsed.Program.Moves.Count} moves)");
        return validation.Any(diagnostic =>
            diagnostic.Severity == Emd.Core.Diagnostics.DiagnosticSeverity.Error) ? 1 : 0;
    }

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

    private static List<string> GetOptions(string[] args, string name)
    {
        var values = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == name && index + 1 < args.Length)
                values.Add(args[index + 1]);
            else if (args[index].StartsWith(name + "=", StringComparison.Ordinal))
                values.Add(args[index][(name.Length + 1)..]);
        }
        return values;
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

    private static int Usage(TextWriter stderr, string message)
    {
        stderr.WriteLine($"emd: {message}");
        return 2;
    }
}
