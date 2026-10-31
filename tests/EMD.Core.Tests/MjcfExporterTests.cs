using System.Globalization;
using System.Xml.Linq;
using Emd.Core.Exporters;
using Emd.Core.Model;

namespace Emd.Core.Tests;

public class MjcfExporterTests
{
    [Fact]
    public void Export_builds_the_body_tree_geoms_joint_and_actuator()
    {
        var project = CreateAxisProject();

        var result = MjcfExporter.Default.Export(project);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var document = XDocument.Parse(result.Xml!);
        var bodies = document.Descendants("body").ToList();
        Assert.Equal(3, bodies.Count);

        var frame = bodies.Single(body => Name(body) == "frame");
        var carriage = bodies.Single(body => Name(body) == "carriage");
        var endstop = bodies.Single(body => Name(body) == "endstop");

        Assert.Equal("worldbody", frame.Parent!.Name.LocalName);
        Assert.Equal("frame", carriage.Parent!.Attribute("name")!.Value);
        Assert.Equal("carriage", endstop.Parent!.Attribute("name")!.Value);

        AssertClose([0.1, 0, 0.02], Vector(carriage, "pos"));
        AssertClose([0.03, 0, 0.01], Vector(endstop, "pos"));

        var frameGeom = Assert.Single(frame.Elements("geom"));
        Assert.Equal("box", (string?)frameGeom.Attribute("type"));
        AssertClose([0.2, 0.1, 0.01], Vector(frameGeom, "size"));

        var joint = Assert.Single(carriage.Elements("joint"));
        Assert.Equal("x_slide", (string?)joint.Attribute("name"));
        Assert.Equal("slide", (string?)joint.Attribute("type"));
        AssertClose([1, 0, 0], Vector(joint, "axis"));
        AssertClose([-0.2, 0.2], Vector(joint, "range"));

        var motor = Assert.Single(document.Descendants("actuator").Elements("motor"));
        Assert.Equal("x_slide", (string?)motor.Attribute("joint"));
        Assert.Equal("x_motor", (string?)motor.Attribute("name"));

        Assert.Empty(endstop.Elements("joint"));
    }

    [Fact]
    public void No_parts_reports_EMD_MJCF_001()
    {
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "empty",
            Parts = []
        };

        var result = MjcfExporter.Default.Export(project);

        Assert.Null(result.Xml);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-MJCF-001");
    }

    [Fact]
    public void Missing_mesh_artifact_reports_EMD_MJCF_002()
    {
        var partId = Guid.NewGuid();
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "mesh test",
            Parts =
            [
                new Part
                {
                    Id = partId,
                    Key = "frame",
                    Name = "frame",
                    Kind = PartKind.Component,
                    Geometry = new PartGeometry
                    {
                        Kind = GeometryKind.Mesh,
                        MeshArtifactId = Guid.NewGuid()
                    },
                    Ports = []
                }
            ]
        };

        var result = MjcfExporter.Default.Export(project);

        Assert.NotNull(result.Xml);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-MJCF-002");
        Assert.Empty(XDocument.Parse(result.Xml!).Descendants("geom"));
    }

    [Fact]
    public void Disconnected_parts_are_emitted_as_world_bodies_with_a_warning()
    {
        var frameId = Guid.NewGuid();
        var sensorId = Guid.NewGuid();
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "disconnected",
            Parts =
            [
                Part(frameId, "frame", [0, 0, 0]),
                Part(sensorId, "sensor", [1, 0, 0])
            ],
            Mechanical = new MechanicalSection { AssemblyRootPartId = frameId }
        };

        var result = MjcfExporter.Default.Export(project);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-MJCF-003");
        var document = XDocument.Parse(result.Xml!);
        var sensor = document.Descendants("body").Single(body => Name(body) == "sensor");
        Assert.Equal("worldbody", sensor.Parent!.Name.LocalName);
    }

    [Fact]
    public void Mesh_geometry_uses_the_artifact_path_and_scale()
    {
        var meshId = Guid.NewGuid();
        var partId = Guid.NewGuid();
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "mesh",
            Artifacts =
            [
                new Artifact
                {
                    Id = meshId,
                    Key = "frame_mesh",
                    Path = "meshes/frame.stl",
                    Format = "stl",
                    Role = ArtifactRole.Generated
                }
            ],
            Parts =
            [
                new Part
                {
                    Id = partId,
                    Key = "frame",
                    Name = "frame",
                    Kind = PartKind.Component,
                    Geometry = new PartGeometry
                    {
                        Kind = GeometryKind.Mesh,
                        MeshArtifactId = meshId,
                        MeshScale = [0.001, 0.001, 0.001]
                    },
                    Ports = []
                }
            ]
        };

        var result = MjcfExporter.Default.Export(project);

        var geom = Assert.Single(XDocument.Parse(result.Xml!).Descendants("geom"));
        Assert.Equal("mesh", (string?)geom.Attribute("type"));
        Assert.Equal("meshes/frame.stl", (string?)geom.Attribute("file"));
        AssertClose([0.001, 0.001, 0.001], Vector(geom, "scale"));
    }

    private static EmdProject CreateAxisProject()
    {
        var frameId = Guid.NewGuid();
        var carriageId = Guid.NewGuid();
        var endstopId = Guid.NewGuid();
        var jointId = Guid.NewGuid();

        return new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "axis",
            Parts =
            [
                Part(frameId, "frame", [0, 0, 0], [0.4, 0.2, 0.02]),
                Part(carriageId, "carriage", [0.1, 0, 0.02], [0.06, 0.06, 0.02]),
                Part(endstopId, "endstop", [0.13, 0, 0.03], [0.02, 0.01, 0.01])
            ],
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    Key = "carriage_endstop",
                    Kind = ConnectionKind.Mate,
                    From = new Endpoint { PartId = carriageId },
                    To = new Endpoint { PartId = endstopId }
                }
            ],
            Mechanical = new MechanicalSection
            {
                AssemblyRootPartId = frameId,
                Joints =
                [
                    new MechanicalJoint
                    {
                        Id = jointId,
                        Key = "x_slide",
                        Type = MechanicalJointType.Slide,
                        ParentPartId = frameId,
                        ChildPartId = carriageId,
                        Axis = [1, 0, 0],
                        Range = [-0.2, 0.2],
                        Actuator = "x_motor",
                        Damping = 0.01
                    }
                ]
            }
        };
    }

    private static Part Part(Guid id, string key, double[] pos, double[]? size = null) => new()
    {
        Id = id,
        Key = key,
        Name = key,
        Kind = PartKind.Component,
        Pose = new Pose { Pos = pos },
        Geometry = size is null ? null : new PartGeometry { Kind = GeometryKind.Box, Size = size },
        Ports = []
    };

    private static string? Name(XElement element) => (string?)element.Attribute("name");

    private static double[] Vector(XElement element, string attribute)
    {
        var value = (string?)element.Attribute(attribute);
        Assert.NotNull(value);
        return value!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => double.Parse(item, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static void AssertClose(double[] expected, double[] actual, double tolerance = 1e-9)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
            Assert.InRange(actual[index], expected[index] - tolerance, expected[index] + tolerance);
    }
}
