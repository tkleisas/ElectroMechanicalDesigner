using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Emd.Core.Diagnostics;

namespace Emd.Core.Exporters;

public sealed record KiCadFabricationInputs(
    string Name,
    IReadOnlyList<string> GerberFiles,
    string? BomPath = null,
    string? PositionPath = null,
    string? StepPath = null);

public sealed record KiCadFabricationResult(
    byte[]? Archive,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Archive is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>Deterministic Gerber/drill/BOM/position/STEP fabrication archive.</summary>
public sealed class KiCadFabricationPackageExporter
{
    private static readonly DateTimeOffset FixedTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static KiCadFabricationPackageExporter Default { get; } = new();

    public KiCadFabricationResult Export(KiCadFabricationInputs inputs)
    {
        var diagnostics = new List<Diagnostic>();
        var files = new List<(string Name, string Format, byte[] Content)>();
        foreach (var path in inputs.GerberFiles.OrderBy(path => path, StringComparer.Ordinal))
        {
            Add(path, $"gerbers/{Path.GetFileName(path)}", "gerber");
        }

        Add(inputs.BomPath, "bom.csv", "bom");
        Add(inputs.PositionPath, "positions.csv", "pos");
        Add(inputs.StepPath, "board.step", "step");

        if (files.All(file => !file.Name.StartsWith("gerbers/", StringComparison.Ordinal)))
        {
            diagnostics.Add(new Diagnostic(
                "EMD-CAM-005",
                DiagnosticSeverity.Error,
                "A fabrication package needs at least one Gerber/drill file."));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return new KiCadFabricationResult(null, diagnostics);

        var manifestFiles = new JsonArray();
        foreach (var file in files.OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            manifestFiles.Add(new JsonObject
            {
                ["name"] = file.Name,
                ["format"] = file.Format,
                ["bytes"] = file.Content.LongLength,
                ["sha256"] = Hash(file.Content)
            });
        }

        var manifest = new JsonObject
        {
            ["schema_version"] = "0.1",
            ["kind"] = "kicad-fabrication",
            ["name"] = inputs.Name,
            ["files"] = manifestFiles
        };

        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files.OrderBy(file => file.Name, StringComparer.Ordinal))
                AddEntry(archive, file.Name, file.Content);
            AddEntry(archive, "manifest.json",
                Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        }

        return new KiCadFabricationResult(memory.ToArray(), diagnostics);

        void Add(string? path, string name, string format)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            if (!File.Exists(path))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-CAM-005",
                    DiagnosticSeverity.Error,
                    $"Fabrication input '{path}' is missing.",
                    Entity: name));
                return;
            }

            files.Add((name, format, File.ReadAllBytes(path)));
        }
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTimestamp;
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
