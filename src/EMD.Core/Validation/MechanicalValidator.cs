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

        ValidateSlideSweeps(project, allowedPairs, tolerance, diagnostics);
        ValidateHingeSweeps(project, allowedPairs, tolerance, diagnostics);

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

    private static void ValidateSlideSweeps(
        EmdProject project,
        HashSet<(Guid, Guid)> allowedPairs,
        double interferenceTolerance,
        List<Diagnostic> diagnostics)
    {
        var partsById = new Dictionary<Guid, Part>();
        foreach (var part in project.Parts)
            partsById.TryAdd(part.Id, part);

        var boundsById = new Dictionary<Guid, PartBounds>();
        foreach (var part in project.Parts)
        {
            if (PartGeometryBounds.TryCreate(part, out var bounds))
                boundsById[part.Id] = bounds;
        }

        foreach (var joint in project.Mechanical?.Joints ?? [])
        {
            if (joint.Type != MechanicalJointType.Slide ||
                joint.Range is not { Length: 2 } range ||
                joint.Axis is not { Length: 3 } axisValues)
            {
                continue;
            }

            if (!partsById.TryGetValue(joint.ParentPartId, out var parent) ||
                !partsById.TryGetValue(joint.ChildPartId, out var child) ||
                !boundsById.TryGetValue(parent.Id, out var parentBounds) ||
                !boundsById.TryGetValue(child.Id, out var childBounds))
            {
                continue;
            }

            var axisLocal = ToVec(axisValues).Normalized();
            if (axisLocal.Length < 1e-12)
                continue;

            var parentPose = parent.Pose ?? new Pose();
            var childPose = child.Pose ?? new Pose();
            var relative = PoseMath.Relative(parentPose, childPose);
            var current = Vec3d.Dot(ToVec(relative.Pos) - ToVec(joint.Anchor?.Pos), axisLocal);
            var axisWorld = Mat3.FromEulerXyzDegrees(parentPose.Rot) * axisLocal;
            var reported = new HashSet<Guid>();
            const int samples = 10;

            for (var index = 0; index <= samples; index++)
            {
                var coordinate = range[0] + (range[1] - range[0]) * index / samples;
                var moved = childBounds with
                {
                    Center = childBounds.Center + (coordinate - current) * axisWorld
                };

                foreach (var other in boundsById)
                {
                    if (other.Key == parent.Id ||
                        other.Key == child.Id ||
                        allowedPairs.Contains((child.Id, other.Key)) ||
                        !partsById.TryGetValue(other.Key, out var otherPart))
                    {
                        continue;
                    }

                    var penetration = CollisionMath.Penetration(moved, other.Value);
                    if (penetration <= interferenceTolerance + 1e-12 || !reported.Add(other.Key))
                        continue;

                    diagnostics.Add(new Diagnostic(
                        "EMD-MECH-006",
                        DiagnosticSeverity.Warning,
                        $"Slide joint '{Entity(joint)}' sweeps child '{Entity(child)}' into part " +
                        $"'{Entity(otherPart)}' at {coordinate * 1000:0.###} mm.",
                        Entity: Entity(joint)));
                }
            }

            _ = parentBounds;
        }
    }

    private static void ValidateHingeSweeps(
        EmdProject project,
        HashSet<(Guid, Guid)> allowedPairs,
        double interferenceTolerance,
        List<Diagnostic> diagnostics)
    {
        var partsById = new Dictionary<Guid, Part>();
        foreach (var part in project.Parts)
            partsById.TryAdd(part.Id, part);

        var boundsById = new Dictionary<Guid, PartBounds>();
        foreach (var part in project.Parts)
        {
            if (PartGeometryBounds.TryCreate(part, out var bounds))
                boundsById[part.Id] = bounds;
        }

        foreach (var joint in project.Mechanical?.Joints ?? [])
        {
            if (joint.Type != MechanicalJointType.Hinge ||
                joint.Range is not { Length: 2 } range ||
                joint.Axis is not { Length: 3 } axisValues)
            {
                continue;
            }

            if (!partsById.TryGetValue(joint.ParentPartId, out var parent) ||
                !partsById.TryGetValue(joint.ChildPartId, out var child) ||
                !boundsById.TryGetValue(child.Id, out var childBounds))
            {
                continue;
            }

            var axisLocal = ToVec(axisValues).Normalized();
            if (axisLocal.Length < 1e-12)
                continue;

            var parentPose = parent.Pose ?? new Pose();
            var childPose = child.Pose ?? new Pose();
            var parentRotation = Mat3.FromEulerXyzDegrees(parentPose.Rot);
            var childRotation = Mat3.FromEulerXyzDegrees(childPose.Rot);
            var currentAngle = SignedAngleAboutAxis(parentRotation.Transpose() * childRotation, axisLocal);
            var anchorWorld = ToVec(parentPose.Pos) + parentRotation * ToVec(joint.Anchor?.Pos);
            var axisWorld = parentRotation * axisLocal;
            var reported = new HashSet<Guid>();
            const int samples = 10;

            for (var index = 0; index <= samples; index++)
            {
                var angle = range[0] + (range[1] - range[0]) * index / samples;
                var rotation = RotationAboutAxis(axisWorld, (angle - currentAngle) * Math.PI / 180);
                var moved = childBounds with
                {
                    Center = anchorWorld + rotation * (childBounds.Center - anchorWorld),
                    Rotation = rotation * childBounds.Rotation
                };

                foreach (var other in boundsById)
                {
                    if (other.Key == parent.Id ||
                        other.Key == child.Id ||
                        allowedPairs.Contains((child.Id, other.Key)) ||
                        !partsById.TryGetValue(other.Key, out var otherPart))
                    {
                        continue;
                    }

                    var penetration = CollisionMath.Penetration(moved, other.Value);
                    if (penetration <= interferenceTolerance + 1e-12 || !reported.Add(other.Key))
                        continue;

                    diagnostics.Add(new Diagnostic(
                        "EMD-MECH-007",
                        DiagnosticSeverity.Warning,
                        $"Hinge joint '{Entity(joint)}' sweeps child '{Entity(child)}' into part " +
                        $"'{Entity(otherPart)}' at {angle:0.###} deg.",
                        Entity: Entity(joint)));
                }
            }
        }
    }

    private static Mat3 RotationAboutAxis(Vec3d axis, double radians)
    {
        var unit = axis.Normalized();
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        var t = 1 - c;
        var x = unit.X;
        var y = unit.Y;
        var z = unit.Z;

        return new Mat3(
            t * x * x + c, t * x * y - s * z, t * x * z + s * y,
            t * x * y + s * z, t * y * y + c, t * y * z - s * x,
            t * x * z - s * y, t * y * z + s * x, t * z * z + c);
    }

    private static double SignedAngleAboutAxis(Mat3 relative, Vec3d axis)
    {
        var sin = 0.5 * (
            (relative.M21 - relative.M12) * axis.X +
            (relative.M02 - relative.M20) * axis.Y +
            (relative.M10 - relative.M01) * axis.Z);
        var cos = (relative.M00 + relative.M11 + relative.M22 - 1) / 2;
        return Math.Atan2(sin, cos) * 180 / Math.PI;
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
        var angle = SignedAngleAboutAxis(parentRotation.Transpose() * childRotation, axis);

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
