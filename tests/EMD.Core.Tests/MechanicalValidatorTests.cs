using Emd.Core.Model;
using Emd.Core.Validation;

namespace Emd.Core.Tests;

public class MechanicalValidatorTests
{
    [Fact]
    public void Overlapping_boxes_report_EMD_MECH_001()
    {
        var frame = Box("frame", [0, 0, 0], [1, 1, 1]);
        var motor = Box("motor", [0, 0, 0], [0.5, 0.5, 0.5]);

        var diagnostics = MechanicalValidator.Default.Validate(Project(frame, motor));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-001");
    }

    [Fact]
    public void Separated_boxes_do_not_report()
    {
        var frame = Box("frame", [0, 0, 0], [1, 1, 1]);
        var motor = Box("motor", [2, 0, 0], [1, 1, 1]);

        var diagnostics = MechanicalValidator.Default.Validate(Project(frame, motor));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-001");
    }

    [Fact]
    public void Touching_boxes_do_not_report()
    {
        var frame = Box("frame", [0, 0, 0], [1, 1, 1]);
        var motor = Box("motor", [1, 0, 0], [1, 1, 1]);

        var diagnostics = MechanicalValidator.Default.Validate(Project(frame, motor));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-001");
    }

    [Fact]
    public void Rotated_boxes_are_checked_with_oriented_bounds()
    {
        var frame = Box("frame", [0, 0, 0], [2, 0.2, 0.2]);
        var motor = Box("motor", [0, 0.2, 0], [0.2, 2, 0.2], rot: [0, 0, 45]);

        var diagnostics = MechanicalValidator.Default.Validate(Project(frame, motor));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-001");
    }

    [Fact]
    public void Explicitly_connected_parts_may_overlap()
    {
        var frame = Box("frame", [0, 0, 0], [1, 1, 1]);
        var motor = Box("motor", [0, 0, 0], [1, 1, 1]);
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Key = "motor_on_frame",
            Kind = ConnectionKind.Mate,
            From = new Endpoint { PartId = frame.Id },
            To = new Endpoint { PartId = motor.Id }
        };

        var diagnostics = MechanicalValidator.Default.Validate(Project(frame, motor, connections: [connection]));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-001");
    }

    [Fact]
    public void Interference_tolerance_is_honoured()
    {
        var frame = Box("frame", [0, 0, 0], [1, 1, 1]);
        var motor = Box("motor", [0.999, 0, 0], [1, 1, 1]);

        var allowed = MechanicalValidator.Default.Validate(Project(frame, motor, interferenceMm: 2));
        var rejected = MechanicalValidator.Default.Validate(Project(frame, motor, interferenceMm: 0));

        Assert.DoesNotContain(allowed, diagnostic => diagnostic.Code == "EMD-MECH-001");
        Assert.Contains(rejected, diagnostic => diagnostic.Code == "EMD-MECH-001");
    }

    [Fact]
    public void Close_unconnected_parts_report_EMD_MECH_005()
    {
        var first = Sphere("first", [0, 0, 0], 0.1);
        var second = Sphere("second", [0.25, 0, 0], 0.1);

        var diagnostics = MechanicalValidator.Default.Validate(
            Project(first, second, clearanceMm: 100));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-005");
    }

    [Fact]
    public void Clearance_tolerance_suppresses_the_warning()
    {
        var first = Sphere("first", [0, 0, 0], 0.1);
        var second = Sphere("second", [0.25, 0, 0], 0.1);

        var diagnostics = MechanicalValidator.Default.Validate(
            Project(first, second, clearanceMm: 10));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-005");
    }

    [Fact]
    public void Connected_parts_do_not_report_clearance()
    {
        var first = Sphere("first", [0, 0, 0], 0.1);
        var second = Sphere("second", [0.25, 0, 0], 0.1);
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Kind = ConnectionKind.Mate,
            From = new Endpoint { PartId = first.Id },
            To = new Endpoint { PartId = second.Id }
        };

        var diagnostics = MechanicalValidator.Default.Validate(
            Project(first, second, connections: [connection], clearanceMm: 100));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-005");
    }

    [Fact]
    public void Overlapping_parts_report_interference_not_clearance()
    {
        var first = Sphere("first", [0, 0, 0], 0.1);
        var second = Sphere("second", [0.05, 0, 0], 0.1);

        var diagnostics = MechanicalValidator.Default.Validate(
            Project(first, second, clearanceMm: 100));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-001");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-005");
    }

    [Fact]
    public void Slide_sweep_detects_an_obstacle_in_the_path()
    {
        var project = SlideSweepProject(obstaclePosition: [0.4, 0, 0]);

        var diagnostics = MechanicalValidator.Default.Validate(project);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-006");
    }

    [Fact]
    public void Slide_sweep_ignores_an_obstacle_outside_the_path()
    {
        var project = SlideSweepProject(obstaclePosition: [0.4, 0.3, 0]);

        var diagnostics = MechanicalValidator.Default.Validate(project);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-006");
    }

    [Fact]
    public void Slide_sweep_ignores_parts_connected_to_the_child()
    {
        var parent = Box("parent", [0, 0, 0], [0.1, 0.1, 0.1]);
        var child = Box("child", [0, 0, 0], [0.1, 0.1, 0.1]);
        var obstacle = Box("obstacle", [0.4, 0, 0], [0.1, 0.1, 0.1]);
        var joint = new MechanicalJoint
        {
            Id = Guid.NewGuid(),
            Key = "x_slide",
            Type = MechanicalJointType.Slide,
            ParentPartId = parent.Id,
            ChildPartId = child.Id,
            Axis = [1, 0, 0],
            Range = [0, 0.5]
        };
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Kind = ConnectionKind.Mate,
            From = new Endpoint { PartId = child.Id },
            To = new Endpoint { PartId = obstacle.Id }
        };
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "sweep connected",
            Parts = [parent, child, obstacle],
            Connections = [connection],
            Mechanical = new MechanicalSection
            {
                AssemblyRootPartId = parent.Id,
                Joints = [joint]
            }
        };

        var diagnostics = MechanicalValidator.Default.Validate(project);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-006");
    }

    [Fact]
    public void Hinge_sweep_detects_an_obstacle_in_the_path()
    {
        var project = HingeSweepProject(obstaclePosition: [0, 0.3, 0]);

        var diagnostics = MechanicalValidator.Default.Validate(project);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-007");
    }

    [Fact]
    public void Hinge_sweep_ignores_an_obstacle_outside_the_path()
    {
        var project = HingeSweepProject(obstaclePosition: [0.6, 0.6, 0]);

        var diagnostics = MechanicalValidator.Default.Validate(project);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-007");
    }

    [Fact]
    public void Hinge_sweep_ignores_parts_connected_to_the_child()
    {
        var project = HingeSweepProject(obstaclePosition: [0, 0.3, 0], connectObstacle: true);

        var diagnostics = MechanicalValidator.Default.Validate(project);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-007");
    }

    [Fact]
    public void Sphere_and_box_interference_is_detected()
    {
        var frame = Box("frame", [0, 0, 0], [1, 1, 1]);
        var ball = Sphere("ball", [0.6, 0, 0], 0.3);

        var diagnostics = MechanicalValidator.Default.Validate(Project(frame, ball));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-001");
    }

    [Fact]
    public void Slide_joint_out_of_range_reports_EMD_MECH_002()
    {
        var parent = Box("parent", [0, 0, 0], [0.1, 0.1, 0.1]);
        var child = Box("child", [0.3, 0, 0], [0.1, 0.1, 0.1]);
        var project = Project(parent, child, mechanical: new MechanicalSection
        {
            AssemblyRootPartId = parent.Id,
            Joints =
            [
                new MechanicalJoint
                {
                    Id = Guid.NewGuid(),
                    Key = "x_slide",
                    Type = MechanicalJointType.Slide,
                    ParentPartId = parent.Id,
                    ChildPartId = child.Id,
                    Axis = [1, 0, 0],
                    Range = [-0.2, 0.2]
                }
            ]
        });

        var diagnostics = MechanicalValidator.Default.Validate(project);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-002");
    }

    [Fact]
    public void Hinge_joint_out_of_range_reports_EMD_MECH_002()
    {
        var parent = Box("parent", [0, 0, 0], [0.1, 0.1, 0.1]);
        var child = Box("child", [0, 0, 0], [0.1, 0.1, 0.1], rot: [0, 0, 60]);
        var project = Project(parent, child, mechanical: new MechanicalSection
        {
            AssemblyRootPartId = parent.Id,
            Joints =
            [
                new MechanicalJoint
                {
                    Id = Guid.NewGuid(),
                    Key = "z_hinge",
                    Type = MechanicalJointType.Hinge,
                    ParentPartId = parent.Id,
                    ChildPartId = child.Id,
                    Axis = [0, 0, 1],
                    Range = [-45, 45]
                }
            ]
        });

        var diagnostics = MechanicalValidator.Default.Validate(project);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-002");
    }

    [Fact]
    public void Joint_within_range_does_not_report()
    {
        var parent = Box("parent", [0, 0, 0], [0.1, 0.1, 0.1]);
        var child = Box("child", [0.1, 0, 0], [0.1, 0.1, 0.1]);
        var project = Project(parent, child, mechanical: new MechanicalSection
        {
            AssemblyRootPartId = parent.Id,
            Joints =
            [
                new MechanicalJoint
                {
                    Id = Guid.NewGuid(),
                    Key = "x_slide",
                    Type = MechanicalJointType.Slide,
                    ParentPartId = parent.Id,
                    ChildPartId = child.Id,
                    Axis = [1, 0, 0],
                    Range = [-0.2, 0.2]
                }
            ]
        });

        var diagnostics = MechanicalValidator.Default.Validate(project);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-002");
    }

    [Fact]
    public void ProjectValidator_includes_mechanical_checks()
    {
        var frame = Box("frame", [0, 0, 0], [1, 1, 1]);
        var motor = Box("motor", [0, 0, 0], [0.5, 0.5, 0.5]);

        var diagnostics = ProjectValidator.Default.Validate(Project(frame, motor));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "EMD-MECH-001");
    }

    private static EmdProject HingeSweepProject(
        double[] obstaclePosition,
        bool connectObstacle = false)
    {
        var parent = Box("parent", [0, 0, 0], [0.1, 0.1, 0.1]);
        var child = Box("child", [0.2, 0, 0], [0.4, 0.1, 0.1]);
        var obstacle = Box("obstacle", obstaclePosition, [0.1, 0.1, 0.1]);
        var joint = new MechanicalJoint
        {
            Id = Guid.NewGuid(),
            Key = "z_hinge",
            Type = MechanicalJointType.Hinge,
            ParentPartId = parent.Id,
            ChildPartId = child.Id,
            Axis = [0, 0, 1],
            Range = [0, 90],
            Anchor = new Pose { Pos = [0, 0, 0] }
        };
        var connections = new List<Connection>();
        if (connectObstacle)
        {
            connections.Add(new Connection
            {
                Id = Guid.NewGuid(),
                Kind = ConnectionKind.Mate,
                From = new Endpoint { PartId = child.Id },
                To = new Endpoint { PartId = obstacle.Id }
            });
        }

        return new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "hinge sweep",
            Parts = [parent, child, obstacle],
            Connections = connections,
            Mechanical = new MechanicalSection
            {
                AssemblyRootPartId = parent.Id,
                Joints = [joint]
            }
        };
    }

    private static EmdProject SlideSweepProject(double[] obstaclePosition)
    {
        var parent = Box("parent", [0, 0, 0], [0.1, 0.1, 0.1]);
        var child = Box("child", [0, 0, 0], [0.1, 0.1, 0.1]);
        var obstacle = Box("obstacle", obstaclePosition, [0.1, 0.1, 0.1]);
        var joint = new MechanicalJoint
        {
            Id = Guid.NewGuid(),
            Key = "x_slide",
            Type = MechanicalJointType.Slide,
            ParentPartId = parent.Id,
            ChildPartId = child.Id,
            Axis = [1, 0, 0],
            Range = [0, 0.5]
        };

        return new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "sweep",
            Parts = [parent, child, obstacle],
            Mechanical = new MechanicalSection
            {
                AssemblyRootPartId = parent.Id,
                Joints = [joint]
            }
        };
    }

    private static EmdProject Project(
        Part first,
        Part second,
        MechanicalSection? mechanical = null,
        IEnumerable<Connection>? connections = null,
        double? interferenceMm = null,
        double? clearanceMm = null)
    {
        var tolerances = new Dictionary<string, double>();
        if (interferenceMm is not null)
            tolerances["interference_mm"] = interferenceMm.Value;
        if (clearanceMm is not null)
            tolerances["clearance_mm"] = clearanceMm.Value;

        return new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "mechanical test",
            Parts = [first, second],
            Connections = connections?.ToList() ?? [],
            Mechanical = mechanical ?? new MechanicalSection { AssemblyRootPartId = first.Id },
            Validation = tolerances.Count == 0
                ? null
                : new ValidationSection { Tolerances = tolerances }
        };
    }

    private static Part Box(string key, double[] pos, double[] size, double[]? rot = null) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = key,
        Kind = PartKind.Component,
        Pose = new Pose { Pos = pos, Rot = rot },
        Geometry = new PartGeometry { Kind = GeometryKind.Box, Size = size },
        Ports = []
    };

    private static Part Sphere(string key, double[] pos, double radius) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Name = key,
        Kind = PartKind.Component,
        Pose = new Pose { Pos = pos },
        Geometry = new PartGeometry { Kind = GeometryKind.Sphere, Size = [radius, radius, radius] },
        Ports = []
    };
}
