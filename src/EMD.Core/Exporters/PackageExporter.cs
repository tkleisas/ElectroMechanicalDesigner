using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Emd.Core.Diagnostics;
using Emd.Core.Importers;
using Emd.Core.Model;
using Emd.Core.Projects;

namespace Emd.Core.Exporters;

public sealed record PackageFile(
    string Name,
    string Format,
    string Role,
    byte[] Content,
    string Sha256);

public sealed record PackageExportResult(
    IReadOnlyList<PackageFile> Files,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Files.Count > 0
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>Composes a deterministic design/manufacturing bundle from the project.</summary>
public sealed class PackageExporter
{
    public static PackageExporter Default { get; } = new();

    public PackageExportResult Export(
        EmdProject project,
        IReadOnlyList<PackageFile>? extraFiles = null)
    {
        var diagnostics = new List<Diagnostic>();
        var files = new List<PackageFile>();

        Add("project.json", "emd-project", "project", ProjectWriter.Serialize(project));

        var bom = BomExporter.Default.Export(project);
        diagnostics.AddRange(bom.Diagnostics);
        if (bom.Csv is not null)
            Add("bom.csv", "csv", "manufacturing", bom.Csv);
        else
        {
            diagnostics.Add(new Diagnostic(
                "EMD-PACKAGE-002",
                DiagnosticSeverity.Error,
                "Package omitted bom.csv because BOM export failed."));
        }

        var harness = HarnessExporter.Default.Export(project);
        diagnostics.AddRange(harness.Diagnostics);
        if (harness.Yaml is not null)
            Add("harness.yml", "wireviz", "manufacturing", harness.Yaml);

        var mjcf = MjcfExporter.Default.Export(project);
        diagnostics.AddRange(mjcf.Diagnostics);
        if (mjcf.Xml is not null)
            Add("scene.xml", "mjcf", "simulation", mjcf.Xml);
        else
        {
            diagnostics.Add(new Diagnostic(
                "EMD-PACKAGE-002",
                DiagnosticSeverity.Error,
                "Package omitted scene.xml because MJCF export failed."));
        }

        foreach (var scenario in project.Simulation?.Scenarios ?? [])
        {
            if (scenario.TraceArtifactId is not { } traceId)
                continue;

            var artifact = project.Artifacts.FirstOrDefault(candidate => candidate.Id == traceId);
            if (artifact is null)
                continue;

            if (artifact.Path.StartsWith("artifact://", StringComparison.Ordinal) ||
                !File.Exists(artifact.Path))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-PACKAGE-001",
                    DiagnosticSeverity.Warning,
                    $"Scenario '{scenario.Key}' trace artifact '{artifact.Key ?? artifact.Id.ToString()}' " +
                    "could not be read; omitted.",
                    Entity: scenario.Key));
                continue;
            }

            if (files.Any(file => file.Name == $"traces/{scenario.Key}.csv"))
                continue;

            Add(
                $"traces/{scenario.Key}.csv",
                "csv",
                "simulation",
                File.ReadAllText(artifact.Path));
        }

        foreach (var artifact in project.Artifacts
                     .Where(artifact => artifact.Role == ArtifactRole.Manufacturing)
                     .OrderBy(artifact => artifact.Key ?? artifact.Id.ToString(), StringComparer.Ordinal))
        {
            if (artifact.Path.StartsWith("artifact://", StringComparison.Ordinal) ||
                !File.Exists(artifact.Path))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-PACKAGE-001",
                    DiagnosticSeverity.Warning,
                    $"Manufacturing artifact '{artifact.Key ?? artifact.Id.ToString()}' could not be read; omitted.",
                    Entity: artifact.Key ?? artifact.Id.ToString()));
                continue;
            }

            var key = Slug.Key(artifact.Key ?? artifact.Id.ToString(), "artifact");
            var extension = Path.GetExtension(artifact.Path);
            if (string.IsNullOrWhiteSpace(extension))
                extension = "." + artifact.Format;
            var name = $"manufacturing/{key}{extension}";
            if (files.Any(file => string.Equals(file.Name, name, StringComparison.Ordinal)))
                continue;

            AddBytes(name, artifact.Format, "manufacturing", File.ReadAllBytes(artifact.Path));
        }

        var machine = MechMakerMachineExporter.Default.Export(project);
        if (machine.Machine is not null)
        {
            diagnostics.AddRange(machine.Diagnostics);
            Add("machine.json", "mechmaker-machine", "simulation", machine.Machine.ToJsonString(CoreJson.Options));
        }
        else
        {
            diagnostics.Add(new Diagnostic(
                "EMD-PACKAGE-001",
                DiagnosticSeverity.Warning,
                "machine.json omitted: the project has no MechMaker-compatible parts."));
        }

        foreach (var extra in extraFiles ?? [])
        {
            if (files.Any(file => string.Equals(file.Name, extra.Name, StringComparison.Ordinal)))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-PACKAGE-008",
                    DiagnosticSeverity.Warning,
                    $"Duplicate package member '{extra.Name}' was ignored.",
                    Entity: extra.Name));
                continue;
            }

            files.Add(extra with
            {
                Sha256 = Convert.ToHexString(SHA256.HashData(extra.Content)).ToLowerInvariant()
            });
        }

        var manifestFiles = new JsonArray();
        foreach (var file in files.OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            manifestFiles.Add(new JsonObject
            {
                ["name"] = file.Name,
                ["format"] = file.Format,
                ["role"] = file.Role,
                ["sha256"] = file.Sha256,
                ["bytes"] = file.Content.LongLength
            });
        }

        var manifest = new JsonObject
        {
            ["schema_version"] = "0.1",
            ["project_id"] = project.Id.ToString(),
            ["name"] = project.Name,
            ["files"] = manifestFiles
        };
        Add("manifest.json", "emd-package-manifest", "manifest", manifest.ToJsonString(CoreJson.Options));

        files.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return new PackageExportResult(files, diagnostics);

        void Add(string name, string format, string role, string content) =>
            AddBytes(name, format, role, Encoding.UTF8.GetBytes(content));

        void AddBytes(string name, string format, string role, byte[] bytes)
        {
            files.Add(new PackageFile(
                name,
                format,
                role,
                bytes,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        }
    }
}
