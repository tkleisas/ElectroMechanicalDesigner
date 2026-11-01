using Emd.Adapters;

namespace Emd.Adapters.Tests;

public class SlicerAdapterTests
{
    [Fact]
    public async Task Orca_slice_builds_the_expected_command()
    {
        using var temp = new DirectoryFixture();
        var model = temp.File("part.stl", "solid part");
        var output = Path.Combine(temp.Path, "part.gcode");
        var runner = new Runner(output);
        var adapter = new SlicerAdapter(runner);

        var result = await adapter.SliceAsync(model, output, "orca", ["profile.ini"]);

        Assert.True(result.Success);
        Assert.Equal("orca-slicer", runner.Executable);
        Assert.Equal(
            ["--export-gcode", "--output", output, "--load", "profile.ini", model],
            runner.Arguments);
        Assert.Equal("gcode", Assert.Single(result.Artifacts).Format);
    }

    [Fact]
    public async Task Unknown_slicer_returns_a_diagnostic()
    {
        using var temp = new DirectoryFixture();
        var model = temp.File("part.stl", "solid");
        var adapter = new SlicerAdapter(new Runner(Path.Combine(temp.Path, "part.gcode")));

        var result = await adapter.SliceAsync(model, "part.gcode", "unknown");

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-SLICER-001");
    }

    private sealed class Runner : IToolProcessRunner
    {
        private readonly string _output;

        public Runner(string output)
        {
            _output = output;
        }

        public string? Executable { get; private set; }
        public string[] Arguments { get; private set; } = [];

        public Task<ToolProcessResult> RunAsync(
            string executable,
            IEnumerable<string>? arguments = null,
            string? stdin = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Executable = executable;
            Arguments = (arguments ?? []).ToArray();
            File.WriteAllText(_output, "G21\nG90\nG1 X1 Y1 F500\n");
            return Task.FromResult(new ToolProcessResult(0, "", "", []));
        }
    }

    private sealed class DirectoryFixture : IDisposable
    {
        public DirectoryFixture()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "emd-slicer-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name, string content)
        {
            var path = System.IO.Path.Combine(Path, name);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
