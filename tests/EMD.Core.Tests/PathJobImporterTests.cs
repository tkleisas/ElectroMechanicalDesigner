using Emd.Core.Importers;

namespace Emd.Core.Tests;

public class PathJobImporterTests
{
    [Fact]
    public void Importer_reads_booleans_and_relationships()
    {
        var result = PathJobImporter.Default.ImportFile(TestPaths.Fixture("path_job.json"));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        var job = result.Job!;
        Assert.Equal("frame_profile", job.Name);
        Assert.Equal(1, job.ToolCount);
        Assert.Equal(1, job.OperationCount);
        Assert.Equal("klipper", job.Postprocessor);
        Assert.EndsWith("frame.gcode", job.GCodePath);
    }

    [Fact]
    public void Importer_rejects_unknown_tools()
    {
        var json = File.ReadAllText(TestPaths.Fixture("path_job.json"))
            .Replace("\"tool\": 1", "\"tool\": 9", StringComparison.Ordinal);

        var result = PathJobImporter.Default.Import(json, "path_job.json");

        Assert.Null(result.Job);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-CAM-008");
    }
}
