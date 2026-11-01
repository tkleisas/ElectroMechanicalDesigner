using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Emd.Core.Exporters;
using Emd.Core.Model;
using Emd.Core.Projects;

namespace Emd.Core.Tests;

public class PackageExporterTests
{
    [Fact]
    public void Export_includes_core_files_and_a_manifest()
    {
        var loaded = ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));

        var result = PackageExporter.Default.Export(loaded.Project!);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var names = result.Files.Select(file => file.Name).ToList();
        Assert.Contains("project.json", names);
        Assert.Contains("bom.csv", names);
        Assert.Contains("harness.yml", names);
        Assert.Contains("scene.xml", names);
        Assert.Contains("machine.json", names);
        Assert.Contains("manifest.json", names);

        var manifestFile = result.Files.Single(file => file.Name == "manifest.json");
        var manifest = JsonNode.Parse(Encoding.UTF8.GetString(manifestFile.Content))!.AsObject();
        var entries = manifest["files"]!.AsArray();
        Assert.Equal(result.Files.Count - 1, entries.Count);
        foreach (var entry in entries)
        {
            var name = entry!["name"]!.GetValue<string>();
            var file = result.Files.Single(candidate => candidate.Name == name);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(file.Content)).ToLowerInvariant(),
                entry["sha256"]!.GetValue<string>());
        }
    }

    [Fact]
    public void Export_includes_extra_trace_files()
    {
        var loaded = ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);
        var trace = new PackageFile(
            "traces/run.csv",
            "csv",
            "simulation",
            Encoding.UTF8.GetBytes("time,x\n0,0\n"),
            "");

        var result = PackageExporter.Default.Export(loaded.Project!, [trace]);

        Assert.Contains(result.Files, file => file.Name == "traces/run.csv");
        var manifest = JsonNode.Parse(Encoding.UTF8.GetString(
            result.Files.Single(file => file.Name == "manifest.json").Content))!.AsObject();
        Assert.Contains(manifest["files"]!.AsArray(), entry =>
            (string?)entry!["name"] == "traces/run.csv");
    }

    [Fact]
    public void Export_includes_scenario_trace_artifacts()
    {
        using var temp = new TempDirectory();
        var tracePath = Path.Combine(temp.Path, "run.csv");
        File.WriteAllText(tracePath, "time,x\n0,0\n");
        var artifactId = Guid.NewGuid();
        var artifact = new Artifact
        {
            Id = artifactId,
            Key = "trace_run",
            Path = tracePath,
            Format = "csv",
            Role = ArtifactRole.Simulation,
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tracePath))).ToLowerInvariant()
        };
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "trace package",
            Parts =
            [
                new Part
                {
                    Id = Guid.NewGuid(),
                    Key = "frame",
                    Name = "Frame",
                    Kind = PartKind.Component,
                    Geometry = new PartGeometry { Kind = GeometryKind.Box, Size = [0.1, 0.1, 0.1] },
                    Ports = []
                }
            ],
            Artifacts = [artifact],
            Simulation = new SimulationSection
            {
                Scenarios =
                [
                    new Scenario
                    {
                        Id = Guid.NewGuid(),
                        Key = "run",
                        Name = "Run",
                        TraceArtifactId = artifactId
                    }
                ]
            }
        };

        var result = PackageExporter.Default.Export(project);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains(result.Files, file => file.Name == "traces/run.csv");
    }

    [Fact]
    public void Export_omits_machine_json_without_catalog_parts()
    {
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "no catalog",
            Parts =
            [
                new Part
                {
                    Id = Guid.NewGuid(),
                    Key = "frame",
                    Name = "Frame",
                    Kind = PartKind.Component,
                    Geometry = new PartGeometry { Kind = GeometryKind.Box, Size = [0.1, 0.1, 0.1] },
                    Ports = []
                }
            ]
        };

        var result = PackageExporter.Default.Export(project);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.DoesNotContain(result.Files, file => file.Name == "machine.json");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-PACKAGE-001");
    }

    [Fact]
    public void Export_includes_manufacturing_artifacts()
    {
        using var temp = new TempDirectory();
        var gcodePath = Path.Combine(temp.Path, "frame.gcode");
        File.WriteAllText(gcodePath, "G28\nG1 X40\n");
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "manufacturing package",
            Parts =
            [
                new Part
                {
                    Id = Guid.NewGuid(),
                    Key = "frame",
                    Name = "Frame",
                    Kind = PartKind.Component,
                    Geometry = new PartGeometry { Kind = GeometryKind.Box, Size = [0.1, 0.1, 0.1] },
                    Ports = []
                }
            ],
            Artifacts =
            [
                new Artifact
                {
                    Id = Guid.NewGuid(),
                    Key = "frame_gcode",
                    Path = gcodePath,
                    Format = "gcode",
                    Role = ArtifactRole.Manufacturing
                }
            ]
        };

        var result = PackageExporter.Default.Export(project);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var file = Assert.Single(result.Files, candidate => candidate.Name == "manufacturing/frame_gcode.gcode");
        Assert.Equal("G28\nG1 X40\n", Encoding.UTF8.GetString(file.Content));
        Assert.Equal("manufacturing", file.Role);
    }

    [Fact]
    public void Export_is_deterministic()
    {
        var loaded = ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);

        var first = PackageExporter.Default.Export(loaded.Project!);
        var second = PackageExporter.Default.Export(loaded.Project!);

        Assert.Equal(
            first.Files.Select(file => (file.Name, file.Sha256)),
            second.Files.Select(file => (file.Name, file.Sha256)));
    }
}
