using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Emd.Core.Artifacts;
using Emd.Core.Diagnostics;
using Emd.Core.Exporters;
using Emd.Core.Importers;
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

    [McpServerTool(Name = "import_mechmaker")]
    [Description("Import a MechMaker machine JSON into an EMD project.")]
    public static string ImportMechMaker(
        [Description("Path to machine.json")] string machinePath,
        [Description("Output project JSON")] string outputPath,
        [Description("Optional MechMaker catalog directory")] string? catalogPath = null)
    {
        MechMakerCatalog? catalog = null;
        if (!string.IsNullOrWhiteSpace(catalogPath))
        {
            try { catalog = MechMakerCatalog.Load(catalogPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return $"error: could not read catalog '{catalogPath}': {exception.Message}";
            }
        }
        var result = MechMakerImporter.Default.ImportFile(machinePath, catalog);
        if (result.Project is null)
            return Diagnostics(result.Diagnostics);
        ProjectWriter.Save(result.Project, outputPath);
        return $"imported: {result.Project.Name} -> {outputPath} " +
               $"({result.Project.Parts.Count} parts, {result.Project.Connections.Count} connections)";
    }

    [McpServerTool(Name = "import_kicad")]
    [Description("Import a KiCad XML netlist into an EMD project.")]
    public static string ImportKiCad(
        [Description("Path to KiCad XML netlist")] string netlistPath,
        [Description("Output project JSON")] string outputPath)
    {
        var result = KiCadNetlistImporter.Default.ImportFile(netlistPath);
        if (result.Project is null)
            return Diagnostics(result.Diagnostics);
        ProjectWriter.Save(result.Project, outputPath);
        return $"imported: {result.Project.Name} -> {outputPath} " +
               $"({result.Project.Parts.Count} parts, {result.Project.Electrical?.Nets.Count ?? 0} nets)";
    }

    [McpServerTool(Name = "import_freecad_manifest")]
    [Description("Import a FreeCAD manifest into an EMD project.")]
    public static string ImportFreeCadManifest(
        [Description("Path to CAD manifest JSON")] string manifestPath,
        [Description("Output project JSON")] string outputPath,
        [Description("Optional artifact store directory")] string? storePath = null)
    {
        ArtifactStore? store = string.IsNullOrWhiteSpace(storePath) ? null : new ArtifactStore(storePath);
        var result = FreeCadManifestImporter.Default.ImportFile(manifestPath, store);
        if (result.Project is null)
            return Diagnostics(result.Diagnostics);
        ProjectWriter.Save(result.Project, outputPath);
        return $"imported: {result.Project.Name} -> {outputPath} " +
               $"({result.Project.Parts.Count} parts, {result.Project.Artifacts.Count} artifacts)";
    }

    [McpServerTool(Name = "list_scenarios")]
    [Description("List scenario keys, durations, records, and assertions in a project.")]
    public static string ListScenarios([Description("Path to emd-project JSON")] string projectPath)
    {
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        if (loaded.Project is null)
            return Diagnostics(loaded.Diagnostics);
        var scenarios = new JsonArray();
        foreach (var scenario in loaded.Project.Simulation?.Scenarios ?? [])
        {
            scenarios.Add(new JsonObject
            {
                ["key"] = scenario.Key,
                ["name"] = scenario.Name,
                ["duration_s"] = scenario.DurationS,
                ["record"] = new JsonArray((scenario.Record ?? [])
                    .Select(signal => JsonValue.Create(signal)).ToArray()),
                ["assertions"] = new JsonArray((scenario.Assertions ?? [])
                    .Select(assertion => (JsonNode?)new JsonObject
                    {
                        ["name"] = assertion.Name,
                        ["expression"] = assertion.Expression
                    }).ToArray())
            });
        }
        return new JsonObject { ["scenarios"] = scenarios }.ToJsonString();
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

    [McpServerTool(Name = "export_mjcf")]
    [Description("Export a project to MJCF.")]
    public static string ExportMjcf(string projectPath, string outputPath)
    {
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        if (loaded.Project is null)
            return Diagnostics(loaded.Diagnostics);
        var result = MjcfExporter.Default.Export(loaded.Project);
        if (result.Xml is null)
            return Diagnostics(result.Diagnostics);
        File.WriteAllText(outputPath, result.Xml);
        return $"exported MJCF: {loaded.Project.Name} -> {outputPath}";
    }

    [McpServerTool(Name = "export_bom")]
    [Description("Export a project BOM as CSV.")]
    public static string ExportBom(string projectPath, string outputPath)
    {
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        if (loaded.Project is null)
            return Diagnostics(loaded.Diagnostics);
        var result = BomExporter.Default.Export(loaded.Project);
        if (result.Csv is null)
            return Diagnostics(result.Diagnostics);
        File.WriteAllText(outputPath, result.Csv);
        return $"exported BOM: {loaded.Project.Name} -> {outputPath}";
    }

    [McpServerTool(Name = "export_mechmaker")]
    [Description("Export a project to a MechMaker machine JSON.")]
    public static string ExportMechMaker(string projectPath, string outputPath)
    {
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        if (loaded.Project is null)
            return Diagnostics(loaded.Diagnostics);
        var result = MechMakerMachineExporter.Default.Export(loaded.Project);
        if (result.Machine is null)
            return Diagnostics(result.Diagnostics);
        File.WriteAllText(outputPath, result.Machine.ToJsonString(Emd.Core.CoreJson.Options));
        return $"exported MechMaker machine: {loaded.Project.Name} -> {outputPath}";
    }

    [McpServerTool(Name = "export_assembly_doc")]
    [Description("Export assembly Markdown with BOM, harness, boards, firmware, and manufacturing.")]
    public static string ExportAssemblyDoc(string projectPath, string outputPath)
    {
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        if (loaded.Project is null)
            return Diagnostics(loaded.Diagnostics);
        var result = AssemblyDocExporter.Default.Export(loaded.Project);
        if (result.Markdown is null)
            return Diagnostics(result.Diagnostics);
        File.WriteAllText(outputPath, result.Markdown);
        return $"exported assembly doc: {loaded.Project.Name} -> {outputPath}";
    }

    [McpServerTool(Name = "verify_artifacts")]
    [Description("Verify project artifacts against the content-addressed store.")]
    public static string VerifyArtifacts(
        [Description("Path to emd-project JSON")] string projectPath,
        [Description("Optional artifact store directory")] string? storePath = null)
    {
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        if (loaded.Project is null)
            return Diagnostics(loaded.Diagnostics);
        var root = string.IsNullOrWhiteSpace(storePath)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, ".emd", "artifacts")
            : storePath;
        var store = new ArtifactStore(root);
        var checkedCount = 0;
        var failures = new List<string>();
        foreach (var artifact in loaded.Project.Artifacts)
        {
            if (artifact.Sha256 is null)
                continue;
            checkedCount++;
            if (!store.Verify(artifact.Sha256))
                failures.Add($"{artifact.Key ?? artifact.Id.ToString()}: {artifact.Sha256}");
        }
        return failures.Count == 0
            ? $"verified {checkedCount} artifact(s)"
            : $"missing/corrupt artifacts ({root}):{Environment.NewLine}" +
              string.Join(Environment.NewLine, failures);
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

    [McpServerTool(Name = "simulate_cam")]
    [Description("Run the 2.5D heightmap material-removal check against an STL target.")]
    public static string SimulateCam(
        [Description("Path to G-code")] string gcodePath,
        [Description("Path to target STL")] string targetPath,
        [Description("Stock bounds: xmin,ymin,zmin,xmax,ymax,zmax")] string stock,
        [Description("Tool diameter in mm")] double toolDiameter,
        [Description("Heightmap cell size in mm")] double cell,
        [Description("Output JSON report path")] string outputPath)
    {
        var bounds = ParseEnvelope(stock);
        if (bounds is null)
            return "error: stock must be xmin,ymin,zmin,xmax,ymax,zmax";
        var parsed = GCodeParser.Default.Parse(File.ReadAllText(gcodePath), gcodePath);
        if (parsed.Program is null)
            return Diagnostics(parsed.Diagnostics);
        var mesh = StlMeshLoader.Default.LoadFile(targetPath);
        if (mesh.Triangles is null)
            return Diagnostics(mesh.Diagnostics);
        var min = bounds[..3];
        var max = bounds[3..];
        var target = Heightmap.FromStl(mesh.Triangles, min, max, cell);
        var stockMap = Heightmap.Stock(min, max, cell);
        stockMap.Simulate(parsed.Program, toolDiameter);
        var report = stockMap.Compare(target);
        File.WriteAllText(outputPath, target.ToJson(report).ToJsonString());
        return $"gouge {report.GougeCells} cell(s), max {report.MaxGougeMm:0.###} mm; " +
               $"remaining {report.RemainingCells} cell(s), max {report.MaxRemainingMm:0.###} mm";
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

    public static JsonObject GraphJson(EmdProject project)
    {
        var parts = new JsonArray();
        foreach (var part in project.Parts)
        {
            parts.Add(new JsonObject
            {
                ["id"] = part.Id.ToString(),
                ["key"] = part.Key,
                ["name"] = part.Name,
                ["kind"] = part.Kind.ToString(),
                ["category"] = part.Category?.ToString()
            });
        }

        var edges = new JsonArray();
        foreach (var connection in project.Connections)
        {
            edges.Add(new JsonObject
            {
                ["key"] = connection.Key,
                ["kind"] = connection.Kind.ToString(),
                ["from"] = EndpointId(connection.From),
                ["to"] = EndpointId(connection.To)
            });
        }
        return new JsonObject { ["nodes"] = parts, ["edges"] = edges };
    }

    public static JsonObject SceneJson(EmdProject project)
    {
        var parts = new JsonArray();
        foreach (var part in project.Parts)
        {
            var position = part.Pose?.Pos ?? [0d, 0d, 0d];
            var size = part.Geometry?.Size ?? [0.02d, 0.02d, 0.02d];
            parts.Add(new JsonObject
            {
                ["key"] = part.Key,
                ["name"] = part.Name,
                ["pos"] = new JsonArray(position.Select(value => JsonValue.Create(value)).ToArray()),
                ["size"] = new JsonArray(size.Select(value => JsonValue.Create(value)).ToArray())
            });
        }
        return new JsonObject { ["parts"] = parts };
    }

    public static JsonArray ValidationJson(EmdProject project) =>
        new(ProjectValidator.Default.Validate(project)
            .Select(diagnostic => (JsonNode?)new JsonObject
            {
                ["code"] = diagnostic.Code,
                ["severity"] = diagnostic.Severity.ToString(),
                ["message"] = diagnostic.Message,
                ["entity"] = diagnostic.Entity
            }).ToArray());

    public static JsonObject TraceChartJson(string tracePath)
    {
        var trace = TraceCsvLoader.Default.LoadFile(tracePath);
        if (trace.Trace is null)
            return new JsonObject { ["error"] = Diagnostics(trace.Diagnostics) };
        var rows = new JsonArray();
        foreach (var row in trace.Trace.Rows.Take(5000))
        {
            var item = new JsonObject();
            foreach (var signal in trace.Trace.Signals)
                item[signal] = row.TryGetValue(signal, out var value) ? value : null;
            rows.Add(item);
        }
        return new JsonObject
        {
            ["signals"] = new JsonArray(trace.Trace.Signals
                .Select(signal => JsonValue.Create(signal)).ToArray()),
            ["rows"] = rows
        };
    }

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

    private static string? EndpointId(Endpoint endpoint) =>
        endpoint.PartId?.ToString() ?? endpoint.NetId?.ToString() ??
        endpoint.ArtifactId?.ToString() ?? endpoint.Uri;

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
