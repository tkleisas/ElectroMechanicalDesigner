using Emd.Core.Diagnostics;
using Emd.Core.Importers;
using Emd.Core.Model;
using Emd.Core.Projects;
using Emd.Core.Validation;

namespace Emd.Core.Tests;

public class KiCadNetlistImporterTests
{
    private static string FixtureXml => File.ReadAllText(TestPaths.Fixture("kicad_netlist.xml"));

    [Fact]
    public void Import_maps_components_nets_and_pins()
    {
        var result = KiCadNetlistImporter.Default.Import(FixtureXml, "axis.net");

        Assert.Empty(result.Diagnostics);
        var project = Assert.IsType<EmdProject>(result.Project);
        Assert.Equal("axis", project.Name);
        Assert.Equal(2, project.Parts.Count);
        Assert.Equal(2, project.Electrical!.Nets.Count);
        Assert.Equal(4, project.Connections.Count);

        var motor = project.Parts.Single(part => part.Key == "M1");
        Assert.Equal(PartCategory.Electronics, motor.Category);
        Assert.Contains(motor.Ports, port => port.Id == "_1" && port.Key == "1");
        Assert.Contains(motor.Ports, port => port.Id == "_2" && port.Key == "2");
        Assert.Equal("kicad", motor.DomainRefs!.Electrical!.Tool);
        Assert.Equal("M1", motor.DomainRefs.Electrical.Ref);

        Assert.DoesNotContain(project.Parts, part => part.Key!.StartsWith("#", StringComparison.Ordinal));

        Assert.All(project.Electrical.Nets, net =>
            Assert.Equal(2, project.Connections.Count(connection =>
                connection.From.NetId == net.Id || connection.To.NetId == net.Id)));

        var gnd = project.Electrical.Nets.Single(net => net.Name == "GND");
        Assert.Equal(NetClass.Ground, gnd.Class);
    }

    [Fact]
    public void Imported_project_round_trips_through_schema_and_semantic_validation()
    {
        var imported = KiCadNetlistImporter.Default.Import(FixtureXml, "axis.net");
        var json = ProjectWriter.Serialize(imported.Project!);

        var loaded = ProjectLoader.Default.Load(json);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        Assert.DoesNotContain(ProjectValidator.Default.Validate(loaded.Project!),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Import_is_deterministic()
    {
        var first = KiCadNetlistImporter.Default.Import(FixtureXml, "axis.net");
        var second = KiCadNetlistImporter.Default.Import(FixtureXml, "axis.net");

        Assert.Equal(first.Project!.Id, second.Project!.Id);
        Assert.Equal(
            ProjectWriter.Serialize(first.Project),
            ProjectWriter.Serialize(second.Project));
    }

    [Fact]
    public void Malformed_xml_reports_EMD_KICAD_001()
    {
        var result = KiCadNetlistImporter.Default.Import("<export><components>");

        Assert.Null(result.Project);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-KICAD-001");
    }

    [Fact]
    public void Node_with_unknown_component_reports_EMD_KICAD_002()
    {
        var xml = FixtureXml.Replace("ref=\"M1\" pin=\"2\"", "ref=\"MISSING\" pin=\"2\"", StringComparison.Ordinal);

        var result = KiCadNetlistImporter.Default.Import(xml, "axis.net");

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-KICAD-002");
    }
}
