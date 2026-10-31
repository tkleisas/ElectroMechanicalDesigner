using System.Globalization;
using System.Security.Cryptography;
using Emd.Adapters.MechMaker;
using Emd.Core;
using Emd.Core.Diagnostics;
using Emd.Core.Exporters;
using Emd.Core.Identity;
using Emd.Core.Model;
using Emd.Core.Projects;
using Emd.Core.Simulation;

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

        var machineOption = GetOption(args, "--machine");
        var projectOption = GetOption(args, "--project");
        var scenario = GetOption(args, "--scenario");
        var output = GetOption(args, "--out");
        var saveTrace = args.Contains("--save-trace");

        if (string.IsNullOrWhiteSpace(output))
            return Usage(stderr, "simulate mechmaker requires --out <trace.csv>.");
        if (string.IsNullOrWhiteSpace(machineOption) == string.IsNullOrWhiteSpace(projectOption))
            return Usage(stderr, "simulate mechmaker requires exactly one of --machine or --project.");
        if (!string.IsNullOrWhiteSpace(scenario) && string.IsNullOrWhiteSpace(projectOption))
            return Usage(stderr, "--scenario requires --project.");
        if (saveTrace && (string.IsNullOrWhiteSpace(projectOption) || string.IsNullOrWhiteSpace(scenario)))
            return Usage(stderr, "--save-trace requires --project and --scenario.");

        if (!TryDouble(args, "--duration", 1.0, out var duration) ||
            !TryDouble(args, "--interval", 0.1, out var interval) ||
            !TryInt(args, "--microsteps", 16, out var microsteps))
        {
            return Usage(stderr, "Invalid --duration, --interval, or --microsteps value.");
        }

        Emd.Core.Model.EmdProject? project = null;
        string machinePath;

        if (!string.IsNullOrWhiteSpace(projectOption))
        {
            var loaded = ProjectLoader.Default.LoadFile(projectOption);
            foreach (var diagnostic in loaded.Diagnostics)
                stderr.WriteLine(diagnostic);
            if (loaded.Project is null)
                return 1;

            var exported = MechMakerMachineExporter.Default.Export(loaded.Project);
            foreach (var diagnostic in exported.Diagnostics)
                stderr.WriteLine(diagnostic);
            if (exported.Machine is null)
                return 1;

            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(output));
            var workDirectory = GetOption(args, "--work-dir") ??
                Path.Combine(outputDirectory ?? ".", "emd-mechmaker");
            Directory.CreateDirectory(workDirectory);
            machinePath = Path.Combine(workDirectory, "machine.json");
            File.WriteAllText(
                machinePath,
                exported.Machine.ToJsonString(CoreJson.Options));
            project = loaded.Project;
        }
        else
        {
            machinePath = machineOption!;
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
            machinePath,
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
        stdout.WriteLine($"simulated: {machinePath} -> {output} ({rows} rows)");

        if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return 1;

        if (saveTrace)
        {
            var content = File.ReadAllBytes(output);
            var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var artifactId = DeterministicGuid.Create(
                EmdNamespaces.MechMaker,
                $"trace:{projectOption}:{scenario}:{sha256}");
            var artifact = new Artifact
            {
                Id = artifactId,
                Key = $"trace_{scenario}",
                Path = Path.GetFullPath(output),
                Format = "csv",
                Role = ArtifactRole.Simulation,
                Sha256 = sha256,
                Bytes = content.LongLength
            };

            var artifacts = project!.Artifacts
                .Where(candidate => candidate.Id != artifactId)
                .ToList();
            artifacts.Add(artifact);

            var scenarios = project.Simulation!.Scenarios
                .Select(candidate => candidate.Key == scenario || candidate.Id.ToString() == scenario
                    ? candidate with { TraceArtifactId = artifactId }
                    : candidate)
                .ToList();

            var updated = project with
            {
                Artifacts = artifacts,
                Simulation = project.Simulation with { Scenarios = scenarios }
            };
            ProjectWriter.Save(updated, projectOption!);
            stdout.WriteLine($"saved trace artifact: {artifactId}");
        }

        if (!string.IsNullOrWhiteSpace(scenario))
        {
            var traceLoad = TraceCsvLoader.Default.Load(result.TraceCsv, output);
            foreach (var diagnostic in traceLoad.Diagnostics)
                stderr.WriteLine(diagnostic);
            if (traceLoad.Trace is null)
                return 1;

            var evaluation = TraceAssertionEvaluator.Default.Evaluate(project!, traceLoad.Trace, scenario);
            foreach (var diagnostic in evaluation.Diagnostics)
                stderr.WriteLine(diagnostic);

            if (!evaluation.Passed)
                return 1;

            stdout.WriteLine($"scenario '{scenario}' passed");
        }

        return 0;
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
