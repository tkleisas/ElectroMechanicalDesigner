using Emd.Core.Exporters;
using Emd.Core.Model;

namespace Emd.Core.Tests;

public class BomExporterTests
{
    [Fact]
    public void Export_groups_identical_parts_and_totals_mass()
    {
        var project = Project(
            Part("x_motor", "NEMA 17", "nema17_stepper", 0.28, "steel", PartCategory.Motor),
            Part("y_motor", "NEMA 17", "nema17_stepper", 0.28, "steel", PartCategory.Motor),
            Part("frame", "Frame, left", null, 1.2, "AL_6063", PartCategory.Structure));

        var result = BomExporter.Default.Export(project);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var lines = result.Csv!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal(
            "item,quantity,refs,name,category,mass_each_kg,mass_total_kg,material,catalog",
            lines[0]);
        Assert.Contains(lines, line =>
            line.Contains("x_motor;y_motor", StringComparison.Ordinal) &&
            line.Contains("0.56", StringComparison.Ordinal));
        Assert.Contains(lines, line =>
            line.Contains("\"Frame, left\"", StringComparison.Ordinal) &&
            line.Contains("1.2", StringComparison.Ordinal));
    }

    [Fact]
    public void Export_without_parts_reports_EMD_BOM_001()
    {
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "empty",
            Parts = []
        };

        var result = BomExporter.Default.Export(project);

        Assert.Null(result.Csv);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-BOM-001");
    }

    [Fact]
    public void Export_is_deterministic()
    {
        var project = Project(
            Part("frame", "Frame", null, 1.2, null, PartCategory.Structure),
            Part("motor", "Motor", "nema17_stepper", 0.28, null, PartCategory.Motor));

        var first = BomExporter.Default.Export(project);
        var second = BomExporter.Default.Export(project);

        Assert.Equal(first.Csv, second.Csv);
    }

    private static EmdProject Project(params Part[] parts) => new()
    {
        Id = Guid.NewGuid(),
        Name = "BOM test",
        Parts = parts.ToList()
    };

    private static Part Part(
        string key,
        string name,
        string? catalogPart,
        double? massKg,
        string? material,
        PartCategory category) => new()
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = name,
            Kind = PartKind.Component,
            Category = category,
            CatalogRef = catalogPart is null
                ? null
                : new PartCatalogRef { Catalog = "mechmaker", Part = catalogPart },
            MassKg = massKg,
            Material = material is null ? null : new Material { Name = material },
            Ports = []
        };
}
