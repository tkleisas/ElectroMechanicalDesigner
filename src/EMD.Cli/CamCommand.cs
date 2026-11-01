using System.Globalization;
using Emd.Adapters;
using Emd.Core.Manufacturing;

namespace Emd.Cli;

public static class CamCommand
{
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CamoticsAdapter camotics)
    {
        if (args.Length == 0 ||
            !string.Equals(args[0], "verify", StringComparison.OrdinalIgnoreCase))
            return Usage(stderr, "cam expects the 'verify' subcommand.");

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
