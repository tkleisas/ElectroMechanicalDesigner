using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Emd.Core.Diagnostics;
using Emd.Core.Exporters;
using Emd.Core.Manufacturing;
using Emd.Core.Model;
using Emd.Core.Packaging;
using Emd.Core.Projects;
using Emd.Core.Simulation;
using Emd.Core.Validation;
using ModelContextProtocol.Server;

namespace Emd.Server;

[McpServerToolType]
public static class EmdTools
{
    [McpServerTool(Name = "validate_project")]
    [Description("Validate an EMD project file and return schema/semantic diagnostics.")]
    public static string ValidateProject(
        [Description("Path to emd-project JSON")] string projectPath)
    {
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        if (loaded.Project is null)
            return Diagnostics(loaded.Diagnostics);
        var semantic = ProjectValidator.Default.Validate(loaded.Project);
        var all = loaded.Diagnostics.Concat(semantic).ToList();
        return all.Count == 0
            ? $"valid: {loaded.Project.Name} ({loaded.Project.Parts.Count} parts, {loaded.Project.Connections.Count} connections)"
            : Diagnostics(all);
    }

    [McpServerTool(Name = "project_summary")]
    [Description("Return a JSON summary of a project's domains, artifacts, scenarios, and validation state.")]
    public static string ProjectSummary(
        [Description("Path to emd-project JSON")] string projectPath)
    {
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        if (loaded.Project is null)
            return Diagnostics(loaded.Diagnostics);
        return SummaryJson(loaded.Project).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool(Name = "check_trace")]
    [Description("Evaluate a recorded CSV trace against scenario assertions in a project.")]
    public static string CheckTrace(
        [Description("Path to emd-project JSON")] string projectPath,
        [Description("Path to trace CSV")] string tracePath,
        [Description("Optional scenario key")] string? scenario = null)
    {
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        if (loaded.Project is null)
            return Diagnostics(loaded.Diagnostics);
        var trace = TraceCsvLoader.Default.LoadFile(tracePath);
        if (trace.Trace is null)
            return Diagnostics(trace.Diagnostics);
        var result = TraceAssertionEvaluator.Default.Evaluate(loaded.Project, trace.Trace, scenario);
        return result.Diagnostics.Count == 0
            ? $"trace: {trace.Trace.Rows.Count} rows, scenario '{scenario ?? "all"}' passed"
            : Diagnostics(result.Diagnostics);
    }

    [McpServerTool(Name = "check_gcode")]
    [Description("Parse G-code and check its envelope and cutting feeds.")]
    public static string CheckGCode(
        [Description("Path to G-code file")] string gcodePath,
        [Description("Optional envelope: xmin,ymin,zmin,xmax,ymax,zmax")] string? envelope = null,
        [Description("Optional maximum feed in mm/min")] double? maxFeed = null)
    {
        var parsed = GCodeParser.Default.Parse(File.ReadAllText(gcodePath), gcodePath);
        if (parsed.Program is null)
            return Diagnostics(parsed.Diagnostics);
        var bounds = ParseEnvelope(envelope);
        var diagnostics = GCodeValidator.Default.Validate(
            parsed.Program, bounds?[..3], bounds?[3..], maxFeed);
        var all = parsed.Diagnostics.Concat(diagnostics).ToList();
        if (all.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return Diagnostics(all);
        return $"gcode: {parsed.Program.Moves.Count} moves, " +
               $"{parsed.Program.CutLengthMm:0.###} mm cutting, " +
               $"{parsed.Program.RapidLengthMm:0.###} mm rapid";
    }

    [McpServerTool(Name = "package_project")]
    [Description("Write a deterministic project package to a directory and verify its manifest.")]
    public static string PackageProject(
        [Description("Path to emd-project JSON")] string projectPath,
        [Description("Output directory")] string outputDirectory,
        [Description("Optional trace CSV to include")] string? tracePath = null)
    {
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        if (loaded.Project is null)
            return Diagnostics(loaded.Diagnostics);
        var extras = new List<PackageFile>();
        if (!string.IsNullOrWhiteSpace(tracePath))
        {
            if (!File.Exists(tracePath))
                return $"error: trace '{tracePath}' was not found";
            extras.Add(new PackageFile(
                $"traces/{Path.GetFileName(tracePath)}",
                "csv",
                "simulation",
                File.ReadAllBytes(tracePath),
                ""));
        }

        var package = PackageExporter.Default.Export(loaded.Project, extras);
        if (package.Files.Count == 0)
            return Diagnostics(package.Diagnostics);
        Directory.CreateDirectory(outputDirectory);
        foreach (var file in package.Files)
        {
            var path = Path.Combine(outputDirectory, file.Name);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(path, file.Content);
        }
        var verified = PackageVerifier.Default.VerifyDirectory(outputDirectory);
        return verified.IsValid
            ? $"packaged: {loaded.Project.Name} -> {outputDirectory} ({package.Files.Count} files, verified)"
            : Diagnostics(verified.Diagnostics);
    }

    [McpServerTool(Name = "export_ssp")]
    [Description("Compose an SSP archive from a project's FMU behavior models.")]
    public static string ExportSsp(
        [Description("Path to emd-project JSON")] string projectPath,
        [Description("Output .ssp path")] string outputPath,
        [Description("SSP version: 1.0 or 2.0")] string version = "2.0")
    {
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        if (loaded.Project is null)
            return Diagnostics(loaded.Diagnostics);
        var result = SspComposer.Default.Compose(loaded.Project, version);
        if (result.Archive is null)
            return Diagnostics(result.Diagnostics);
        var full = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(full, result.Archive);
        return $"exported SSP: {loaded.Project.Name} -> {full}";
    }

    [McpServerTool(Name = "verify_package")]
    [Description("Verify a package directory against its manifest.json.")]
    public static string VerifyPackage(
        [Description("Package directory")] string directory)
    {
        var result = PackageVerifier.Default.VerifyDirectory(directory);
        return result.IsValid
            ? $"verified package: {directory}"
            : Diagnostics(result.Diagnostics);
    }

    public static JsonObject SummaryJson(EmdProject project) => new()
    {
        ["name"] = project.Name,
        ["id"] = project.Id.ToString(),
        ["parts"] = project.Parts.Count,
        ["connections"] = project.Connections.Count,
        ["artifacts"] = project.Artifacts.Count,
        ["nets"] = project.Electrical?.Nets.Count ?? 0,
        ["boards"] = project.Electrical?.Boards.Count ?? 0,
        ["firmware_projects"] = project.Firmware?.Projects.Count ?? 0,
        ["behavior_models"] = project.Behavior?.Models.Count ?? 0,
        ["scenarios"] = project.Simulation?.Scenarios.Count ?? 0,
        ["manufacturing_processes"] = project.Manufacturing?.Processes.Count ?? 0,
        ["validation_errors"] = ProjectValidator.Default
            .Validate(project)
            .Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
    };

    public static JsonObject TraceSummaryJson(string tracePath)
    {
        var trace = TraceCsvLoader.Default.LoadFile(tracePath);
        if (trace.Trace is null)
            return new JsonObject { ["error"] = Diagnostics(trace.Diagnostics) };
        var last = trace.Trace.Rows[^1];
        return new JsonObject
        {
            ["rows"] = trace.Trace.Rows.Count,
            ["signals"] = new JsonArray(trace.Trace.Signals
                .Select(signal => JsonValue.Create(signal)).ToArray()),
            ["last"] = new JsonObject(last.ToDictionary(
                pair => pair.Key,
                pair => (JsonNode?)JsonValue.Create(pair.Value)))
        };
    }

    public static JsonObject PackageSummaryJson(string directory)
    {
        var result = PackageVerifier.Default.VerifyDirectory(directory);
        var manifestPath = Path.Combine(directory, "manifest.json");
        JsonNode? manifest = null;
        if (File.Exists(manifestPath))
            manifest = JsonNode.Parse(File.ReadAllText(manifestPath));
        return new JsonObject
        {
            ["valid"] = result.IsValid,
            ["manifest"] = manifest,
            ["diagnostics"] = new JsonArray(result.Diagnostics
                .Select(diagnostic => (JsonNode?)JsonValue.Create(diagnostic.ToString()))
                .ToArray())
        };
    }

    private static string Diagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        var list = diagnostics.ToList();
        return list.Count == 0
            ? "valid"
            : string.Join(Environment.NewLine, list.Select(diagnostic => diagnostic.ToString()));
    }

    private static double[]? ParseEnvelope(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length != 6)
            return null;
        var result = new double[6];
        for (var index = 0; index < 6; index++)
        {
            if (!double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out result[index]))
                return null;
        }
        return result;
    }
}
