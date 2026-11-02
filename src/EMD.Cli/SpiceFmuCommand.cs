using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Emd.Adapters;
using Emd.Core.Diagnostics;
using Emd.Core.Exporters;
using Emd.Core.Simulation;

namespace Emd.Cli;

public static class SpiceFmuCommand
{
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        IToolProcessRunner runner)
    {
        var netlist = GetOption(args, "--netlist");
        var output = GetOption(args, "--out");
        var signals = GetOption(args, "--signals");
        var stop = GetOption(args, "--stop-time") ?? "1";
        var step = GetOption(args, "--step-size") ?? "0.001";
        var ngspice = GetOption(args, "--ngspice") ?? "ngspice";
        var python = GetOption(args, "--python") ?? "python3";
        var closedLoop = args.Contains("--closed-loop");

        if (string.IsNullOrWhiteSpace(netlist))
            return Usage(stderr, "spice fmu requires --netlist <file.cir>.");
        if (string.IsNullOrWhiteSpace(output))
            return Usage(stderr, "spice fmu requires --out <file.fmu>.");
        if (string.IsNullOrWhiteSpace(signals))
            return Usage(stderr, "spice fmu requires --signals <v(out),...>.");
        if (!double.TryParse(stop, NumberStyles.Float, CultureInfo.InvariantCulture, out var stopTime) ||
            stopTime <= 0)
            return Usage(stderr, "spice fmu requires a positive --stop-time.");
        if (!double.TryParse(step, NumberStyles.Float, CultureInfo.InvariantCulture, out var stepSize) ||
            stepSize <= 0)
            return Usage(stderr, "spice fmu requires a positive --step-size.");

        var signalList = signals
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (signalList.Count == 0)
            return Usage(stderr, "spice fmu requires at least one signal.");

        string netlistText;
        try
        {
            netlistText = File.ReadAllText(netlist);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"emd: could not read SPICE netlist '{netlist}': {exception.Message}");
            return 1;
        }

        var work = Directory.CreateTempSubdirectory("emd-spice-fmu-");
        try
        {
            var slavePath = Path.Combine(work.FullName, "spice_trace_slave.py");
            var buildDirectory = Path.Combine(work.FullName, "build");
            Directory.CreateDirectory(buildDirectory);

            string className;
            string sourceText;
            IReadOnlyList<string> variables;
            if (closedLoop)
            {
                var inputs = (GetOption(args, "--inputs") ?? "")
                    .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                if (inputs.Count == 0)
                    return Usage(stderr, "spice fmu --closed-loop requires --inputs <name,...>.");
                SpiceClosedLoopSource source;
                try
                {
                    source = SpiceClosedLoopSourceBuilder.Default.Build(
                        netlistText, inputs, signalList);
                }
                catch (InvalidOperationException exception)
                {
                    stderr.WriteLine($"emd: {exception.Message}");
                    return 1;
                }
                className = source.ClassName;
                sourceText = source.Source;
                variables = source.Variables;
            }
            else
            {
                var wrapperPath = Path.Combine(work.FullName, "analysis.cir");
                var csvPath = Path.Combine(work.FullName, "trace.csv");
                var logPath = Path.Combine(work.FullName, "ngspice.log");
                File.WriteAllText(wrapperPath, Wrap(
                    netlistText, signalList, stopTime, stepSize, csvPath));
                var run = runner.RunAsync(
                    ngspice,
                    ["-b", "-o", logPath, wrapperPath],
                    timeout: TimeSpan.FromMinutes(2)).GetAwaiter().GetResult();
                PrintDiagnostics(run.Diagnostics, stderr);
                if (!run.Succeeded)
                {
                    if (run.Diagnostics.Count == 0)
                        stderr.WriteLine(
                            $"emd: ngspice failed with exit code {run.ExitCode}: {run.Stderr.Trim()}");
                    return 1;
                }

                if (!File.Exists(csvPath))
                {
                    stderr.WriteLine("EMD-SPICE-003 [Error] ngspice did not create the trace CSV.");
                    return 1;
                }

                var loaded = SpiceTraceLoader.Default.Load(
                    File.ReadAllText(csvPath), signalList, csvPath);
                PrintDiagnostics(loaded.Diagnostics, stderr);
                if (loaded.Trace is null)
                    return 1;

                var source = SpiceFmuSourceBuilder.Default.Build(loaded.Trace);
                className = source.ClassName;
                sourceText = source.Source;
                variables = source.Variables;
            }

            File.WriteAllText(slavePath, sourceText);
            var build = runner.RunAsync(
                python,
                ["-m", "pythonfmu", "build", "-f", slavePath, "--dest", buildDirectory],
                timeout: TimeSpan.FromMinutes(10)).GetAwaiter().GetResult();
            PrintDiagnostics(build.Diagnostics, stderr);
            if (!build.Succeeded)
            {
                stderr.WriteLine(
                    $"EMD-SPICE-004 [Error] pythonfmu failed to build the FMU: " +
                    (build.Stderr.Trim().Length == 0 ? $"exit code {build.ExitCode}" : build.Stderr.Trim()));
                return 1;
            }

            var builtFmu = Path.Combine(buildDirectory, $"{className}.fmu");
            if (!File.Exists(builtFmu))
            {
                stderr.WriteLine($"EMD-SPICE-004 [Error] pythonfmu did not create '{builtFmu}'.");
                return 1;
            }

            var outputPath = Path.GetFullPath(output);
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.Copy(builtFmu, outputPath, overwrite: true);

            stdout.WriteLine(
                $"exported SPICE FMU: {string.Join(", ", variables)} -> {outputPath}");
            return 0;
        }
        finally
        {
            try
            {
                work.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string Wrap(
        string netlist,
        IReadOnlyList<string> signals,
        double stopTime,
        double stepSize,
        string csvPath)
    {
        var builder = new StringBuilder();
        var wroteTitle = false;
        var inControl = false;
        foreach (var line in netlist.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(".control", StringComparison.OrdinalIgnoreCase))
            {
                inControl = true;
                continue;
            }
            if (inControl)
            {
                if (trimmed.StartsWith(".endc", StringComparison.OrdinalIgnoreCase))
                    inControl = false;
                continue;
            }
            if (trimmed.StartsWith(".tran", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, ".end", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!wroteTitle && trimmed.Length > 0)
            {
                builder.AppendLine(trimmed.StartsWith('*') ? line : "* emd spice fmu");
                wroteTitle = true;
            }
            builder.AppendLine(line);
        }

        if (!wroteTitle)
            builder.AppendLine("* emd spice fmu");

        builder.AppendLine(".control");
        builder.AppendLine("set wr_singlescale");
        builder.AppendLine("set wr_vecnames");
        builder.AppendLine($"tran {Format(stepSize)} {Format(stopTime)}");
        builder.AppendLine($"wrdata {Quote(csvPath)} {string.Join(' ', signals)}");
        builder.AppendLine(".endc");
        builder.AppendLine(".end");
        return builder.ToString();
    }

    private static string Quote(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

    private static string Format(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private static void PrintDiagnostics(IEnumerable<Diagnostic> diagnostics, TextWriter stderr)
    {
        foreach (var diagnostic in diagnostics)
            stderr.WriteLine(diagnostic);
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

    private static int Usage(TextWriter stderr, string message)
    {
        stderr.WriteLine($"emd: {message}");
        return 2;
    }
}
