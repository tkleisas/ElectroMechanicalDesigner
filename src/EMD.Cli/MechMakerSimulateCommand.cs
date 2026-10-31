using System.Globalization;
using Emd.Adapters.MechMaker;
using Emd.Core.Diagnostics;

namespace Emd.Cli;

public static class MechMakerSimulateCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        IMechMakerToolClient client)
    {
        if (args.Length == 0 || !string.Equals(args[0], "mechmaker", StringComparison.OrdinalIgnoreCase))
            return Usage(stderr, "simulate expects the 'mechmaker' subcommand.");

        var machine = GetOption(args, "--machine");
        var output = GetOption(args, "--out");
        if (string.IsNullOrWhiteSpace(machine))
            return Usage(stderr, "simulate mechmaker requires --machine <machine.json>.");
        if (string.IsNullOrWhiteSpace(output))
            return Usage(stderr, "simulate mechmaker requires --out <trace.csv>.");

        if (!TryDouble(args, "--duration", 1.0, out var duration) ||
            !TryDouble(args, "--interval", 0.1, out var interval) ||
            !TryInt(args, "--microsteps", 16, out var microsteps))
        {
            return Usage(stderr, "Invalid --duration, --interval, or --microsteps value.");
        }

        var motors = new List<MechMakerMotorCommand>();
        foreach (var value in GetOptions(args, "--motor"))
        {
            var parts = value.Split(':', 2);
            if (parts.Length != 2 ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var revPerSec))
            {
                return Usage(stderr, $"Invalid --motor value '{value}'; expected <component>:<rev/s>.");
            }

            motors.Add(new MechMakerMotorCommand(parts[0], revPerSec));
        }

        var recorder = new MechMakerSimulationRecorder(client);
        var result = await recorder.RecordAsync(new MechMakerRunOptions(
            machine,
            duration,
            interval,
            microsteps,
            motors));

        foreach (var diagnostic in result.Diagnostics)
            stderr.WriteLine(diagnostic);

        if (result.TraceCsv is null)
            return 1;

        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(output, result.TraceCsv);

        var rows = result.TraceCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;
        stdout.WriteLine($"simulated: {machine} -> {output} ({rows} rows)");
        return result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        var prefix = name + "=";
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.StartsWith(prefix, StringComparison.Ordinal))
                return argument[prefix.Length..];
            if (argument == name && index + 1 < args.Length)
                return args[index + 1];
        }

        return null;
    }

    private static IEnumerable<string> GetOptions(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == name && index + 1 < args.Length)
                yield return args[index + 1];
            else if (args[index].StartsWith(name + "=", StringComparison.Ordinal))
                yield return args[index][(name.Length + 1)..];
        }
    }

    private static bool TryDouble(string[] args, string name, double fallback, out double value)
    {
        var text = GetOption(args, name);
        if (text is null)
        {
            value = fallback;
            return true;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryInt(string[] args, string name, int fallback, out int value)
    {
        var text = GetOption(args, name);
        if (text is null)
        {
            value = fallback;
            return true;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static int Usage(TextWriter stderr, string message)
    {
        stderr.WriteLine($"emd: {message}");
        return 2;
    }
}
