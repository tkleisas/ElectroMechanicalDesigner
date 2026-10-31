using System.Globalization;
using System.Xml.Linq;
using Emd.Core.Diagnostics;
using Emd.Core.Importers;
using Emd.Core.Mathematics;
using Emd.Core.Model;

namespace Emd.Core.Exporters;

public sealed record MjcfExportResult(
    string? Xml,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Xml is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>
/// Compiles the EMD mechanical graph into MuJoCo MJCF. The CAD document remains
/// canonical; <see cref="PartGeometry"/> is the simulation proxy.
/// </summary>
public sealed class MjcfExporter
{
    public static MjcfExporter Default { get; } = new();

    public MjcfExportResult Export(EmdProject project)
    {
        var diagnostics = new List<Diagnostic>();

        if (project.Parts.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-MJCF-001",
                DiagnosticSeverity.Error,
                "Cannot export MJCF: the project has no parts."));
            return new MjcfExportResult(null, diagnostics);
        }

        var partsById = new Dictionary<Guid, Part>();
        foreach (var part in project.Parts)
        {
            if (!partsById.TryAdd(part.Id, part))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-ID-001",
                    DiagnosticSeverity.Error,
                    $"Duplicate part id '{part.Id}' in MJCF export.",
                    Entity: part.Id.ToString()));
            }
        }

        var artifactsById = new Dictionary<Guid, Artifact>();
        foreach (var artifact in project.Artifacts)
        {
            if (!artifactsById.TryAdd(artifact.Id, artifact))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-ID-001",
                    DiagnosticSeverity.Error,
                    $"Duplicate artifact id '{artifact.Id}' in MJCF export.",
                    Entity: artifact.Id.ToString()));
            }
        }

        var mechanical = project.Mechanical;
        var rootId = mechanical?.AssemblyRootPartId;
        if (rootId is null || !partsById.ContainsKey(rootId.Value))
        {
            if (mechanical?.AssemblyRootPartId is not null)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-001",
                    DiagnosticSeverity.Error,
                    $"Mechanical assembly root '{mechanical.AssemblyRootPartId}' is not a project part."));
            }

            rootId = project.Parts[0].Id;
        }

        var jointEdges = new Dictionary<(Guid Parent, Guid Child), MechanicalJoint>();
        foreach (var joint in mechanical?.Joints ?? [])
        {
            if (!partsById.ContainsKey(joint.ParentPartId) ||
                !partsById.ContainsKey(joint.ChildPartId) ||
                joint.ParentPartId == joint.ChildPartId)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-004",
                    DiagnosticSeverity.Error,
                    $"Joint '{joint.Key}' has an invalid parent/child part.",
                    Entity: joint.Key));
                continue;
            }

            jointEdges[(joint.ParentPartId, joint.ChildPartId)] = joint;
        }

        var adjacency = new Dictionary<Guid, List<Guid>>();
        var mateEdges = new HashSet<(Guid A, Guid B)>();

        void AddNeighbor(Guid a, Guid b)
        {
            if (!adjacency.TryGetValue(a, out var neighbors))
            {
                neighbors = [];
                adjacency[a] = neighbors;
            }

            if (!neighbors.Contains(b))
                neighbors.Add(b);
        }

        foreach (var (parent, child) in jointEdges.Keys)
        {
            AddNeighbor(parent, child);
            AddNeighbor(child, parent);
        }

        foreach (var connection in project.Connections)
        {
            if (connection.Kind is not (ConnectionKind.Mate or ConnectionKind.Mount))
                continue;
            if (connection.From.PartId is not { } a || connection.To.PartId is not { } b)
                continue;
            if (!partsById.ContainsKey(a) || !partsById.ContainsKey(b) || a == b)
                continue;
            if (jointEdges.ContainsKey((a, b)) || jointEdges.ContainsKey((b, a)))
                continue;
            if (!mateEdges.Add((a, b)) || !mateEdges.Add((b, a)))
                continue;

            AddNeighbor(a, b);
            AddNeighbor(b, a);
        }

        var parentOf = new Dictionary<Guid, Guid?>();
        var incomingJoint = new Dictionary<Guid, MechanicalJoint?>();
        var children = new Dictionary<Guid, List<Guid>>();
        var visited = new HashSet<Guid>();
        var roots = new List<Guid>();
        var cycleReported = false;

        void Traverse(Guid start)
        {
            roots.Add(start);
            var queue = new Queue<Guid>();
            queue.Enqueue(start);
            visited.Add(start);
            parentOf[start] = null;
            incomingJoint[start] = null;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!adjacency.TryGetValue(current, out var neighbors))
                    continue;

                foreach (var neighbor in neighbors)
                {
                    if (visited.Add(neighbor))
                    {
                        parentOf[neighbor] = current;
                        incomingJoint[neighbor] = FindJoint(current, neighbor);
                        queue.Enqueue(neighbor);
                        continue;
                    }

                    if (parentOf.TryGetValue(current, out var currentParent) && currentParent != neighbor && !cycleReported)
                    {
                        cycleReported = true;
                        diagnostics.Add(new Diagnostic(
                            "EMD-MJCF-004",
                            DiagnosticSeverity.Warning,
                            "Mechanical graph contains a cycle or multiple paths; the first tree path was used."));
                    }
                }
            }
        }

        Traverse(rootId.Value);

        foreach (var part in project.Parts)
        {
            if (visited.Contains(part.Id))
                continue;

            Traverse(part.Id);
            diagnostics.Add(new Diagnostic(
                "EMD-MJCF-003",
                DiagnosticSeverity.Warning,
                $"Part '{part.Key ?? part.Id.ToString()}' is not connected to the assembly root; it was emitted at world level.",
                Entity: part.Key ?? part.Id.ToString()));
        }

        foreach (var part in project.Parts)
        {
            if (parentOf.TryGetValue(part.Id, out var parent) && parent is { } parentId)
            {
                if (!children.TryGetValue(parentId, out var siblings))
                {
                    siblings = [];
                    children[parentId] = siblings;
                }

                siblings.Add(part.Id);
            }
        }

        var actuators = new List<XElement>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var worldbody = new XElement("worldbody");

        foreach (var root in roots)
            worldbody.Add(BuildBody(root, new Pose(), null, isRoot: true));

        var mujoco = new XElement("mujoco",
            new XAttribute("model", project.Name));
        mujoco.Add(new XElement("compiler", new XAttribute("angle", "degree")));
        mujoco.Add(new XElement("option",
            new XAttribute("timestep", FormatNumber(project.Simulation?.Systems.FirstOrDefault()?.StepSizeS ?? 0.000125))));
        mujoco.Add(worldbody);
        if (actuators.Count > 0)
            mujoco.Add(new XElement("actuator", actuators));

        return new MjcfExportResult(new XDocument(mujoco).ToString(), diagnostics);

        MechanicalJoint? FindJoint(Guid parent, Guid child)
        {
            if (jointEdges.TryGetValue((parent, child), out var joint))
                return joint;
            return jointEdges.TryGetValue((child, parent), out var reverse) ? reverse : null;
        }

        XElement BuildBody(Guid partId, Pose parentAbsolute, MechanicalJoint? joint, bool isRoot)
        {
            var part = partsById[partId];
            var absolute = part.Pose ?? new Pose();
            var relative = isRoot ? absolute : PoseMath.Relative(parentAbsolute, absolute);

            var baseName = Slug.Key(part.Key ?? part.Name, "body");
            var name = baseName;
            var suffix = 2;
            while (!usedNames.Add(name))
                name = $"{baseName}_{suffix++}";

            var body = new XElement("body", new XAttribute("name", name));
            if (!IsZero(relative.Pos) || !IsZero(relative.Rot))
            {
                body.Add(new XAttribute("pos", FormatNumbers(relative.Pos ?? [0, 0, 0])));
                body.Add(new XAttribute("euler", FormatNumbers(relative.Rot ?? [0, 0, 0])));
            }

            AddGeometry(body, part, artifactsById, diagnostics);
            if (joint is not null)
                AddJoint(body, joint, name, diagnostics, actuators);

            if (children.TryGetValue(partId, out var childIds))
            {
                foreach (var childId in childIds)
                {
                    var childJoint = incomingJoint.GetValueOrDefault(childId);
                    body.Add(BuildBody(childId, absolute, childJoint, isRoot: false));
                }
            }

            return body;
        }
    }

    private static void AddGeometry(
        XElement body,
        Part part,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        List<Diagnostic> diagnostics)
    {
        var geometry = part.Geometry;
        if (geometry is null)
            return;

        XElement? geom = null;
        switch (geometry.Kind)
        {
            case GeometryKind.Box:
                if (TrySize(geometry.Size, part, geometry.Kind, diagnostics, out var box))
                {
                    geom = new XElement("geom",
                        new XAttribute("type", "box"),
                        new XAttribute("size", FormatNumbers([box[0] / 2, box[1] / 2, box[2] / 2])));
                }
                break;
            case GeometryKind.Cylinder:
                if (TrySize(geometry.Size, part, geometry.Kind, diagnostics, out var cylinder))
                {
                    geom = new XElement("geom",
                        new XAttribute("type", "cylinder"),
                        new XAttribute("size", FormatNumbers([cylinder[0], cylinder[1] / 2])));
                }
                break;
            case GeometryKind.Sphere:
                if (TrySize(geometry.Size, part, geometry.Kind, diagnostics, out var sphere))
                {
                    geom = new XElement("geom",
                        new XAttribute("type", "sphere"),
                        new XAttribute("size", FormatNumbers([sphere[0]])));
                }
                break;
            case GeometryKind.Capsule:
                if (TrySize(geometry.Size, part, geometry.Kind, diagnostics, out var capsule))
                {
                    geom = new XElement("geom",
                        new XAttribute("type", "capsule"),
                        new XAttribute("size", FormatNumbers([capsule[0], capsule[1] / 2])));
                }
                break;
            case GeometryKind.Ellipsoid:
                if (TrySize(geometry.Size, part, geometry.Kind, diagnostics, out var ellipsoid))
                {
                    geom = new XElement("geom",
                        new XAttribute("type", "ellipsoid"),
                        new XAttribute("size", FormatNumbers(ellipsoid)));
                }
                break;
            case GeometryKind.Mesh:
                if (geometry.MeshArtifactId is not { } artifactId ||
                    !artifacts.TryGetValue(artifactId, out var artifact))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-MJCF-002",
                        DiagnosticSeverity.Error,
                        $"Part '{part.Key ?? part.Id.ToString()}' references a missing mesh artifact.",
                        Entity: part.Key ?? part.Id.ToString()));
                    return;
                }

                if (artifact.Path.StartsWith("artifact://", StringComparison.Ordinal))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-MJCF-002",
                        DiagnosticSeverity.Error,
                        $"Mesh artifact '{artifact.Key ?? artifact.Id.ToString()}' is content-addressed; materialize it to a file before MJCF export.",
                        Entity: artifact.Key ?? artifact.Id.ToString()));
                    return;
                }

                geom = new XElement("geom",
                    new XAttribute("type", "mesh"),
                    new XAttribute("file", artifact.Path));
                if (geometry.MeshScale is { Length: 3 })
                    geom.Add(new XAttribute("scale", FormatNumbers(geometry.MeshScale)));
                break;
        }

        if (geom is null)
            return;

        if (!string.IsNullOrWhiteSpace(geometry.Rgba))
            geom.SetAttributeValue("rgba", geometry.Rgba);
        if (geometry.Collision == false)
        {
            geom.SetAttributeValue("contype", "0");
            geom.SetAttributeValue("conaffinity", "0");
        }
        if (geometry.Visual == false)
            geom.SetAttributeValue("rgba", "0 0 0 0");

        body.Add(geom);
    }

    private static void AddJoint(
        XElement body,
        MechanicalJoint joint,
        string bodyName,
        List<Diagnostic> diagnostics,
        List<XElement> actuators)
    {
        var jointName = Slug.Key(joint.Key, "joint");

        switch (joint.Type)
        {
            case MechanicalJointType.Fixed:
                return;
            case MechanicalJointType.Hinge:
            case MechanicalJointType.Slide:
                {
                    var axis = joint.Axis;
                    if (axis is not { Length: 3 } ||
                        (axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2]) < 1e-18)
                    {
                        diagnostics.Add(new Diagnostic(
                            "EMD-MECH-004",
                            DiagnosticSeverity.Error,
                            $"Joint '{joint.Key}' has no usable axis; using +Z.",
                            Entity: joint.Key));
                        axis = [0, 0, 1];
                    }

                    var element = new XElement("joint",
                        new XAttribute("name", jointName),
                        new XAttribute("type", joint.Type == MechanicalJointType.Hinge ? "hinge" : "slide"),
                        new XAttribute("axis", FormatNumbers(axis)));

                    if (joint.Range is { Length: 2 })
                        element.Add(new XAttribute("range", FormatNumbers(joint.Range)));
                    if (joint.Damping is { } damping)
                        element.Add(new XAttribute("damping", FormatNumber(damping)));
                    if (joint.Stiffness is { } stiffness)
                        element.Add(new XAttribute("stiffness", FormatNumber(stiffness)));
                    body.Add(element);

                    if (!string.IsNullOrWhiteSpace(joint.Actuator))
                    {
                        actuators.Add(new XElement("motor",
                            new XAttribute("joint", jointName),
                            new XAttribute("name", Slug.Key(joint.Actuator, "actuator"))));
                    }

                    break;
                }
            case MechanicalJointType.Ball:
                body.Add(new XElement("joint",
                    new XAttribute("name", jointName),
                    new XAttribute("type", "ball")));
                break;
            case MechanicalJointType.Free:
                body.Add(new XElement("freejoint", new XAttribute("name", jointName)));
                break;
            default:
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-004",
                    DiagnosticSeverity.Error,
                    $"Joint '{joint.Key}' has unsupported type '{joint.Type}' for body '{bodyName}'.",
                    Entity: joint.Key));
                break;
        }
    }

    private static bool TrySize(
        double[]? size,
        Part part,
        GeometryKind kind,
        List<Diagnostic> diagnostics,
        out double[] values)
    {
        if (size is { Length: 3 })
        {
            values = size;
            return true;
        }

        values = [];
        diagnostics.Add(new Diagnostic(
            "EMD-MECH-004",
            DiagnosticSeverity.Error,
            $"Part '{part.Key ?? part.Id.ToString()}' {kind} geometry needs a three-component size.",
            Entity: part.Key ?? part.Id.ToString()));
        return false;
    }

    private static bool IsZero(double[]? values) =>
        values is null || values.Length == 0 || values.All(value => Math.Abs(value) < 1e-12);

    private static string FormatNumbers(IEnumerable<double> values) =>
        string.Join(" ", values.Select(FormatNumber));

    private static string FormatNumber(double value)
    {
        if (Math.Abs(value) < 1e-15)
            return "0";

        return value.ToString("0.############", CultureInfo.InvariantCulture);
    }
}
