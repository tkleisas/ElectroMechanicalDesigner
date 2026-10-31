using Emd.Core.Mathematics;
using Emd.Core.Model;

namespace Emd.Core.Mechanical;

public enum BoundsKind
{
    Box,
    Sphere
}

public readonly record struct PartBounds(
    Vec3d Center,
    Mat3 Rotation,
    Vec3d HalfExtents,
    double Radius,
    BoundsKind Kind);

/// <summary>Builds conservative collision bounds from the simulation-proxy geometry.</summary>
public static class PartGeometryBounds
{
    public static bool TryCreate(Part part, out PartBounds bounds)
    {
        bounds = default;
        var geometry = part.Geometry;
        if (geometry is null || geometry.Kind == GeometryKind.Mesh)
            return false;
        if (geometry.Size is not { Length: 3 } size)
            return false;

        var pose = part.Pose ?? new Pose();
        var center = ToVec(pose.Pos);
        var rotation = Mat3.FromEulerXyzDegrees(pose.Rot);

        switch (geometry.Kind)
        {
            case GeometryKind.Box:
                {
                    var half = new Vec3d(size[0] / 2, size[1] / 2, size[2] / 2);
                    bounds = new PartBounds(center, rotation, half, half.Length, BoundsKind.Box);
                    return true;
                }
            case GeometryKind.Cylinder:
                {
                    var radius = size[0];
                    var halfLength = size[1] / 2;
                    var sphere = Math.Sqrt(radius * radius + halfLength * halfLength);
                    bounds = new PartBounds(center, rotation,
                        new Vec3d(radius, radius, halfLength), sphere, BoundsKind.Sphere);
                    return true;
                }
            case GeometryKind.Sphere:
                {
                    var radius = size[0];
                    bounds = new PartBounds(center, rotation,
                        new Vec3d(radius, radius, radius), radius, BoundsKind.Sphere);
                    return true;
                }
            case GeometryKind.Capsule:
                {
                    var radius = size[0] + size[1] / 2;
                    bounds = new PartBounds(center, rotation,
                        new Vec3d(size[0], size[0], size[1] / 2), radius, BoundsKind.Sphere);
                    return true;
                }
            case GeometryKind.Ellipsoid:
                {
                    var radius = Math.Max(size[0], Math.Max(size[1], size[2]));
                    bounds = new PartBounds(center, rotation,
                        new Vec3d(size[0], size[1], size[2]), radius, BoundsKind.Sphere);
                    return true;
                }
            default:
                return false;
        }
    }

    private static Vec3d ToVec(double[]? values) =>
        values is { Length: 3 }
            ? new Vec3d(values[0], values[1], values[2])
            : new Vec3d(0, 0, 0);
}
