using Emd.Adapters;

namespace Emd.Adapters.Tests;

public class CamoticsAdapterTests
{
    [Fact]
    public async Task Verify_runs_camotics_and_returns_the_surface()
    {
        using var temp = new DirectoryFixture();
        var gcode = temp.File("part.gcode", "G1 X1");
        var output = Path.Combine(temp.Path, "part.stl");
        var runner = new Runner(output);
        var adapter = new CamoticsAdapter(runner);

        var result = await adapter.VerifyAsync(gcode, output);

        Assert.True(result.Success);
        Assert.Equal("camotics", runner.Executable);
        Assert.Equal([gcode, output], runner.Arguments);
        Assert.Equal("stl", Assert.Single(result.Artifacts).Format);
    }

    [Fact]
    public async Task Missing_surface_reports_EMD_CAM_007()
    {
        using var temp = new DirectoryFixture();
        var gcode = temp.File("part.gcode", "G1 X1");
        var output = Path.Combine(temp.Path, "missing.stl");
        var adapter = new CamoticsAdapter(new Runner(null));

        var result = await adapter.VerifyAsync(gcode, output);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-CAM-007");
    }

    private sealed class Runner : IToolProcessRunner
    {
        private readonly string? _output;

        public Runner(string? output)
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
            if (_output is not null)
                File.WriteAllText(_output, "solid surface");
            return Task.FromResult(new ToolProcessResult(0, "", "", []));
        }
    }

    private sealed class DirectoryFixture : IDisposable
    {
        public DirectoryFixture()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "emd-camotics-tests", Guid.NewGuid().ToString("N"));
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
