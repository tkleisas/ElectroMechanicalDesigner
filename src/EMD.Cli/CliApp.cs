using System.Text.Json;
using Emd.Adapters;
using Emd.Adapters.MechMaker;
using Emd.Core;
using Emd.Core.Artifacts;
using Emd.Core.Build;
using Emd.Core.Diagnostics;
using Emd.Core.Exporters;
using Emd.Core.Importers;
using Emd.Core.Model;
using Emd.Core.Packaging;
using Emd.Core.Projects;
using Emd.Core.Validation;

namespace Emd.Cli;

public static class CliApp
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage(stdout);
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "validate" => Validate(args, stdout, stderr),
                "import" => Import(args, stdout, stderr),
                "export" => Export(args, stdout, stderr),
                "kicad" => KiCadCliCommand.Run(
                    args.Skip(1).ToArray(),
                    stdout,
                    stderr,
                    new KiCadCliAdapter(ProcessToolHost.Default)),
                "spice" => SpiceCommand.Run(
                    args.Skip(1).ToArray(),
                    stdout,
                    stderr,
                    new NgSpiceAdapter(ProcessToolHost.Default)),
                "compile" => Compile(args.Skip(1).ToArray(), stdout, stderr),
                "package" => Package(args, stdout, stderr),
                "trace" => TraceCommand.Run(args.Skip(1).ToArray(), stdout, stderr),
                "simulate" => Simulate(args.Skip(1).ToArray(), stdout, stderr),
                "build" => Build(args, stdout, stderr),
                "artifacts" => Artifacts(args, stdout, stderr),
                _ => UsageError(stderr, $"Unknown command '{args[0]}'.")
            };
        }
        catch (Exception exception)
        {
            stderr.WriteLine($"emd: unexpected error: {exception.Message}");
            return 1;
        }
    }

    private static int Validate(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length != 2)
            return UsageError(stderr, "validate expects exactly one project path.");

        var loaded = ProjectLoader.Default.LoadFile(args[1]);
        PrintDiagnostics(loaded.Diagnostics, stderr);
        if (loaded.Project is null)
            return 1;

        var semantic = ProjectValidator.Default.Validate(loaded.Project);
        PrintDiagnostics(semantic, stderr);

        if (loaded.Diagnostics.Concat(semantic).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return 1;

        stdout.WriteLine(
            $"valid: {loaded.Project.Name} " +
            $"({loaded.Project.Parts.Count} parts, {loaded.Project.Connections.Count} connections)");
        return 0;
    }

    private static int Import(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length < 3)
            return UsageError(stderr, "import expects a source type and an input file.");

        return args[1].ToLowerInvariant() switch
        {
            "mechmaker" => ImportMechMaker(args, stdout, stderr),
            "kicad" => ImportKiCad(args, stdout, stderr),
            "freecad" => ImportFreeCad(args, stdout, stderr),
            "freecad-manifest" => ImportFreeCadManifest(args, stdout, stderr),
            "kicad-pcb" => ImportKiCadPcb(args, stdout, stderr),
            _ => UsageError(stderr, $"Unknown import source '{args[1]}'.")
        };
    }

    private static int ImportMechMaker(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var input = args[2];
        var output = GetOption(args, "--out");
        if (string.IsNullOrWhiteSpace(output))
            return UsageError(stderr, "import mechmaker requires --out <project.json>.");

        MechMakerCatalog? catalog = null;
        var catalogPath = GetOption(args, "--catalog");
        if (!string.IsNullOrWhiteSpace(catalogPath))
        {
            try
            {
                catalog = MechMakerCatalog.Load(catalogPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"emd: could not read catalog '{catalogPath}': {exception.Message}");
                return 1;
            }
        }

        var result = MechMakerImporter.Default.ImportFile(input, catalog);
        PrintDiagnostics(result.Diagnostics, stderr);
        if (result.Project is null)
            return 1;

        ProjectWriter.Save(result.Project, output);
        var nets = result.Project.Electrical?.Nets.Count ?? 0;
        stdout.WriteLine(
            $"imported: {result.Project.Name} -> {output} " +
            $"({result.Project.Parts.Count} parts, {result.Project.Connections.Count} connections, {nets} nets)");

        return result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static int ImportKiCad(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var input = args[2];
        var output = GetOption(args, "--out");
        if (string.IsNullOrWhiteSpace(output))
            return UsageError(stderr, "import kicad requires --out <project.json>.");

        var result = KiCadNetlistImporter.Default.ImportFile(input);
        PrintDiagnostics(result.Diagnostics, stderr);
        if (result.Project is null)
            return 1;

        ProjectWriter.Save(result.Project, output);
        var nets = result.Project.Electrical?.Nets.Count ?? 0;
        stdout.WriteLine(
            $"imported: {result.Project.Name} -> {output} " +
            $"({result.Project.Parts.Count} parts, {result.Project.Connections.Count} connections, {nets} nets)");

        return result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static int ImportFreeCad(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var input = args[2];
        var projectPath = GetOption(args, "--project");
        if (string.IsNullOrWhiteSpace(projectPath))
            return UsageError(stderr, "import freecad requires --project <project.json>.");

        var output = GetOption(args, "--out") ?? projectPath;
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        PrintDiagnostics(loaded.Diagnostics, stderr);
        if (loaded.Project is null)
            return 1;

        ArtifactStore? store = null;
        var storePath = GetOption(args, "--store");
        if (!string.IsNullOrWhiteSpace(storePath))
            store = new ArtifactStore(storePath);

        var result = FreeCadArtifactImporter.Default.AddReference(loaded.Project, input, store);
        PrintDiagnostics(result.Diagnostics, stderr);
        if (result.Project is null)
            return 1;

        ProjectWriter.Save(result.Project, output);
        stdout.WriteLine(
            $"added: {Path.GetFileName(input)} -> {output} " +
            $"({result.Project.Artifacts.Count} artifacts)");

        return result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static int ImportKiCadPcb(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var input = args[2];
        var projectPath = GetOption(args, "--project");
        if (string.IsNullOrWhiteSpace(projectPath))
            return UsageError(stderr, "import kicad-pcb requires --project <project.json>.");

        var output = GetOption(args, "--out") ?? projectPath;
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        PrintDiagnostics(loaded.Diagnostics, stderr);
        if (loaded.Project is null)
            return 1;

        var result = KiCadPcbOutlineImporter.Default.ImportFile(input);
        PrintDiagnostics(result.Diagnostics, stderr);
        if (result.Board is null || result.BoardEntry is null)
            return 1;

        var parts = loaded.Project.Parts
            .Where(part => part.Id != result.Board.Id)
            .ToList();
        parts.Add(result.Board);

        var electrical = loaded.Project.Electrical ?? new ElectricalSection();
        var boards = electrical.Boards
            .Where(board => board.Id != result.BoardEntry.Id)
            .ToList();
        boards.Add(result.BoardEntry);

        var mechanical = loaded.Project.Mechanical ?? new MechanicalSection();
        if (mechanical.AssemblyRootPartId is null)
            mechanical = mechanical with { AssemblyRootPartId = result.Board.Id };

        var updated = loaded.Project with
        {
            Parts = parts,
            Electrical = electrical with { Boards = boards },
            Mechanical = mechanical
        };

        ProjectWriter.Save(updated, output);
        stdout.WriteLine(
            $"imported PCB: {result.Board.Name} -> {output} " +
            $"({result.Board.Ports.Count} mounting holes)");

        return result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static int ImportFreeCadManifest(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var input = args[2];
        var output = GetOption(args, "--out");
        if (string.IsNullOrWhiteSpace(output))
            return UsageError(stderr, "import freecad-manifest requires --out <project.json>.");

        ArtifactStore? store = null;
        var storePath = GetOption(args, "--store");
        if (!string.IsNullOrWhiteSpace(storePath))
            store = new ArtifactStore(storePath);

        var result = FreeCadManifestImporter.Default.ImportFile(input, store);
        PrintDiagnostics(result.Diagnostics, stderr);
        if (result.Project is null)
            return 1;

        ProjectWriter.Save(result.Project, output);
        var joints = result.Project.Mechanical?.Joints?.Count ?? 0;
        stdout.WriteLine(
            $"imported: {result.Project.Name} -> {output} " +
            $"({result.Project.Parts.Count} parts, {joints} joints, {result.Project.Artifacts.Count} artifacts)");

        return result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static int Export(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length < 2)
            return UsageError(stderr, "export expects the 'mjcf' or 'bom' subcommand.");

        return args[1].ToLowerInvariant() switch
        {
            "mjcf" => ExportMjcf(args, stdout, stderr),
            "bom" => ExportBom(args, stdout, stderr),
            "harness" => ExportHarness(args, stdout, stderr),
            "mechmaker" => ExportMechMaker(args, stdout, stderr),
            _ => UsageError(stderr, $"Unknown export '{args[1]}'.")
        };
    }

    private static int ExportMjcf(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var projectPath = GetOption(args, "--project") ?? (args.Length > 2 ? args[2] : null);
        if (string.IsNullOrWhiteSpace(projectPath))
            return UsageError(stderr, "export mjcf requires --project <project.json>.");

        var output = GetOption(args, "--out");
        if (string.IsNullOrWhiteSpace(output))
            return UsageError(stderr, "export mjcf requires --out <scene.xml>.");

        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        PrintDiagnostics(loaded.Diagnostics, stderr);
        if (loaded.Project is null)
            return 1;

        var result = MjcfExporter.Default.Export(loaded.Project);
        PrintDiagnostics(result.Diagnostics, stderr);
        if (result.Xml is null)
            return 1;

        WriteTextFile(output, result.Xml);

        stdout.WriteLine(
            $"exported: {loaded.Project.Name} -> {output} ({loaded.Project.Parts.Count} parts)");

        return result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static int ExportBom(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var projectPath = GetOption(args, "--project") ?? (args.Length > 2 ? args[2] : null);
        if (string.IsNullOrWhiteSpace(projectPath))
            return UsageError(stderr, "export bom requires --project <project.json>.");

        var output = GetOption(args, "--out");
        if (string.IsNullOrWhiteSpace(output))
            return UsageError(stderr, "export bom requires --out <bom.csv>.");

        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        PrintDiagnostics(loaded.Diagnostics, stderr);
        if (loaded.Project is null)
            return 1;

        var result = BomExporter.Default.Export(loaded.Project);
        PrintDiagnostics(result.Diagnostics, stderr);
        if (result.Csv is null)
            return 1;

        WriteTextFile(output, result.Csv);
        stdout.WriteLine(
            $"exported BOM: {loaded.Project.Name} -> {output} ({loaded.Project.Parts.Count} parts)");

        return result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static int ExportHarness(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var projectPath = GetOption(args, "--project") ?? (args.Length > 2 ? args[2] : null);
        if (string.IsNullOrWhiteSpace(projectPath))
            return UsageError(stderr, "export harness requires --project <project.json>.");

        var output = GetOption(args, "--out");
        if (string.IsNullOrWhiteSpace(output))
            return UsageError(stderr, "export harness requires --out <harness.yml>.");

        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        PrintDiagnostics(loaded.Diagnostics, stderr);
        if (loaded.Project is null)
            return 1;

        var result = HarnessExporter.Default.Export(loaded.Project);
        PrintDiagnostics(result.Diagnostics, stderr);
        if (result.Yaml is null)
            return 1;

        WriteTextFile(output, result.Yaml);
        stdout.WriteLine(
            $"exported harness: {loaded.Project.Name} -> {output} " +
            $"({loaded.Project.Electrical?.Nets.Count ?? 0} nets)");

        return result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static int ExportMechMaker(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var projectPath = GetOption(args, "--project") ?? (args.Length > 2 ? args[2] : null);
        if (string.IsNullOrWhiteSpace(projectPath))
            return UsageError(stderr, "export mechmaker requires --project <project.json>.");

        var output = GetOption(args, "--out");
        if (string.IsNullOrWhiteSpace(output))
            return UsageError(stderr, "export mechmaker requires --out <machine.json>.");

        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        PrintDiagnostics(loaded.Diagnostics, stderr);
        if (loaded.Project is null)
            return 1;

        var result = MechMakerMachineExporter.Default.Export(loaded.Project);
        PrintDiagnostics(result.Diagnostics, stderr);
        if (result.Machine is null)
            return 1;

        var json = result.Machine.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        WriteTextFile(output, json);
        var partCount = result.Machine["parts"]?.AsArray().Count ?? 0;
        stdout.WriteLine(
            $"exported MechMaker machine: {loaded.Project.Name} -> {output} ({partCount} parts)");

        return result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static int Compile(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || !string.Equals(args[0], "mechmaker", StringComparison.OrdinalIgnoreCase))
            return UsageError(stderr, "compile expects the 'mechmaker' subcommand.");

        var toolPath = GetOption(args, "--mechmaker") ?? Environment.GetEnvironmentVariable("MECHMAKER_CLI");
        if (string.IsNullOrWhiteSpace(toolPath))
            return UsageError(stderr, "compile mechmaker requires --mechmaker <cli.dll|exe> or MECHMAKER_CLI.");

        var adapter = Path.GetExtension(toolPath).Equals(".dll", StringComparison.OrdinalIgnoreCase)
            ? new MechMakerCompileAdapter(ProcessToolHost.Default, "dotnet", [toolPath])
            : new MechMakerCompileAdapter(ProcessToolHost.Default, toolPath);
        return MechMakerCompileCommand.Run(args, stdout, stderr, adapter);
    }

    private static int Package(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length > 1 && string.Equals(args[1], "verify", StringComparison.OrdinalIgnoreCase))
            return VerifyPackage(args, stdout, stderr);

        var projectPath = GetOption(args, "--project") ?? (args.Length > 2 ? args[2] : null);
        if (string.IsNullOrWhiteSpace(projectPath))
            return UsageError(stderr, "package requires --project <project.json>.");

        var outputDirectory = GetOption(args, "--out-dir") ?? GetOption(args, "--out");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return UsageError(stderr, "package requires --out-dir <dir>.");

        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        PrintDiagnostics(loaded.Diagnostics, stderr);
        if (loaded.Project is null)
            return 1;

        var result = PackageExporter.Default.Export(loaded.Project);
        PrintDiagnostics(result.Diagnostics, stderr);
        if (result.Files.Count == 0)
            return 1;

        Directory.CreateDirectory(outputDirectory);
        foreach (var file in result.Files)
            File.WriteAllBytes(Path.Combine(outputDirectory, file.Name), file.Content);

        stdout.WriteLine(
            $"packaged: {loaded.Project.Name} -> {outputDirectory} ({result.Files.Count} files)");

        return result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            ? 1
            : 0;
    }

    private static int VerifyPackage(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var directory = GetOption(args, "--dir") ?? (args.Length > 2 ? args[2] : null);
        if (string.IsNullOrWhiteSpace(directory))
            return UsageError(stderr, "package verify requires --dir <dir>.");

        var result = PackageVerifier.Default.VerifyDirectory(directory);
        PrintDiagnostics(result.Diagnostics, stderr);
        if (!result.IsValid)
            return 1;

        stdout.WriteLine($"verified package: {directory}");
        return 0;
    }

    private static int Simulate(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || !string.Equals(args[0], "mechmaker", StringComparison.OrdinalIgnoreCase))
            return UsageError(stderr, "simulate expects the 'mechmaker' subcommand.");

        var serverPath = GetOption(args, "--mechmaker") ?? Environment.GetEnvironmentVariable("MECHMAKER_SERVER");
        if (string.IsNullOrWhiteSpace(serverPath))
            return UsageError(stderr, "simulate mechmaker requires --mechmaker <server.dll|exe> or MECHMAKER_SERVER.");

        var catalog = GetOption(args, "--catalog");
        var workingDirectory = GetOption(args, "--work-dir") ??
            Path.GetDirectoryName(Path.GetFullPath(serverPath)) ??
            Directory.GetCurrentDirectory();

        return SimulateAsync(args, stdout, stderr, serverPath, catalog, workingDirectory)
            .GetAwaiter()
            .GetResult();
    }

    private static async Task<int> SimulateAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        string serverPath,
        string? catalog,
        string workingDirectory)
    {
        var environment = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(catalog))
            environment["MECHMAKER_CATALOG"] = catalog;

        var executable = Path.GetExtension(serverPath).Equals(".dll", StringComparison.OrdinalIgnoreCase)
            ? "dotnet"
            : serverPath;
        var arguments = executable == "dotnet" ? new[] { serverPath } : [];

        await using var client = await MechMakerMcpToolClient.ConnectAsync(
            executable,
            arguments,
            workingDirectory,
            environment);
        return await MechMakerSimulateCommand.RunAsync(args, stdout, stderr, client);
    }

    private static int Build(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length < 2 || !string.Equals(args[1], "plan", StringComparison.OrdinalIgnoreCase))
            return UsageError(stderr, "build expects the 'plan' subcommand.");

        var projectPath = GetOption(args, "--project") ?? (args.Length > 2 ? args[2] : null);
        if (string.IsNullOrWhiteSpace(projectPath))
            return UsageError(stderr, "build plan requires --project <project.json>.");

        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        PrintDiagnostics(loaded.Diagnostics, stderr);
        if (loaded.Project is null)
            return 1;

        var nodes = loaded.Project.Artifacts
            .Select(artifact => new BuildNode(
                artifact.Id.ToString(),
                artifact.Key ?? artifact.Id.ToString(),
                artifact.Inputs.Select(input => input.ToString()).ToList()))
            .ToList();

        var plan = BuildPlanner.Default.Plan(nodes);
        PrintDiagnostics(plan.Diagnostics, stderr);
        if (plan.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return 1;

        stdout.WriteLine($"build plan: {plan.Order.Count} node(s)");
        foreach (var node in plan.Order)
            stdout.WriteLine($"  {node.Name}");

        return 0;
    }

    private static int Artifacts(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length < 3 || !string.Equals(args[1], "verify", StringComparison.OrdinalIgnoreCase))
            return UsageError(stderr, "artifacts expects the 'verify' subcommand.");

        var projectPath = args[2];
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        PrintDiagnostics(loaded.Diagnostics, stderr);
        if (loaded.Project is null)
            return 1;

        var storePath = GetOption(args, "--store");
        var root = string.IsNullOrWhiteSpace(storePath)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, ".emd", "artifacts")
            : storePath;
        var store = new ArtifactStore(root);

        var checkedCount = 0;
        var errors = 0;

        foreach (var artifact in loaded.Project.Artifacts)
        {
            if (artifact.Sha256 is null)
                continue;

            checkedCount++;
            if (store.Verify(artifact.Sha256))
                continue;

            errors++;
            stderr.WriteLine(
                $"EMD-ART-002 [Error] artifact '{artifact.Key ?? artifact.Id.ToString()}' " +
                $"hash {artifact.Sha256} is missing or corrupt in '{root}'.");
        }

        if (errors > 0)
            return 1;

        stdout.WriteLine($"verified {checkedCount} artifact(s)");
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

    private static void WriteTextFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, content);
    }

    private static void PrintDiagnostics(IEnumerable<Diagnostic> diagnostics, TextWriter writer)
    {
        foreach (var diagnostic in diagnostics)
            writer.WriteLine(diagnostic);
    }

    private static int UsageError(TextWriter stderr, string message)
    {
        stderr.WriteLine($"emd: {message}");
        PrintUsage(stderr);
        return 2;
    }

    private static bool IsHelp(string argument) =>
        argument is "-h" or "--help" or "help";

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("emd — ElectroMechanicalDesigner CLI");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  emd validate <project.json>");
        writer.WriteLine("  emd import mechmaker <machine.json> --out <project.json> [--catalog <dir>]");
        writer.WriteLine("  emd import kicad <netlist.xml> --out <project.json>");
        writer.WriteLine("  emd import freecad <file.FCStd|.step|.stl|...> --project <project.json> [--out <project.json>] [--store <dir>]");
        writer.WriteLine("  emd import freecad-manifest <manifest.json> --out <project.json> [--store <dir>]");
        writer.WriteLine("  emd import kicad-pcb <board.kicad_pcb> --project <project.json> [--out <project.json>]");
        writer.WriteLine("  emd export mjcf --project <project.json> --out <scene.xml>");
        writer.WriteLine("  emd export bom --project <project.json> --out <bom.csv>");
        writer.WriteLine("  emd export harness --project <project.json> --out <harness.yml>");
        writer.WriteLine("  emd export mechmaker --project <project.json> --out <machine.json>");
        writer.WriteLine("  emd kicad erc|drc|netlist|bom|gerbers|step --schematic|--pcb <file> --out <path>");
        writer.WriteLine("  emd spice --netlist <file.cir> --out <log>");
        writer.WriteLine("  emd compile mechmaker --project <project.json> --out <scene.xml> --mechmaker <cli.dll|exe> [--work-dir <dir>]");
        writer.WriteLine("  emd package --project <project.json> --out-dir <dir>");
        writer.WriteLine("  emd package verify --dir <dir>");
        writer.WriteLine("  emd trace check --project <project.json> --trace <trace.csv> [--scenario <key>]");
        writer.WriteLine("  emd simulate mechmaker --machine <machine.json>|--project <project.json> --out <trace.csv> --mechmaker <server.dll|exe> [--scenario <key>] [--duration <s>] [--motor <id:rev/s>]");
        writer.WriteLine("  emd artifacts verify <project.json> [--store <dir>]");
        writer.WriteLine("  emd build plan --project <project.json>");
        writer.WriteLine();
        writer.WriteLine("Exit codes: 0 success, 1 diagnostics/errors, 2 usage.");
    }
}
