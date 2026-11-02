using Emd.Adapters;

namespace Emd.Adapters.Tests;

public class RenodeAdapterTests
{
    [Fact]
    public async Task Run_generates_a_headless_script_and_log()
    {
        using var temp = new DirectoryFixture();
        var firmware = temp.File("axis.elf", "elf");
        var platform = temp.File("axis.repl", "platform");
        var log = Path.Combine(temp.Path, "renode.log");
        var runner = new Runner();
        var adapter = new RenodeAdapter(runner);

        var result = await adapter.RunAsync(firmware, platform, log, 1.5, "uart0");

        Assert.True(result.Success);
        Assert.Equal("renode", runner.Executable);
        Assert.Contains("--console", runner.Arguments);
        Assert.Contains("--disable-gui", runner.Arguments);
        Assert.Contains("sysbus LoadELF", runner.Script);
        Assert.Contains("showAnalyzer uart0", runner.Script);
        Assert.Contains("emulation RunFor", runner.Script);
        Assert.Contains("uart: boot", File.ReadAllText(log));
        Assert.Equal("renode-log", Assert.Single(result.Artifacts).Format);
    }

    [Fact]
    public async Task Missing_firmware_reports_EMD_RENODE_001()
    {
        using var temp = new DirectoryFixture();
        var platform = temp.File("axis.repl", "platform");

        var result = await new RenodeAdapter(new Runner()).RunAsync(
            Path.Combine(temp.Path, "missing.elf"), platform, Path.Combine(temp.Path, "log"));

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-RENODE-001");
    }

    private sealed class Runner : IToolProcessRunner
    {
        public string? Executable { get; private set; }
        public string[] Arguments { get; private set; } = [];
        public string Script { get; private set; } = "";

        public Task<ToolProcessResult> RunAsync(
            string executable,
            IEnumerable<string>? arguments = null,
            string? stdin = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Executable = executable;
            Arguments = (arguments ?? []).ToArray();
            Script = File.ReadAllText(Arguments[^1]);
            return Task.FromResult(new ToolProcessResult(0, "uart: boot", "", []));
        }
    }

    private sealed class DirectoryFixture : IDisposable
    {
        public DirectoryFixture()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "emd-renode-tests", Guid.NewGuid().ToString("N"));
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
