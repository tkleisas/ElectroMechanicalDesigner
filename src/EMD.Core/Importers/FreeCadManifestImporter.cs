using System.Text.Json;
using System.Text.Json.Nodes;
using Emd.Core.Artifacts;
using Emd.Core.Diagnostics;
using Emd.Core.Identity;
using Emd.Core.Model;

namespace Emd.Core.Importers;

public sealed record FreeCadManifestImportResult(
    EmdProject? Project,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Project is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>
/// Imports the JSON sidecar produced by a CAD adapter (FreeCAD first): parts with
/// simulation proxy geometry, connectors, joints, and exported artifacts.
/// </summary>
public sealed class FreeCadManifestImporter
{
    public static FreeCadManifestImporter Default { get; } = new();

    public FreeCadManifestImportResult ImportFile(string path, ArtifactStore? store = null)
    {
        try
        {
            return Import(File.ReadAllText(path), path, store);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "EMD-IO-001",
                $"Could not read CAD manifest '{path}': {exception.Message}",
                path);
        }
    }

    public FreeCadManifestImportResult Import(
        string json,
        string? source = null,
        ArtifactStore? store = null)
    {
        CadManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CadManifest>(json, CoreJson.Options);
        }
        catch (JsonException exception)
        {
            return Failure(
                "EMD-IMPORT-002",
                $"Invalid CAD manifest JSON: {exception.Message}",
                source);
        }

        if (manifest is null || manifest.Parts is not { Count: > 0 })
        {
            return Failure(
                "EMD-IMPORT-002",
                "CAD manifest has no parts.",
                source);
        }

        var diagnostics = new List<Diagnostic>();
        var ns = EmdNamespaces.FreeCad;
        var sourceName = string.IsNullOrWhiteSpace(manifest.Source)
            ? source ?? "freecad_manifest"
            : manifest.Source!;
        var name = Path.GetFileNameWithoutExtension(sourceName);
        if (string.IsNullOrWhiteSpace(name))
            name = "freecad_manifest";

        var artifacts = new List<Artifact>();
        var artifactIdByPath = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var exports = new Dictionary<string, Guid>(StringComparer.Ordinal);
        Guid? sourceArtifactId = null;
        Guid? bomArtifactId = null;

        foreach (var artifactManifest in manifest.Artifacts ?? [])
            AddArtifact(artifactManifest, isBom: false);
        AddArtifact(manifest.Bom, isBom: true);

        var partIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var part in manifest.Parts)
        {
            if (string.IsNullOrWhiteSpace(part.Id))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-IMPORT-003",
                    DiagnosticSeverity.Error,
                    "CAD manifest part with an empty id was skipped."));
                continue;
            }

            if (!partIds.TryAdd(part.Id, DeterministicGuid.Create(ns, $"part:{part.Id}")))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-IMPORT-003",
                    DiagnosticSeverity.Error,
                    $"Duplicate CAD manifest part id '{part.Id}' was skipped.",
                    Entity: part.Id));
            }
        }

        var parts = new List<Part>();
        foreach (var partManifest in manifest.Parts)
        {
            if (!partIds.TryGetValue(partManifest.Id, out var partId))
                continue;

            var category = ParseCategory(partManifest.Category);
            var geometry = ToGeometry(
                partManifest.Geometry,
                artifactIdByPath,
                partManifest.Id,
                diagnostics);

            var ports = new List<Port>();
            foreach (var connector in partManifest.Connectors ?? [])
            {
                if (string.IsNullOrWhiteSpace(connector.Name))
                    continue;

                var connectorName = connector.Name.Trim();
                ports.Add(new Port
                {
                    Id = Slug.Key(connectorName, "port"),
                    Key = connectorName,
                    Name = connectorName,
                    Domain = PortDomain.Mechanical,
                    Kind = string.IsNullOrWhiteSpace(connector.Type) ? "mount" : connector.Type.Trim(),
                    Direction = PortDirection.Passive,
                    Pose = ToPose(connector.Pose)
                });
            }

            parts.Add(new Part
            {
                Id = partId,
                Key = Slug.Key(partManifest.Id, "part"),
                Name = string.IsNullOrWhiteSpace(partManifest.Name)
                    ? partManifest.Id
                    : partManifest.Name.Trim(),
                Kind = PartKind.Component,
                Category = category,
                Pose = ToPose(partManifest.Pose),
                MassKg = partManifest.MassKg,
                Material = ToMaterial(partManifest.Material),
                Geometry = geometry,
                DomainRefs = new PartDomainRefs
                {
                    Mechanical = new DomainRef { Tool = "freecad", Ref = partManifest.Id }
                },
                Ports = ports.OrderBy(port => port.Id, StringComparer.Ordinal).ToList(),
                Metadata = ToMetadata(partManifest)
            });
        }

        var joints = new List<MechanicalJoint>();
        foreach (var jointManifest in manifest.Joints ?? [])
        {
            if (string.IsNullOrWhiteSpace(jointManifest.Key) ||
                string.IsNullOrWhiteSpace(jointManifest.Parent) ||
                string.IsNullOrWhiteSpace(jointManifest.Child))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-004",
                    DiagnosticSeverity.Error,
                    "CAD manifest joint is missing key/parent/child."));
                continue;
            }

            if (!partIds.TryGetValue(jointManifest.Parent, out var parentId) ||
                !partIds.TryGetValue(jointManifest.Child, out var childId))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-004",
                    DiagnosticSeverity.Error,
                    $"CAD manifest joint '{jointManifest.Key}' references a missing part.",
                    Entity: jointManifest.Key));
                continue;
            }

            var type = ParseJointType(jointManifest.Type);
            if (type is null)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-004",
                    DiagnosticSeverity.Error,
                    $"CAD manifest joint '{jointManifest.Key}' has unsupported type '{jointManifest.Type}'.",
                    Entity: jointManifest.Key));
                continue;
            }

            joints.Add(new MechanicalJoint
            {
                Id = DeterministicGuid.Create(ns, $"joint:{jointManifest.Key}"),
                Key = Slug.Key(jointManifest.Key, "joint"),
                Name = jointManifest.Name,
                Type = type.Value,
                ParentPartId = parentId,
                ChildPartId = childId,
                Axis = jointManifest.Axis,
                Range = jointManifest.Range,
                Actuator = jointManifest.Actuator,
                Damping = jointManifest.Damping,
                Stiffness = jointManifest.Stiffness
            });
        }

        Guid? rootId = null;
        if (!string.IsNullOrWhiteSpace(manifest.Root) &&
            partIds.TryGetValue(manifest.Root, out var rootPartId))
        {
            rootId = rootPartId;
        }
        else if (parts.Count > 0)
        {
            rootId = parts[0].Id;
        }

        var mechanical = new MechanicalSection
        {
            AssemblyRootPartId = rootId,
            Joints = joints,
            SourceArtifactId = sourceArtifactId,
            Exports = exports.Count == 0 ? null : exports
        };

        var manufacturing = bomArtifactId is null
            ? null
            : new ManufacturingSection { BomArtifactId = bomArtifactId };

        var metadata = new JsonObject
        {
            ["source"] = "freecad",
            ["source_name"] = name
        };
        if (!string.IsNullOrWhiteSpace(source))
            metadata["source_path"] = source;
        if (!string.IsNullOrWhiteSpace(manifest.SchemaVersion))
            metadata["manifest_schema_version"] = manifest.SchemaVersion;

        var project = new EmdProject
        {
            Id = DeterministicGuid.Create(ns, $"project:{source ?? sourceName}"),
            Key = Slug.Create(name),
            Name = name,
            Description = $"Imported from CAD manifest for '{name}'.",
            License = "GPL-3.0-only",
            Units = new Units(),
            CoordinateSystem = new CoordinateSystem(),
            Artifacts = artifacts,
            Parts = parts,
            Mechanical = mechanical,
            Manufacturing = manufacturing,
            Metadata = metadata
        };

        return new FreeCadManifestImportResult(project, diagnostics);

        void AddArtifact(CadArtifact? artifactManifest, bool isBom)
        {
            if (artifactManifest is null || string.IsNullOrWhiteSpace(artifactManifest.Path))
                return;

            var path = artifactManifest.Path;
            var sha256 = artifactManifest.Sha256;
            var bytes = artifactManifest.Bytes;
            var storedPath = path;

            if (store is not null && File.Exists(path))
            {
                try
                {
                    var stored = store.PutFile(path);
                    storedPath = stored.Reference;
                    sha256 = stored.Sha256;
                    bytes = stored.Bytes;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-IO-001",
                        DiagnosticSeverity.Error,
                        $"Could not store CAD artifact '{path}': {exception.Message}",
                        Entity: path));
                }
            }

            var artifactId = DeterministicGuid.Create(ns, $"artifact:{sha256 ?? path}");
            if (artifacts.All(artifact => artifact.Id != artifactId))
            {
                var format = string.IsNullOrWhiteSpace(artifactManifest.Format)
                    ? "bin"
                    : artifactManifest.Format.Trim().ToLowerInvariant();
                var role = isBom
                    ? ArtifactRole.Manufacturing
                    : ParseRole(artifactManifest.Role);

                artifacts.Add(new Artifact
                {
                    Id = artifactId,
                    Key = Slug.Create(Path.GetFileNameWithoutExtension(path), "artifact"),
                    Path = storedPath,
                    Format = format,
                    Role = role,
                    Sha256 = sha256,
                    Bytes = bytes
                });

                if (isBom)
                    bomArtifactId = artifactId;
                else if (format == "fcstd")
                    sourceArtifactId ??= artifactId;
                else if (format is "step" or "stp" or "stl" or "gltf" or "glb" or "obj" or "iges" or "igs")
                    exports[NormalizeFormat(format)] = artifactId;
            }
            else
            {
                if (isBom)
                    bomArtifactId = artifactId;
            }

            artifactIdByPath[path] = artifactId;
        }
    }

    private static PartGeometry? ToGeometry(
        CadGeometry? geometry,
        IReadOnlyDictionary<string, Guid> artifactIdByPath,
        string partId,
        List<Diagnostic> diagnostics)
    {
        if (geometry is null)
            return null;

        var kind = ParseGeometryKind(geometry.Kind);
        if (kind is null)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-MECH-004",
                DiagnosticSeverity.Error,
                $"Part '{partId}' has unsupported geometry kind '{geometry.Kind}'.",
                Entity: partId));
            return null;
        }

        Guid? meshArtifactId = null;
        if (kind == GeometryKind.Mesh)
        {
            if (string.IsNullOrWhiteSpace(geometry.Mesh) ||
                !artifactIdByPath.TryGetValue(geometry.Mesh, out var artifactId))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-ART-001",
                    DiagnosticSeverity.Error,
                    $"Part '{partId}' mesh geometry references unknown artifact '{geometry.Mesh}'.",
                    Entity: partId));
            }
            else
            {
                meshArtifactId = artifactId;
            }
        }

        return new PartGeometry
        {
            Kind = kind.Value,
            Size = geometry.Size,
            MeshArtifactId = meshArtifactId,
            MeshScale = geometry.MeshScale,
            Rgba = geometry.Rgba,
            Collision = geometry.Collision,
            Visual = geometry.Visual
        };
    }

    private static Pose? ToPose(CadPose? pose) =>
        pose is null ? null : new Pose { Pos = pose.Pos, Rot = pose.Rot };

    private static Material? ToMaterial(CadMaterial? material) =>
        material is null
            ? null
            : new Material
            {
                Name = material.Name,
                DensityKgM3 = material.DensityKgM3,
                YoungsModulusPa = material.YoungsModulusPa,
                PoissonRatio = material.PoissonRatio,
                ThermalConductivityWMK = material.ThermalConductivityWMK
            };

    private static PartCategory ParseCategory(string? category) => category switch
    {
        "structure" => PartCategory.Structure,
        "motor" => PartCategory.Motor,
        "transmission" => PartCategory.Transmission,
        "linear_motion" => PartCategory.LinearMotion,
        "sensor" => PartCategory.Sensor,
        "electronics" => PartCategory.Electronics,
        "tool" => PartCategory.Tool,
        "fastener" => PartCategory.Fastener,
        _ => PartCategory.Other
    };

    private static GeometryKind? ParseGeometryKind(string? kind) => kind switch
    {
        "box" => GeometryKind.Box,
        "cylinder" => GeometryKind.Cylinder,
        "sphere" => GeometryKind.Sphere,
        "capsule" => GeometryKind.Capsule,
        "ellipsoid" => GeometryKind.Ellipsoid,
        "mesh" => GeometryKind.Mesh,
        _ => null
    };

    private static MechanicalJointType? ParseJointType(string? type) => type switch
    {
        "fixed" => MechanicalJointType.Fixed,
        "hinge" => MechanicalJointType.Hinge,
        "slide" => MechanicalJointType.Slide,
        "ball" => MechanicalJointType.Ball,
        "free" => MechanicalJointType.Free,
        _ => null
    };

    private static ArtifactRole ParseRole(string? role) => role switch
    {
        "source" => ArtifactRole.Source,
        "generated" => ArtifactRole.Generated,
        "visualization" => ArtifactRole.Visualization,
        "simulation" => ArtifactRole.Simulation,
        "manufacturing" => ArtifactRole.Manufacturing,
        "report" => ArtifactRole.Report,
        _ => ArtifactRole.Generated
    };

    private static JsonObject? ToMetadata(CadPart part)
    {
        var metadata = new JsonObject();
        if (!string.IsNullOrWhiteSpace(part.Description))
            metadata["description"] = part.Description;
        if (part.Tags is { Count: > 0 })
            metadata["tags"] = new JsonArray(part.Tags.Select(tag => JsonValue.Create(tag)).ToArray());
        if (part.Features is { Count: > 0 })
            metadata["features"] = JsonSerializer.SerializeToNode(part.Features, CoreJson.Options);
        if (part.Bom is not null)
            metadata["bom"] = JsonSerializer.SerializeToNode(part.Bom, CoreJson.Options);
        return metadata.Count == 0 ? null : metadata;
    }

    private static string NormalizeFormat(string format) => format switch
    {
        "stp" => "step",
        "igs" => "iges",
        _ => format
    };

    private static FreeCadManifestImportResult Failure(string code, string message, string? entity) =>
        new(null,
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message, Entity: entity)
        ]);

    private sealed record CadManifest
    {
        public string? SchemaVersion { get; init; }

        public string? Source { get; init; }

        public string? Units { get; init; }

        public string? Root { get; init; }

        public List<CadPart>? Parts { get; init; }

        public List<CadJoint>? Joints { get; init; }

        public List<CadArtifact>? Artifacts { get; init; }

        public CadArtifact? Bom { get; init; }
    }

    private sealed record CadPart
    {
        public string Id { get; init; } = "";

        public string? Name { get; init; }

        public string? Description { get; init; }

        public string? Category { get; init; }

        public CadPose? Pose { get; init; }

        public double? MassKg { get; init; }

        public CadMaterial? Material { get; init; }

        public CadGeometry? Geometry { get; init; }

        public List<CadConnector>? Connectors { get; init; }

        public List<string>? Tags { get; init; }

        public List<CadFeature>? Features { get; init; }

        public CadBom? Bom { get; init; }
    }

    private sealed record CadFeature
    {
        public string? Name { get; init; }

        public CadPose? Pose { get; init; }

        public CadGeometry? Geometry { get; init; }

        public CadMaterial? Material { get; init; }

        public List<string>? Tags { get; init; }
    }

    private sealed record CadBom
    {
        public string? Manufacturer { get; init; }

        public string? ManufacturerPartNumber { get; init; }

        public string? Supplier { get; init; }

        public string? SupplierPartNumber { get; init; }

        public double? UnitCost { get; init; }

        public string? Currency { get; init; }

        public double? Quantity { get; init; }
    }

    private sealed record CadPose
    {
        public double[]? Pos { get; init; }

        public double[]? Rot { get; init; }
    }

    private sealed record CadMaterial
    {
        public string? Name { get; init; }

        public double? DensityKgM3 { get; init; }

        public double? YoungsModulusPa { get; init; }

        public double? PoissonRatio { get; init; }

        public double? ThermalConductivityWMK { get; init; }
    }

    private sealed record CadGeometry
    {
        public string? Kind { get; init; }

        public double[]? Size { get; init; }

        public string? Mesh { get; init; }

        public double[]? MeshScale { get; init; }

        public string? Rgba { get; init; }

        public bool? Collision { get; init; }

        public bool? Visual { get; init; }
    }

    private sealed record CadConnector
    {
        public string? Name { get; init; }

        public string? Type { get; init; }

        public CadPose? Pose { get; init; }
    }

    private sealed record CadJoint
    {
        public string? Key { get; init; }

        public string? Name { get; init; }

        public string? Type { get; init; }

        public string? Parent { get; init; }

        public string? Child { get; init; }

        public double[]? Axis { get; init; }

        public double[]? Range { get; init; }

        public string? Actuator { get; init; }

        public double? Damping { get; init; }

        public double? Stiffness { get; init; }
    }

    private sealed record CadArtifact
    {
        public string? Path { get; init; }

        public string? Format { get; init; }

        public string? Role { get; init; }

        public string? Sha256 { get; init; }

        public long? Bytes { get; init; }
    }
}
