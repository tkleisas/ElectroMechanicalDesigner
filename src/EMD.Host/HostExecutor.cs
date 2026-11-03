using System.Diagnostics;
using System.Text.RegularExpressions;
using Emd.Contracts;

namespace Emd.Host;

public sealed class HostExecutor
{
    private static readonly Regex WorkspaceKeyPattern = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
    private readonly IHostProcessRunner _runner;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public HostExecutor(string workspaceRoot, string agentId, IHostProcessRunner? runner = null)
    {
        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        Directory.CreateDirectory(WorkspaceRoot);
        AgentId = agentId;
        _runner = runner ?? new HostProcessRunner();
    }

    public string WorkspaceRoot { get; }
    public string AgentId { get; }

    public async Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var tools = new List<HostTool>
        {
            new("git", await VersionAsync("git", ["--version"], cancellationToken), false),
            new("freecad", null, true),
            new("kicad", null, true),
            new("freecadcmd", await VersionAsync("freecadcmd", ["--version"], cancellationToken), false),
            new("kicad-cli", await VersionAsync("kicad-cli", ["version"], cancellationToken), false),
        };
        return new HostCapabilities(
            AgentId,
            Environment.MachineName,
            WorkspaceRoot,
            tools,
            _startedAt);
    }

    public async Task<HostCommandResult> ExecuteAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return request.Command switch
            {
                "probe" => new HostCommandResult(true, "ok"),
                "git.checkout" => await CheckoutAsync(request, cancellationToken),
                "git.commit" => await CommitAsync(request, cancellationToken),
                "gui.open" => OpenGui(request),
                _ => new HostCommandResult(false, $"unsupported host command '{request.Command}'"),
            };
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
        {
            return new HostCommandResult(false, exception.Message);
        }
    }

    private async Task<HostCommandResult> CheckoutAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken)
    {
        var path = WorkspacePath(request.WorkspaceKey);
        if (!Directory.Exists(Path.Combine(path, ".git")))
        {
            if (string.IsNullOrWhiteSpace(request.Repository))
                return new HostCommandResult(false, "repository is required for git.checkout");
            var args = new List<string> { "clone" };
            if (!string.IsNullOrWhiteSpace(request.Branch))
            {
                args.Add("--branch");
                args.Add(request.Branch);
                args.Add("--single-branch");
            }
            args.Add(request.Repository);
            args.Add(path);
            var clone = await _runner.RunAsync("git", args, null, cancellationToken);
            if (!clone.Success)
                return Failure("git clone failed", clone);
        }
        else
        {
            var fetch = await _runner.RunAsync("git", ["fetch", "--all", "--prune"], path, cancellationToken);
            if (!fetch.Success)
                return Failure("git fetch failed", fetch);
            if (!string.IsNullOrWhiteSpace(request.Branch))
            {
                var checkout = await _runner.RunAsync(
                    "git", ["checkout", "-B", request.Branch, $"origin/{request.Branch}"], path, cancellationToken);
                if (!checkout.Success)
                    return Failure("git checkout failed", checkout);
            }
        }

        return new HostCommandResult(true, "checked out", Path: path);
    }

    private async Task<HostCommandResult> CommitAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken)
    {
        var path = WorkspacePath(request.WorkspaceKey);
        if (!Directory.Exists(Path.Combine(path, ".git")))
            return new HostCommandResult(false, "workspace is not checked out");
        var branch = string.IsNullOrWhiteSpace(request.Branch) ? "main" : request.Branch.Trim();
        var message = string.IsNullOrWhiteSpace(request.Message) ? "EMD host commit" : request.Message;
        var name = string.IsNullOrWhiteSpace(request.AuthorName) ? "EMD Host" : request.AuthorName;
        var email = string.IsNullOrWhiteSpace(request.AuthorEmail) ? "emd@localhost" : request.AuthorEmail;

        var checkout = await _runner.RunAsync(
            "git", ["checkout", "-B", branch], path, cancellationToken);
        if (!checkout.Success)
            return Failure("git checkout failed", checkout);
        var add = await _runner.RunAsync("git", ["add", "-A"], path, cancellationToken);
        if (!add.Success)
            return Failure("git add failed", add);
        var commit = await _runner.RunAsync(
            "git",
            ["-c", $"user.name={name}", "-c", $"user.email={email}", "commit", "-m", message],
            path,
            cancellationToken);
        if (!commit.Success)
            return Failure("git commit failed", commit);
        var rev = await _runner.RunAsync("git", ["rev-parse", "HEAD"], path, cancellationToken);
        if (!rev.Success)
            return Failure("git rev-parse failed", rev);
        var push = await _runner.RunAsync(
            "git", ["push", "-u", "origin", branch], path, cancellationToken);
        if (!push.Success)
            return Failure("git push failed", push);
        return new HostCommandResult(true, "committed", Commit: rev.StdOut.Trim(), Path: path);
    }

    private HostCommandResult OpenGui(HostCommandRequest request)
    {
        var tool = request.Tool?.ToLowerInvariant();
        var executable = tool switch
        {
            "freecad" => "FreeCAD",
            "kicad" => "kicad",
            _ => null,
        };
        if (executable is null)
            return new HostCommandResult(false, "tool must be 'freecad' or 'kicad'");
        if (string.IsNullOrWhiteSpace(request.Path))
            return new HostCommandResult(false, "path is required");

        var path = Path.GetFullPath(request.Path);
        if (!WithinWorkspace(path))
            return new HostCommandResult(false, "path is outside the workspace root");
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
        };
        info.ArgumentList.Add(path);
        Process.Start(info);
        return new HostCommandResult(true, "opened", Path: path);
    }

    private string WorkspacePath(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || !WorkspaceKeyPattern.IsMatch(key))
            throw new ArgumentException("workspace key is invalid");
        var path = Path.GetFullPath(Path.Combine(WorkspaceRoot, key));
        if (!WithinWorkspace(path))
            throw new ArgumentException("workspace key escapes the workspace root");
        return path;
    }

    private bool WithinWorkspace(string path) =>
        path.StartsWith(WorkspaceRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
        path == WorkspaceRoot;

    private async Task<string?> VersionAsync(
        string file,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(file, args, null, cancellationToken);
        return result.Success ? result.StdOut.Trim() : null;
    }

    private static HostCommandResult Failure(string message, HostProcessResult process) =>
        new(false, $"{message}: {process.StdErr.Trim()}", Logs: [process.StdOut.Trim()]);
}
