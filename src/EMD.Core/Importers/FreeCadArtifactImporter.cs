using System.Security.Cryptography;
using Emd.Core.Artifacts;
using Emd.Core.Diagnostics;
using Emd.Core.Identity;
using Emd.Core.Model;

namespace Emd.Core.Importers;

public sealed record FreeCadImportResult(
    EmdProject? Project,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Project is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>
/// Registers a FreeCAD document or export as an EMD artifact. When a store is
/// supplied the file is copied into the content-addressed store; otherwise the
/// artifact keeps its absolute path and SHA-256.
/// </summary>
public sealed class FreeCadArtifactImporter
{
    public static FreeCadArtifactImporter Default { get; } = new();

    public FreeCadImportResult AddReference(
        EmdProject project,
        string path,
        ArtifactStore? store = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Failure("EMD-IO-001", $"FreeCAD artifact '{path}' does not exist.", path);
        }

        var spec = Map(Path.GetExtension(path));
        if (spec is null)
        {
            return Failure(
                "EMD-FREECAD-001",
                $"Unsupported FreeCAD artifact extension '{Path.GetExtension(path)}'.",
                path);
        }

        string sha256;
        string artifactPath;
        long bytes;

        try
        {
            if (store is not null)
            {
                var stored = store.PutFile(path);
                sha256 = stored.Sha256;
                artifactPath = stored.Reference;
                bytes = stored.Bytes;
            }
            else
            {
                var content = File.ReadAllBytes(path);
                sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
                artifactPath = Path.GetFullPath(path);
                bytes = content.LongLength;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "EMD-IO-001",
                $"Could not read FreeCAD artifact '{path}': {exception.Message}",
                path);
        }

        var artifactId = DeterministicGuid.Create(EmdNamespaces.FreeCad, $"artifact:{sha256}");
        if (project.Artifacts.Any(artifact => artifact.Id == artifactId))
            return new FreeCadImportResult(project, []);

        var artifact = new Artifact
        {
            Id = artifactId,
            Key = Slug.Create(Path.GetFileNameWithoutExtension(path), "artifact"),
            Path = artifactPath,
            Format = spec.Value.Format,
            Role = spec.Value.Role,
            Sha256 = sha256,
            Bytes = bytes
        };

        var artifacts = new List<Artifact>(project.Artifacts) { artifact };
        MechanicalSection? mechanical = project.Mechanical;

        if (spec.Value.IsMechanicalSource)
        {
            mechanical = (mechanical ?? new MechanicalSection()) with
            {
                SourceArtifactId = artifactId
            };
        }
        else if (spec.Value.IsMechanicalExport)
        {
            var updated = mechanical ?? new MechanicalSection();
            var exports = new Dictionary<string, Guid>(updated.Exports ?? []);
            exports[spec.Value.Format] = artifactId;
            mechanical = updated with { Exports = exports };
        }

        return new FreeCadImportResult(project with
        {
            Artifacts = artifacts,
            Mechanical = mechanical
        }, []);
    }

    private static ArtifactSpec? Map(string extension) => extension.ToLowerInvariant() switch
    {
        ".fcstd" => new ArtifactSpec("fcstd", ArtifactRole.Source, IsMechanicalSource: true, IsMechanicalExport: false),
        ".step" or ".stp" => new ArtifactSpec("step", ArtifactRole.Generated, false, true),
        ".iges" or ".igs" => new ArtifactSpec("iges", ArtifactRole.Generated, false, true),
        ".stl" => new ArtifactSpec("stl", ArtifactRole.Generated, false, true),
        ".obj" => new ArtifactSpec("obj", ArtifactRole.Generated, false, true),
        ".gltf" => new ArtifactSpec("gltf", ArtifactRole.Generated, false, true),
        ".glb" => new ArtifactSpec("glb", ArtifactRole.Generated, false, true),
        _ => null
    };

    private static FreeCadImportResult Failure(string code, string message, string entity) =>
        new(null,
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message, Entity: entity)
        ]);

    private readonly record struct ArtifactSpec(
        string Format,
        ArtifactRole Role,
        bool IsMechanicalSource,
        bool IsMechanicalExport);
}
