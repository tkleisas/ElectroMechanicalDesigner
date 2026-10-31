using Emd.Core.Exporters;
using Emd.Core.Model;

namespace Emd.Core.Tests;

public class HarnessExporterTests
{
    [Fact]
    public void Export_builds_connectors_cables_and_connections()
    {
        var project = CreateProject();

        var result = HarnessExporter.Default.Export(project);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var yaml = result.Yaml!;
        Assert.Contains("connectors:", yaml, StringComparison.Ordinal);
        Assert.Contains("\"driver\":", yaml, StringComparison.Ordinal);
        Assert.Contains("type: \"skr-pico\"", yaml, StringComparison.Ordinal);
        Assert.Contains("pincount: 2", yaml, StringComparison.Ordinal);
        Assert.Contains("cables:", yaml, StringComparison.Ordinal);
        Assert.Contains("\"W1\":", yaml, StringComparison.Ordinal);
        Assert.Contains("connections:", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"driver\": [\"M1A\"]", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"motor\": [\"A+\"]", yaml, StringComparison.Ordinal);
        Assert.Contains("- \"W1\": [1]", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Nets_with_more_than_two_endpoints_are_reported_as_EMD_HARNESS_001()
    {
        var project = CreateProject(withThirdEndpoint: true);

        var result = HarnessExporter.Default.Export(project);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-HARNESS-001");
    }

    [Fact]
    public void Export_is_deterministic()
    {
        var project = CreateProject();

        var first = HarnessExporter.Default.Export(project);
        var second = HarnessExporter.Default.Export(project);

        Assert.Equal(first.Yaml, second.Yaml);
    }

    private static EmdProject CreateProject(bool withThirdEndpoint = false)
    {
        var motor = new Part
        {
            Id = Guid.NewGuid(),
            Key = "motor",
            Name = "NEMA17",
            Kind = PartKind.Component,
            CatalogRef = new PartCatalogRef { Catalog = "mechmaker", Part = "nema17_stepper" },
            Ports =
            [
                Port("phase_a", "A+"),
                Port("phase_b", "B+")
            ]
        };
        var driver = new Part
        {
            Id = Guid.NewGuid(),
            Key = "driver",
            Name = "Stepper driver",
            Kind = PartKind.Board,
            CatalogRef = new PartCatalogRef { Catalog = "kicad", Part = "skr-pico" },
            Ports =
            [
                Port("m1_a", "M1A"),
                Port("m1_b", "M1B")
            ]
        };

        var netA = new Net
        {
            Id = Guid.NewGuid(),
            Key = "m1a",
            Name = "M1_A",
            Class = NetClass.Signal
        };
        var netB = new Net
        {
            Id = Guid.NewGuid(),
            Key = "m1b",
            Name = "M1_B",
            Class = NetClass.Signal
        };

        var connections = new List<Connection>
        {
            Wire(motor, "phase_a", netA),
            Wire(driver, "m1_a", netA),
            Wire(motor, "phase_b", netB),
            Wire(driver, "m1_b", netB)
        };

        var parts = new List<Part> { motor, driver };
        if (withThirdEndpoint)
        {
            var psu = new Part
            {
                Id = Guid.NewGuid(),
                Key = "psu",
                Name = "PSU",
                Kind = PartKind.Component,
                Ports = [Port("vmot", "VMOT")]
            };
            parts.Add(psu);
            connections.Add(Wire(psu, "vmot", netA));
        }

        return new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "harness test",
            Parts = parts,
            Connections = connections,
            Electrical = new ElectricalSection { Nets = [netB, netA] }
        };
    }

    private static Port Port(string id, string key) => new()
    {
        Id = id,
        Key = key,
        Name = key,
        Domain = PortDomain.Electrical,
        Kind = "pin",
        Direction = PortDirection.Inout
    };

    private static Connection Wire(Part part, string portId, Net net) => new()
    {
        Id = Guid.NewGuid(),
        Kind = ConnectionKind.Wire,
        From = new Endpoint { PartId = part.Id, PortId = portId },
        To = new Endpoint { NetId = net.Id }
    };
}
