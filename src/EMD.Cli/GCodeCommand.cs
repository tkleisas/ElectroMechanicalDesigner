using System.Globalization;
using Emd.Core.Manufacturing;

namespace Emd.Cli;

public static class GCodeCommand
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
            return Usage(stderr, "gcode expects 'check' or 'post'.");

        var operation = args[0].ToLowerInvariant();
        var path = GetOption(args, "--gcode");
        if (string.IsNullOrWhiteSpace(path))
            return Usage(stderr, $"gcode {operation} requires --gcode <file>.");
        if (!File.Exists(path))
        {
            stderr.WriteLine($"emd: G-code file '{path}' was not found.");
            return 1;
        }

        return operation switch
        {
            "check" => Check(path, args, stdout, stderr),
            "post" => Post(path, args, stdout, stderr),
            _ => Usage(stderr, $"Unknown gcode operation '{operation}'.")
        };
    }

    private static int Check(
        string path,
        string[] args,
        TextWriter stdout,
        TextWriter stderr)
    {
        var parsed = GCodeParser.Default.Parse(File.ReadAllText(path), path);
        foreach (var diagnostic in parsed.Diagnostics)
            stderr.WriteLine(diagnostic);
        if (parsed.Program is null)
            return 1;

        var envelope = ParseVec(GetOption(args, "--envelope"));
        var maxFeed = ParseDouble(GetOption(args, "--max-feed"));
        var diagnostics = GCodeValidator.Default.Validate(
            parsed.Program,
            envelope?[..3],
            envelope?[3..],
            maxFeed);
        foreach (var diagnostic in diagnostics)
            stderr.WriteLine(diagnostic);

        var valid = diagnostics.All(diagnostic =>
            diagnostic.Severity != Emd.Core.Diagnostics.DiagnosticSeverity.Error);
        stdout.WriteLine(
            $"gcode: {parsed.Program.Moves.Count} move(s), " +
            $"{parsed.Program.CutLengthMm:0.###} mm cutting, " +
            $"{parsed.Program.RapidLengthMm:0.###} mm rapid, " +
            $"bounds {Format(parsed.Program.Min)} .. {Format(parsed.Program.Max)}");
        stdout.WriteLine(valid ? "gcode: valid" : "gcode: invalid");
        return valid ? 0 : 1;
    }

    private static int Post(
        string path,
        string[] args,
        TextWriter stdout,
        TextWriter stderr)
    {
        var output = GetOption(args, "--out");
        if (string.IsNullOrWhiteSpace(output))
            return Usage(stderr, "gcode post requires --out <file>.");
        var flavor = GetOption(args, "--flavor") ?? "klipper";
        var text = GCodePostProcessor.Default.Process(File.ReadAllText(path), flavor);
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(output, text);
        stdout.WriteLine($"posted gcode: {flavor} -> {output}");
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
