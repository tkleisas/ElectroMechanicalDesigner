using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Emd.Core.Exporters;

namespace Emd.Core.Tests;

public class KiCadFabricationPackageExporterTests
{
    [Fact]
    public void Export_bundles_gerbers_bom_positions_step_and_manifest()
    {
        using var temp = new TempDirectory();
        var gerber = Path.Combine(temp.Path, "axis-F_Cu.gbr");
        var drill = Path.Combine(temp.Path, "axis.drl");
        var bom = Path.Combine(temp.Path, "bom.csv");
        var pos = Path.Combine(temp.Path, "positions.csv");
        var step = Path.Combine(temp.Path, "board.step");
        File.WriteAllText(gerber, "gerber");
        File.WriteAllText(drill, "drill");
        File.WriteAllText(bom, "item\n1\n");
        File.WriteAllText(pos, "ref,x,y\n");
        File.WriteAllText(step, "ISO-10303-21;");

        var result = KiCadFabricationPackageExporter.Default.Export(
            new KiCadFabricationInputs("axis", [gerber, drill], bom, pos, step));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        using var archive = new ZipArchive(new MemoryStream(result.Archive!), ZipArchiveMode.Read);
        Assert.Contains(archive.Entries, entry => entry.FullName == "gerbers/axis-F_Cu.gbr");
        Assert.Contains(archive.Entries, entry => entry.FullName == "gerbers/axis.drl");
        Assert.Contains(archive.Entries, entry => entry.FullName == "bom.csv");
        Assert.Contains(archive.Entries, entry => entry.FullName == "positions.csv");
        Assert.Contains(archive.Entries, entry => entry.FullName == "board.step");
        var manifest = JsonNode.Parse(Read(archive, "manifest.json"))!.AsObject();
        Assert.Equal("kicad-fabrication", (string?)manifest["kind"]);
        Assert.Equal(5, manifest["files"]!.AsArray().Count);
    }

    [Fact]
    public void Export_requires_a_gerber_file()
    {
        var result = KiCadFabricationPackageExporter.Default.Export(
            new KiCadFabricationInputs("axis", []));

        Assert.Null(result.Archive);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-CAM-005");
    }

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
