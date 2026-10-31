using Emd.Core.Exporters;
using Emd.Core.Importers;
using Emd.Core.Model;

namespace Emd.Core.Tests;

public class MechMakerMachineExporterTests
{
    [Fact]
    public void Export_round_trips_a_mechmaker_import()
    {
        var imported = MechMakerImporter.Default.Import(
            File.ReadAllText(TestPaths.Fixture("linear_axis.json")), "linear_axis.json");
        Assert.True(imported.IsValid, string.Join(Environment.NewLine, imported.Diagnostics));

        var exported = MechMakerMachineExporter.Default.Export(imported.Project!);

        Assert.True(exported.IsValid, string.Join(Environment.NewLine, exported.Diagnostics));
        Assert.Empty(exported.Diagnostics);
        var json = exported.Machine!.ToJsonString();
        var reloaded = MechMakerImporter.Default.Import(json, "roundtrip.json");

        Assert.Empty(reloaded.Diagnostics);
        Assert.Equal(imported.Project!.Parts.Count, reloaded.Project!.Parts.Count);
        Assert.Equal(imported.Project.Connections.Count, reloaded.Project.Connections.Count);
        Assert.Equal(imported.Project.Electrical!.Nets.Count, reloaded.Project.Electrical!.Nets.Count);
    }

    [Fact]
    public void Export_is_deterministic()
    {
        var imported = MechMakerImporter.Default.Import(
            File.ReadAllText(TestPaths.Fixture("linear_axis.json")), "linear_axis.json");

        var first = MechMakerMachineExporter.Default.Export(imported.Project!);
        var second = MechMakerMachineExporter.Default.Export(imported.Project!);

        Assert.Equal(first.Machine!.ToJsonString(), second.Machine!.ToJsonString());
    }

    [Fact]
    public void Part_without_a_mechmaker_catalog_ref_is_reported()
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
                    Ports = []
                }
            ]
        };

        var result = MechMakerMachineExporter.Default.Export(project);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-MECHMAKER-001");
        Assert.Null(result.Machine);
    }
}
