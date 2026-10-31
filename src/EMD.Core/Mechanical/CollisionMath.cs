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

    private static double SphereSphere(PartBounds a, PartBounds b) =>
        Math.Max(0, a.Radius + b.Radius - (b.Center - a.Center).Length);

    private static double BoxSphere(PartBounds box, PartBounds sphere)
    {
        var local = box.Rotation.Transpose() * (sphere.Center - box.Center);
        var closest = new Vec3d(
            Math.Clamp(local.X, -box.HalfExtents.X, box.HalfExtents.X),
            Math.Clamp(local.Y, -box.HalfExtents.Y, box.HalfExtents.Y),
            Math.Clamp(local.Z, -box.HalfExtents.Z, box.HalfExtents.Z));

        return Math.Max(0, sphere.Radius - (local - closest).Length);
    }

    private static double BoxBox(PartBounds a, PartBounds b)
    {
        var axesA = new[] { a.Rotation.AxisX, a.Rotation.AxisY, a.Rotation.AxisZ };
        var axesB = new[] { b.Rotation.AxisX, b.Rotation.AxisY, b.Rotation.AxisZ };
        var translation = b.Center - a.Center;

        var axes = new List<Vec3d>(15);
        axes.AddRange(axesA);
        axes.AddRange(axesB);
        foreach (var axisA in axesA)
        {
            foreach (var axisB in axesB)
                axes.Add(Vec3d.Cross(axisA, axisB));
        }

        var minOverlap = double.PositiveInfinity;

        foreach (var candidate in axes)
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

    private static double ProjectRadius(PartBounds bounds, Vec3d axis) =>
        Math.Abs(bounds.HalfExtents.X * Vec3d.Dot(bounds.Rotation.AxisX, axis)) +
        Math.Abs(bounds.HalfExtents.Y * Vec3d.Dot(bounds.Rotation.AxisY, axis)) +
        Math.Abs(bounds.HalfExtents.Z * Vec3d.Dot(bounds.Rotation.AxisZ, axis));
}
