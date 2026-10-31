using Emd.Core.Mathematics;
using Emd.Core.Model;

namespace Emd.Core.Tests;

public class PoseMathTests
{
    [Fact]
    public void Relative_of_identity_is_identity()
    {
        var relative = PoseMath.Relative(new Pose(), new Pose());

        AssertClose([0, 0, 0], relative.Pos);
        AssertClose([0, 0, 0], relative.Rot);
    }

    [Fact]
    public void Relative_translation_is_parent_relative()
    {
        var relative = PoseMath.Relative(
            new Pose { Pos = [1, 0, 0] },
            new Pose { Pos = [2, 0, 0] });

        AssertClose([1, 0, 0], relative.Pos);
        AssertClose([0, 0, 0], relative.Rot);
    }

    [Fact]
    public void Relative_position_rotates_into_the_parent_frame()
    {
        var relative = PoseMath.Relative(
            new Pose { Rot = [0, 0, 90] },
            new Pose { Pos = [1, 0, 0] });

        AssertClose([0, -1, 0], relative.Pos);
        AssertClose([0, 0, -90], relative.Rot);
    }

    [Fact]
    public void Relative_rotation_is_the_child_minus_the_parent()
    {
        var relative = PoseMath.Relative(
            new Pose { Rot = [0, 0, 30] },
            new Pose { Rot = [0, 0, 90] });

        AssertClose([0, 0, 60], relative.Rot);
    }

    [Fact]
    public void Euler_round_trips_through_the_matrix()
    {
        var relative = PoseMath.Relative(
            new Pose(),
            new Pose { Rot = [10, 20, 30] });

        AssertClose([10, 20, 30], relative.Rot);
    }

    [Fact]
    public void Absolute_pose_can_be_recomposed_from_root_and_relative()
    {
        var root = new Pose { Pos = [1, 2, 3], Rot = [0, 0, 90] };
        var relative = PoseMath.Relative(root, new Pose { Pos = [1, 0, 0], Rot = [0, 0, 30] });

        var absolute = PoseMath.Compose(root, relative);

        AssertClose([1, 0, 0], absolute.Pos);
        AssertClose([0, 0, 30], absolute.Rot);
    }

    private static void AssertClose(double[] expected, double[]? actual, double tolerance = 1e-9)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Length, actual!.Length);
        for (var index = 0; index < expected.Length; index++)
            Assert.InRange(actual[index], expected[index] - tolerance, expected[index] + tolerance);
    }
}
