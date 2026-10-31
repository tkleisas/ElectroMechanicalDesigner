using System.Diagnostics;
using Emd.Adapters;

namespace Emd.Adapters.Tests;

public class ProcessToolHostTests
{
    private readonly ProcessToolHost _host = ProcessToolHost.Default;

    [Fact]
    public async Task Run_captures_stdout_and_exit_code()
    {
        var result = await _host.RunAsync("dotnet", ["--version"]);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Stdout));
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Run_missing_executable_returns_EMD_ADAPTER_001()
    {
        var result = await _host.RunAsync(
            Path.Combine(Path.GetTempPath(), "emd-missing-" + Guid.NewGuid().ToString("N")));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "EMD-ADAPTER-001");
    }

    [Fact]
    public async Task Run_captures_stderr_and_nonzero_exit()
    {
        var result = await _host.RunAsync("dotnet", ["--definitely-not-a-valid-option"]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Stdout + result.Stderr));
    }

    [Fact]
    public async Task Run_writes_stdin_to_the_process()
    {
        if (OperatingSystem.IsWindows())
            return;

        var result = await _host.RunAsync("cat", [], stdin: "hello from stdin\n");

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello from stdin\n", result.Stdout);
    }

    [Fact]
    public async Task Run_preserves_argument_boundaries()
    {
        if (OperatingSystem.IsWindows())
            return;

        var result = await _host.RunAsync("echo", ["hello world", "second argument"]);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("hello world second argument\n", result.Stdout);
    }

    [Fact]
    public async Task Timeout_kills_the_process_and_returns_EMD_ADAPTER_002()
    {
        if (OperatingSystem.IsWindows())
            return;

        var stopwatch = Stopwatch.StartNew();
        var result = await _host.RunAsync(
            "sleep", ["5"], timeout: TimeSpan.FromMilliseconds(200));

        stopwatch.Stop();
        Assert.Contains(result.Diagnostics, d => d.Code == "EMD-ADAPTER-002");
        Assert.False(result.Succeeded);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
    }
}
