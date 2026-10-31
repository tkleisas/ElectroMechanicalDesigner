using Emd.Core.Model;

namespace Emd.Core.Mathematics;

public readonly record struct Vec3d(double X, double Y, double Z)
{
    public static Vec3d operator +(Vec3d left, Vec3d right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Vec3d operator -(Vec3d left, Vec3d right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static Vec3d operator *(Mat3 matrix, Vec3d vector) => matrix.Multiply(vector);

    public double[] ToArray() => [X, Y, Z];
}

public readonly record struct Mat3(
    double M00, double M01, double M02,
    double M10, double M11, double M12,
    double M20, double M21, double M22)
{
    public static Mat3 Identity => new(
        1, 0, 0,
        0, 1, 0,
        0, 0, 1);

    public static Mat3 FromEulerXyzDegrees(double[]? euler)
    {
        var values = euler ?? [0, 0, 0];
        if (values.Length != 3)
            throw new ArgumentException("Euler angle must have three components.", nameof(euler));

        var x = double.DegreesToRadians(values[0]);
        var y = double.DegreesToRadians(values[1]);
        var z = double.DegreesToRadians(values[2]);

        var cx = Math.Cos(x);
        var sx = Math.Sin(x);
        var cy = Math.Cos(y);
        var sy = Math.Sin(y);
        var cz = Math.Cos(z);
        var sz = Math.Sin(z);

        // Extrinsic XYZ: R = Rz * Ry * Rx.
        return new Mat3(
            cz * cy, cz * sy * sx - sz * cx, cz * sy * cx + sz * sx,
            sz * cy, sz * sy * sx + cz * cx, sz * sy * cx - cz * sx,
            -sy, cy * sx, cy * cx);
    }

    public Mat3 Transpose() => new(
        M00, M10, M20,
        M01, M11, M21,
        M02, M12, M22);

    public Vec3d Multiply(Vec3d vector) => new(
        M00 * vector.X + M01 * vector.Y + M02 * vector.Z,
        M10 * vector.X + M11 * vector.Y + M12 * vector.Z,
        M20 * vector.X + M21 * vector.Y + M22 * vector.Z);

    public static Mat3 operator *(Mat3 left, Mat3 right) => new(
        left.M00 * right.M00 + left.M01 * right.M10 + left.M02 * right.M20,
        left.M00 * right.M01 + left.M01 * right.M11 + left.M02 * right.M21,
        left.M00 * right.M02 + left.M01 * right.M12 + left.M02 * right.M22,
        left.M10 * right.M00 + left.M11 * right.M10 + left.M12 * right.M20,
        left.M10 * right.M01 + left.M11 * right.M11 + left.M12 * right.M21,
        left.M10 * right.M02 + left.M11 * right.M12 + left.M12 * right.M22,
        left.M20 * right.M00 + left.M21 * right.M10 + left.M22 * right.M20,
        left.M20 * right.M01 + left.M21 * right.M11 + left.M22 * right.M21,
        left.M20 * right.M02 + left.M21 * right.M12 + left.M22 * right.M22);

    public double[] ToEulerXyzDegrees()
    {
        var clamped = Math.Clamp(-M20, -1.0, 1.0);
        var y = Math.Asin(clamped);
        double x;
        double z;

        if (Math.Abs(M20) < 0.9999999)
        {
            x = Math.Atan2(M21, M22);
            z = Math.Atan2(M10, M00);
        }
        else
        {
            // Gimbal lock: the X and Z rotations are coupled; choose Z = 0.
            x = Math.Atan2(-M12, M11);
            z = 0;
        }

        return
        [
            double.RadiansToDegrees(x),
            double.RadiansToDegrees(y),
            double.RadiansToDegrees(z)
        ];
    }
}

public static class PoseMath
{
    public static Pose Relative(Pose parent, Pose child)
    {
        var parentPosition = ToVec(parent.Pos);
        var childPosition = ToVec(child.Pos);
        var parentRotation = Mat3.FromEulerXyzDegrees(parent.Rot);
        var childRotation = Mat3.FromEulerXyzDegrees(child.Rot);
        var inverseParent = parentRotation.Transpose();

        var relativePosition = inverseParent * (childPosition - parentPosition);
        var relativeRotation = inverseParent * childRotation;

        return new Pose
        {
            Pos = relativePosition.ToArray(),
            Rot = relativeRotation.ToEulerXyzDegrees()
        };
    }

    public static Pose Compose(Pose parent, Pose relative)
    {
        var parentPosition = ToVec(parent.Pos);
        var relativePosition = ToVec(relative.Pos);
        var parentRotation = Mat3.FromEulerXyzDegrees(parent.Rot);
        var relativeRotation = Mat3.FromEulerXyzDegrees(relative.Rot);

        var position = parentPosition + parentRotation * relativePosition;
        var rotation = parentRotation * relativeRotation;

        return new Pose
        {
            Pos = position.ToArray(),
            Rot = rotation.ToEulerXyzDegrees()
        };
    }

    private static Vec3d ToVec(double[]? values)
    {
        if (values is null || values.Length == 0)
            return new Vec3d(0, 0, 0);
        if (values.Length != 3)
            throw new ArgumentException("A position must have three components.", nameof(values));

        return new Vec3d(values[0], values[1], values[2]);
    }
}
