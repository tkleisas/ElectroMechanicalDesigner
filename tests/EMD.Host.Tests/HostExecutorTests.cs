using System.Diagnostics;
using Emd.Contracts;
using Emd.Host;

namespace Emd.Host.Tests;

public sealed class HostExecutorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "emd-host-tests", Guid.NewGuid().ToString("N"));

    public HostExecutorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Probe_reports_git_and_the_workspace_root()
    {
        var executor = new HostExecutor(_root, "test-agent");
        var capabilities = await executor.ProbeAsync();

        Assert.Equal("test-agent", capabilities.AgentId);
        Assert.Equal(Path.GetFullPath(_root), capabilities.WorkspaceRoot);
        Assert.Contains(capabilities.Tools, tool => tool.Name == "git");
        Assert.Contains(capabilities.Tools, tool => tool.Name == "freecad");
        Assert.Contains(capabilities.Tools, tool => tool.Name == "kicad");
    }

    [Fact]
    public async Task Checkout_then_commit_pushes_to_the_repository()
    {
        var (bare, source) = CreateRepository();
        var executor = new HostExecutor(_root, "test-agent");

        var checkout = await executor.ExecuteAsync(new HostCommandRequest(
            "git.checkout", "demo", bare, "main"));
        Assert.True(checkout.Success, checkout.Message);
        var working = checkout.Path!;
        await File.WriteAllTextAsync(Path.Combine(working, "change.txt"), "change");

        var commit = await executor.ExecuteAsync(new HostCommandRequest(
            "git.commit", "demo", Branch: "main", Message: "host commit",
            AuthorName: "EMD Host", AuthorEmail: "host@example.test"));
        Assert.True(commit.Success, commit.Message);
        Assert.False(string.IsNullOrWhiteSpace(commit.Commit));

        var log = Run("git", "--git-dir", bare, "log", "--oneline");
        Assert.Contains("host commit", log);
        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public async Task Checkout_rejects_workspace_path_traversal()
    {
        var executor = new HostExecutor(_root, "test-agent");
        var result = await executor.ExecuteAsync(new HostCommandRequest(
            "git.checkout", "../escape", "repo", "main"));
        Assert.False(result.Success);
        Assert.Contains("workspace key", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_command_fails_cleanly()
    {
        var executor = new HostExecutor(_root, "test-agent");
        var result = await executor.ExecuteAsync(new HostCommandRequest("unknown"));
        Assert.False(result.Success);
        Assert.Contains("unsupported", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private (string Bare, string Source) CreateRepository()
    {
        var bare = Path.Combine(_root, "remote.git");
        var source = Path.Combine(_root, "source");
        Run("git", "init", "--bare", "-b", "main", bare);
        Run("git", "init", "-b", "main", source);
        File.WriteAllText(Path.Combine(source, "README.md"), "initial");
        Run("git", "-C", source, "add", ".");
        Run("git", "-C", source, "-c", "user.name=Test", "-c", "user.email=test@example.test",
            "commit", "-m", "initial");
        Run("git", "-C", source, "remote", "add", "origin", bare);
        Run("git", "-C", source, "push", "-u", "origin", "main");
        return (bare, source);
    }

    private static string Run(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{file} {string.Join(' ', args)}: {error}");
        return output;
    }

}
