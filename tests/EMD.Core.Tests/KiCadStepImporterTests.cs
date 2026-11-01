using Emd.Core.Importers;

namespace Emd.Core.Tests;

public class KiCadStepImporterTests
{
    private const string Step = """
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION(('test'),'2;1');
        FILE_NAME('board.step','2026-11-01T00:00:00',(''),(''),'','','');
        FILE_SCHEMA(('AUTOMOTIVE_DESIGN'));
        ENDSEC;
        DATA;
        #1=CARTESIAN_POINT('',(0.,0.,0.));
        #2=CARTESIAN_POINT('',(100.,50.,1.6));
        ENDSEC;
        END-ISO-10303-21;
        """;

    [Fact]
    public void Import_reads_header_and_bounding_box()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "board.step");
        File.WriteAllText(path, Step);

        var result = KiCadStepImporter.Default.ImportFile(path);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Artifact);
        Assert.Equal("step", result.Artifact!.Format);
        Assert.Equal("board.step", result.Info!.FileName);
        Assert.Equal("AUTOMOTIVE_DESIGN", result.Info.Schema);
        Assert.Equal(2, result.Info.PointCount);
        Assert.Equal(100, result.Info.WidthMm);
        Assert.Equal(50, result.Info.HeightMm);
        Assert.Equal(1.6, result.Info.DepthMm, 3);
        Assert.Equal(64, result.Artifact.Sha256!.Length);
    }

    [Fact]
    public void Import_rejects_non_step_content()
    {
        var result = KiCadStepImporter.Default.Import("<not step>", "bad.step");

        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-KICAD-023");
    }
}
