using Emd.Core.Mathematics;

namespace Emd.Core.Mechanical;

public static class CollisionMath
{
    /// <summary>Returns the penetration depth in metres (0 when separated or just touching).</summary>
    public static double Penetration(PartBounds a, PartBounds b)
    {
        if (a.Kind == BoundsKind.Box && b.Kind == BoundsKind.Box)
            return BoxBox(a, b);
        if (a.Kind == BoundsKind.Box)
            return BoxSphere(a, b);
        if (b.Kind == BoundsKind.Box)
            return BoxSphere(b, a);
        return SphereSphere(a, b);
    }

    /// <summary>Returns a separation distance in metres (0 when overlapping or touching).</summary>
    public static double Separation(PartBounds a, PartBounds b)
    {
        if (a.Kind == BoundsKind.Box && b.Kind == BoundsKind.Box)
            return BoxBoxSeparation(a, b);
        if (a.Kind == BoundsKind.Box)
            return BoxSphereSeparation(a, b);
        if (b.Kind == BoundsKind.Box)
            return BoxSphereSeparation(b, a);
        return Math.Max(0, (b.Center - a.Center).Length - a.Radius - b.Radius);
    }

    private static double SphereSphere(PartBounds a, PartBounds b) =>
        Math.Max(0, a.Radius + b.Radius - (b.Center - a.Center).Length);

    private static double BoxSphere(PartBounds box, PartBounds sphere)
    {
        var local = box.Rotation.Transpose() * (sphere.Center - box.Center);
        var closest = ClosestPoint(box, local);
        return Math.Max(0, sphere.Radius - (local - closest).Length);
    }

    private static double BoxSphereSeparation(PartBounds box, PartBounds sphere)
    {
        var local = box.Rotation.Transpose() * (sphere.Center - box.Center);
        var closest = ClosestPoint(box, local);
        return Math.Max(0, (local - closest).Length - sphere.Radius);
    }

    private static double BoxBox(PartBounds a, PartBounds b)
    {
        var translation = b.Center - a.Center;
        var minOverlap = double.PositiveInfinity;

        foreach (var candidate in Axes(a, b))
        {
            var axis = candidate.Normalized();
            if (axis.Length < 1e-12)
                continue;

            var radiusA = ProjectRadius(a, axis);
            var radiusB = ProjectRadius(b, axis);
            var distance = Math.Abs(Vec3d.Dot(translation, axis));
            var overlap = radiusA + radiusB - distance;

            if (overlap < 0)
                return 0;

            minOverlap = Math.Min(minOverlap, overlap);
        }

        return double.IsPositiveInfinity(minOverlap) ? 0 : minOverlap;
    }

    private static double BoxBoxSeparation(PartBounds a, PartBounds b)
    {
        if (BoxBox(a, b) > 0)
            return 0;

        var translation = b.Center - a.Center;
        var maxSeparation = 0.0;

        foreach (var candidate in Axes(a, b))
        {
            var axis = candidate.Normalized();
            if (axis.Length < 1e-12)
                continue;

            var separation = Math.Abs(Vec3d.Dot(translation, axis)) -
                             ProjectRadius(a, axis) - ProjectRadius(b, axis);
            if (separation > maxSeparation)
                maxSeparation = separation;
        }

        return maxSeparation;
    }

    private static IEnumerable<Vec3d> Axes(PartBounds a, PartBounds b)
    {
        var axesA = new[] { a.Rotation.AxisX, a.Rotation.AxisY, a.Rotation.AxisZ };
        var axesB = new[] { b.Rotation.AxisX, b.Rotation.AxisY, b.Rotation.AxisZ };

        foreach (var axis in axesA)
            yield return axis;
        foreach (var axis in axesB)
            yield return axis;
        foreach (var axisA in axesA)
        {
            foreach (var axisB in axesB)
                yield return Vec3d.Cross(axisA, axisB);
        }
    }

    private static Vec3d ClosestPoint(PartBounds box, Vec3d local) => new(
        Math.Clamp(local.X, -box.HalfExtents.X, box.HalfExtents.X),
        Math.Clamp(local.Y, -box.HalfExtents.Y, box.HalfExtents.Y),
        Math.Clamp(local.Z, -box.HalfExtents.Z, box.HalfExtents.Z));

    private static double ProjectRadius(PartBounds bounds, Vec3d axis) =>
        Math.Abs(bounds.HalfExtents.X * Vec3d.Dot(bounds.Rotation.AxisX, axis)) +
        Math.Abs(bounds.HalfExtents.Y * Vec3d.Dot(bounds.Rotation.AxisY, axis)) +
        Math.Abs(bounds.HalfExtents.Z * Vec3d.Dot(bounds.Rotation.AxisZ, axis));
}
