using Emd.Core.Exporters;
using Emd.Core.Projects;

namespace Emd.Core.Tests;

public class AssemblyDocExporterTests
{
    [Fact]
    public void Export_includes_bom_harness_boards_and_firmware()
    {
        var loaded = ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);
        Assert.True(loaded.IsValid);

        var result = AssemblyDocExporter.Default.Export(loaded.Project!);

        Assert.NotNull(result.Markdown);
        var text = result.Markdown!;
        Assert.Contains("# Motorized axis MVP assembly", text);
        Assert.Contains("## Bill of materials", text);
        Assert.Contains("## Harness", text);
        Assert.Contains("## Boards", text);
        Assert.Contains("driver_board", text);
        Assert.Contains("## Firmware", text);
        Assert.Contains("PA0", text);
    }
}
