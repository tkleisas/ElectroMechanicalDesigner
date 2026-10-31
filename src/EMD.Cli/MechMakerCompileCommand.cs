using System.Text.Json;
using Emd.Adapters;
using Emd.Core.Diagnostics;
using Emd.Core.Exporters;
using Emd.Core.Projects;

namespace Emd.Cli;

public static class MechMakerCompileCommand
{
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        MechMakerCompileAdapter adapter)
    {
        var projectPath = GetOption(args, "--project");
        var output = GetOption(args, "--out");

        if (string.IsNullOrWhiteSpace(projectPath))
            return Usage(stderr, "compile mechmaker requires --project <project.json>.");
        if (string.IsNullOrWhiteSpace(output))
            return Usage(stderr, "compile mechmaker requires --out <scene.xml>.");

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var workDirectory = GetOption(args, "--work-dir");
        if (string.IsNullOrWhiteSpace(workDirectory))
            workDirectory = Path.Combine(outputDirectory ?? ".", "emd-mechmaker");
        Directory.CreateDirectory(workDirectory);

        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        PrintDiagnostics(loaded.Diagnostics, stderr);
        if (loaded.Project is null)
            return 1;

        var exported = MechMakerMachineExporter.Default.Export(loaded.Project);
        PrintDiagnostics(exported.Diagnostics, stderr);
        if (exported.Machine is null)
            return 1;

        var machinePath = Path.Combine(workDirectory, "machine.json");
        File.WriteAllText(
            machinePath,
            exported.Machine.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var compiled = adapter.CompileAsync(machinePath, output).GetAwaiter().GetResult();
        PrintDiagnostics(compiled.Diagnostics, stderr);
        if (!compiled.Success)
            return 1;

        foreach (var artifact in compiled.Artifacts)
            stdout.WriteLine($"artifact: {artifact.Format} {artifact.Path}");

        stdout.WriteLine($"compiled: {loaded.Project.Name} -> {output}");
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

    private static int Usage(TextWriter stderr, string message)
    {
        stderr.WriteLine($"emd: {message}");
        return 2;
    }

    private static void PrintDiagnostics(IEnumerable<Diagnostic> diagnostics, TextWriter writer)
    {
        foreach (var diagnostic in diagnostics)
            writer.WriteLine(diagnostic);
    }
}
