using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Emd.Core.Exporters;
using Emd.Core.Model;

namespace Emd.Core.Tests;

public class SspComposerTests
{
    private static readonly XNamespace Ssd = "http://ssp-standard.org/SSP1/SystemStructureDescription";
    private static readonly XNamespace Ssc = "http://ssp-standard.org/SSP1/SystemStructureCommon";

    [Fact]
    public void Compose_builds_the_ssd_and_fmu_resources()
    {
        using var temp = new TempDirectory();
        var project = ProjectWithFmus(temp.Path);

        var result = SspComposer.Default.Compose(project);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Archive);

        using var archive = new ZipArchive(new MemoryStream(result.Archive!), ZipArchiveMode.Read);
        Assert.Contains(archive.Entries, entry => entry.FullName == "SystemStructure.ssd");
        Assert.Contains(archive.Entries, entry => entry.FullName == "resources/controller.fmu");
        Assert.Contains(archive.Entries, entry => entry.FullName == "resources/plant.fmu");

        var ssd = ReadEntry(archive, "SystemStructure.ssd");
        Assert.StartsWith("<?xml", ssd, StringComparison.Ordinal);
        var document = XDocument.Parse(ssd);
        var root = document.Root!;
        Assert.Equal("SystemStructureDescription", root.Name.LocalName);
        Assert.Equal("2.0", (string?)root.Attribute("version"));

        var controller = root.Descendants(Ssd + "Component")
            .Single(component => (string?)component.Attribute("name") == "controller");
        var plant = root.Descendants(Ssd + "Component")
            .Single(component => (string?)component.Attribute("name") == "plant");
        Assert.Equal("application/x-fmu-sharedlibrary", (string?)controller.Attribute("type"));
        Assert.Contains(controller.Descendants(Ssd + "Connector"), connector =>
            (string?)connector.Attribute("name") == "cmd" &&
            (string?)connector.Attribute("kind") == "output");
        Assert.Contains(plant.Descendants(Ssd + "Connector"), connector =>
            (string?)connector.Attribute("name") == "cmd_in" &&
            (string?)connector.Attribute("kind") == "input");

        var connection = Assert.Single(root.Descendants(Ssd + "Connection"));
        Assert.Equal("controller", (string?)connection.Attribute("startElement"));
        Assert.Equal("cmd", (string?)connection.Attribute("startConnector"));
        Assert.Equal("plant", (string?)connection.Attribute("endElement"));
        Assert.Equal("cmd_in", (string?)connection.Attribute("endConnector"));

        var experiment = Assert.Single(root.Descendants(Ssd + "DefaultExperiment"));
        Assert.Equal("1", (string?)experiment.Attribute("stopTime"));
        Assert.Null(experiment.Attribute("stepSize"));
    }

    [Fact]
    public void Compose_emits_connector_types_from_model_ports()
    {
        using var temp = new TempDirectory();
        var project = ProjectWithFmus(temp.Path);
        var controllerArtifactId = project.Artifacts[0].Id;
        var plantArtifactId = project.Artifacts[1].Id;

        project.Behavior!.Models[0] = project.Behavior.Models[0] with
        {
            Outputs = [new ModelPort { Name = "enable", Variable = "enable", Type = "boolean" }]
        };
        project.Behavior.Models[1] = project.Behavior.Models[1] with
        {
            Inputs = [new ModelPort { Name = "enable_in", Variable = "enable_in", Type = "boolean" }]
        };
        project.Connections.Clear();
        project.Connections.Add(new Connection
        {
            Id = Guid.NewGuid(),
            Key = "enable_to_plant",
            Kind = ConnectionKind.SimulationBinding,
            From = new Endpoint { ArtifactId = controllerArtifactId, Variable = "enable" },
            To = new Endpoint { ArtifactId = plantArtifactId, Variable = "enable_in" }
        });

        var result = SspComposer.Default.Compose(project);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        using var archive = new ZipArchive(new MemoryStream(result.Archive!), ZipArchiveMode.Read);
        var document = XDocument.Parse(ReadEntry(archive, "SystemStructure.ssd"));
        var controllers = document.Root!.Descendants(Ssd + "Component")
            .Where(component => (string?)component.Attribute("name") == "controller")
            .ToList();
        var controller = Assert.Single(controllers);
        var enable = Assert.Single(controller.Descendants(Ssd + "Connector"),
            connector => (string?)connector.Attribute("name") == "enable");
        Assert.NotNull(enable.Element(Ssc + "Boolean"));
        Assert.Null(enable.Element(Ssc + "Real"));

        var plant = document.Root.Descendants(Ssd + "Component")
            .Single(component => (string?)component.Attribute("name") == "plant");
        var enableIn = Assert.Single(plant.Descendants(Ssd + "Connector"),
            connector => (string?)connector.Attribute("name") == "enable_in");
        Assert.NotNull(enableIn.Element(Ssc + "Boolean"));
    }

    [Fact]
    public void Compose_can_emit_an_ssp_1_0_compatibility_archive()
    {
        using var temp = new TempDirectory();
        var project = ProjectWithFmus(temp.Path);

        var result = SspComposer.Default.Compose(project, "1.0");

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        using var archive = new ZipArchive(new MemoryStream(result.Archive!), ZipArchiveMode.Read);
        var document = XDocument.Parse(ReadEntry(archive, "SystemStructure.ssd"));
        Assert.Equal("1.0", (string?)document.Root!.Attribute("version"));
    }

    [Fact]
    public void Missing_fmu_file_reports_EMD_SSP_002()
    {
        using var temp = new TempDirectory();
        var project = ProjectWithFmus(temp.Path, deletePlantFmu: true);

        var result = SspComposer.Default.Compose(project);

        Assert.Null(result.Archive);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-SSP-002");
    }

    [Fact]
    public void Unsupported_ssp_version_reports_EMD_SSP_004()
    {
        using var temp = new TempDirectory();
        var project = ProjectWithFmus(temp.Path);

        var result = SspComposer.Default.Compose(project, "3.0");

        Assert.Null(result.Archive);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-SSP-004");
    }

    [Fact]
    public void Project_without_fmu_models_reports_EMD_SSP_001()
    {
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "no fmu",
            Parts = []
        };

        var result = SspComposer.Default.Compose(project);

        Assert.Null(result.Archive);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-SSP-001");
    }

    private static EmdProject ProjectWithFmus(string directory, bool deletePlantFmu = false)
    {
        var controllerPath = Path.Combine(directory, "controller.fmu");
        var plantPath = Path.Combine(directory, "plant.fmu");
        File.WriteAllText(controllerPath, "controller fmu");
        File.WriteAllText(plantPath, "plant fmu");
        if (deletePlantFmu)
            File.Delete(plantPath);

        var controllerArtifactId = Guid.NewGuid();
        var plantArtifactId = Guid.NewGuid();
        return new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "SSP test",
            Parts =
            [
                new Part
                {
                    Id = Guid.NewGuid(),
                    Key = "frame",
                    Name = "Frame",
                    Kind = PartKind.Component,
                    Ports = []
                }
            ],
            Artifacts =
            [
                new Artifact
                {
                    Id = controllerArtifactId,
                    Key = "controller_fmu",
                    Path = controllerPath,
                    Format = "fmu",
                    Role = ArtifactRole.Simulation
                },
                new Artifact
                {
                    Id = plantArtifactId,
                    Key = "plant_fmu",
                    Path = plantPath,
                    Format = "fmu",
                    Role = ArtifactRole.Simulation
                }
            ],
            Behavior = new BehaviorSection
            {
                Models =
                [
                    new BehaviorModel
                    {
                        Id = Guid.NewGuid(),
                        Key = "controller",
                        Name = "Controller",
                        Kind = ModelKind.Fmu,
                        ArtifactId = controllerArtifactId,
                        Outputs = [new ModelPort { Name = "cmd", Variable = "cmd" }]
                    },
                    new BehaviorModel
                    {
                        Id = Guid.NewGuid(),
                        Key = "plant",
                        Name = "Plant",
                        Kind = ModelKind.Fmu,
                        ArtifactId = plantArtifactId,
                        Inputs = [new ModelPort { Name = "cmd_in", Variable = "cmd_in" }]
                    }
                ]
            },
            Connections =
            [
                new Connection
                {
                    Id = Guid.NewGuid(),
                    Key = "cmd_to_plant",
                    Kind = ConnectionKind.SimulationBinding,
                    From = new Endpoint { ArtifactId = controllerArtifactId, Variable = "cmd" },
                    To = new Endpoint { ArtifactId = plantArtifactId, Variable = "cmd_in" }
                }
            ],
            Simulation = new SimulationSection
            {
                Systems =
                [
                    new SimSystem
                    {
                        Id = Guid.NewGuid(),
                        Key = "twin",
                        Standard = SimulationStandard.Ssp2,
                        Master = SimulationMaster.Omsimulator,
                        StepSizeS = 0.001,
                        StartTimeS = 0,
                        StopTimeS = 1
                    }
                ]
            }
        };
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name)!;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
