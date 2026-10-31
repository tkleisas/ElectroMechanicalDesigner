using Emd.Core.Diagnostics;
using Emd.Core.Mathematics;
using Emd.Core.Mechanical;
using Emd.Core.Model;

namespace Emd.Core.Validation;

/// <summary>Interference, clearance, and joint-limit checks over posed proxy geometry.</summary>
public sealed class MechanicalValidator
{
    public static MechanicalValidator Default { get; } = new();

    public IReadOnlyList<Diagnostic> Validate(EmdProject project)
    {
        var diagnostics = new List<Diagnostic>();
        var allowedPairs = new HashSet<(Guid, Guid)>();

        void AddPair(Guid a, Guid b)
        {
            allowedPairs.Add((a, b));
            allowedPairs.Add((b, a));
        }

        foreach (var connection in project.Connections)
        {
            if (connection.Kind is not (ConnectionKind.Mate or ConnectionKind.Mount))
                continue;
            if (connection.From.PartId is { } a && connection.To.PartId is { } b)
                AddPair(a, b);
        }

        foreach (var joint in project.Mechanical?.Joints ?? [])
            AddPair(joint.ParentPartId, joint.ChildPartId);

        var bounds = new List<(Part Part, PartBounds Bounds)>();
        foreach (var part in project.Parts)
        {
            if (PartGeometryBounds.TryCreate(part, out var partBounds))
                bounds.Add((part, partBounds));
        }

        var tolerance = InterferenceToleranceMeters(project);
        var clearance = ClearanceToleranceMeters(project);

        for (var i = 0; i < bounds.Count; i++)
        {
            for (var j = i + 1; j < bounds.Count; j++)
            {
                var a = bounds[i];
                var b = bounds[j];
                if (a.Part.Id == b.Part.Id || allowedPairs.Contains((a.Part.Id, b.Part.Id)))
                    continue;

                var penetration = CollisionMath.Penetration(a.Bounds, b.Bounds);
                if (penetration > tolerance + 1e-12)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-MECH-001",
                        DiagnosticSeverity.Error,
                        $"Parts '{Entity(a.Part)}' and '{Entity(b.Part)}' interfere by {penetration * 1000:0.###} mm.",
                        Entity: $"{Entity(a.Part)}|{Entity(b.Part)}"));
                    continue;
                }

                if (clearance <= 0)
                    continue;

                var separation = CollisionMath.Separation(a.Bounds, b.Bounds);
                if (separation > 0 && separation < clearance)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-MECH-005",
                        DiagnosticSeverity.Warning,
                        $"Parts '{Entity(a.Part)}' and '{Entity(b.Part)}' are {separation * 1000:0.###} mm apart; " +
                        $"clearance is {clearance * 1000:0.###} mm.",
                        Entity: $"{Entity(a.Part)}|{Entity(b.Part)}"));
                }
            }
        }

        var partsById = new Dictionary<Guid, Part>();
        foreach (var part in project.Parts)
            partsById.TryAdd(part.Id, part);

        foreach (var joint in project.Mechanical?.Joints ?? [])
        {
            if (joint.Range is not { Length: 2 } range)
                continue;
            if (!partsById.TryGetValue(joint.ParentPartId, out var parent) ||
                !partsById.TryGetValue(joint.ChildPartId, out var child))
                continue;

            var parentPose = parent.Pose ?? new Pose();
            var childPose = child.Pose ?? new Pose();

            if (joint.Type == MechanicalJointType.Slide)
                CheckSlide(joint, parentPose, childPose, range, diagnostics);
            else if (joint.Type == MechanicalJointType.Hinge)
                CheckHinge(joint, parentPose, childPose, range, diagnostics);
        }

        return diagnostics;
    }

    private static void CheckSlide(
        MechanicalJoint joint,
        Pose parentPose,
        Pose childPose,
        double[] range,
        List<Diagnostic> diagnostics)
    {
        if (joint.Axis is not { Length: 3 } axisValues)
            return;

        var axis = ToVec(axisValues).Normalized();
        if (axis.Length < 1e-12)
            return;

        var relative = PoseMath.Relative(parentPose, childPose);
        var position = ToVec(relative.Pos) - ToVec(joint.Anchor?.Pos);
        var coordinate = Vec3d.Dot(position, axis);

        if (coordinate < range[0] - 1e-9 || coordinate > range[1] + 1e-9)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-MECH-002",
                DiagnosticSeverity.Error,
                $"Slide joint '{Entity(joint)}' coordinate {coordinate * 1000:0.###} mm " +
                $"is outside [{range[0] * 1000:0.###}, {range[1] * 1000:0.###}] mm.",
                Entity: Entity(joint)));
        }
    }

    private static void CheckHinge(
        MechanicalJoint joint,
        Pose parentPose,
        Pose childPose,
        double[] range,
        List<Diagnostic> diagnostics)
    {
        if (joint.Axis is not { Length: 3 } axisValues)
            return;

        var axis = ToVec(axisValues).Normalized();
        if (axis.Length < 1e-12)
            return;

        var parentRotation = Mat3.FromEulerXyzDegrees(parentPose.Rot);
        var childRotation = Mat3.FromEulerXyzDegrees(childPose.Rot);
        var relative = parentRotation.Transpose() * childRotation;

        var sin = 0.5 * (
            (relative.M21 - relative.M12) * axis.X +
            (relative.M02 - relative.M20) * axis.Y +
            (relative.M10 - relative.M01) * axis.Z);
        var cos = (relative.M00 + relative.M11 + relative.M22 - 1) / 2;
        var angle = Math.Atan2(sin, cos) * 180 / Math.PI;

        if (angle < range[0] - 1e-9 || angle > range[1] + 1e-9)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-MECH-002",
                DiagnosticSeverity.Error,
                $"Hinge joint '{Entity(joint)}' angle {angle:0.###} deg " +
                $"is outside [{range[0]:0.###}, {range[1]:0.###}] deg.",
                Entity: Entity(joint)));
        }
    }

    private static double InterferenceToleranceMeters(EmdProject project)
    {
        var millimeters = 0.0;
        if (project.Validation?.Tolerances is { } validation &&
            validation.TryGetValue("interference_mm", out var fromValidation))
        {
            millimeters = fromValidation;
        }
        else if (project.Mechanical?.Tolerances is { } mechanical &&
                 mechanical.TryGetValue("interference_mm", out var fromMechanical))
        {
            millimeters = fromMechanical;
        }

        return millimeters / 1000.0;
    }

    private static double ClearanceToleranceMeters(EmdProject project)
    {
        var millimeters = 0.5;
        if (project.Validation?.Tolerances is { } validation &&
            validation.TryGetValue("clearance_mm", out var fromValidation))
        {
            millimeters = fromValidation;
        }
        else if (project.Mechanical?.Tolerances is { } mechanical &&
                 mechanical.TryGetValue("clearance_mm", out var fromMechanical))
        {
            millimeters = fromMechanical;
        }

        return millimeters / 1000.0;
    }

    private static Vec3d ToVec(double[]? values) =>
        values is { Length: 3 }
            ? new Vec3d(values[0], values[1], values[2])
            : new Vec3d(0, 0, 0);

    private static string Entity(Part part) => part.Key ?? part.Id.ToString();

    private static string Entity(MechanicalJoint joint) =>
        string.IsNullOrWhiteSpace(joint.Key) ? joint.Id.ToString() : joint.Key;
}
