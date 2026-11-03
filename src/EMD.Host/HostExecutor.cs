using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
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
                "gui.export" => await ExportAsync(request, cancellationToken),
                "script.run" => await RunScriptAsync(request, cancellationToken),
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

    private async Task<HostCommandResult> ExportAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.OutputPath))
            return new HostCommandResult(false, "path and outputPath are required");
        var input = ResolveWorkspacePath(request.Path);
        var output = ResolveWorkspacePath(request.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? WorkspaceRoot);
        var format = (request.Format ?? Path.GetExtension(output).TrimStart('.')).ToLowerInvariant();
        var tool = request.Tool?.ToLowerInvariant();

        HostProcessResult result;
        if (tool == "freecad")
            result = await ExportFreeCadAsync(input, output, format, cancellationToken);
        else if (tool == "kicad-cli" || tool == "kicad")
            result = await ExportKiCadAsync(input, output, format, cancellationToken);
        else
            return new HostCommandResult(false, "tool must be 'freecad' or 'kicad-cli'");

        if (!result.Success)
            return Failure($"{tool} export failed", result);
        if (!File.Exists(output))
            return new HostCommandResult(false, $"{tool} did not produce {output}");
        var bytes = await File.ReadAllBytesAsync(output, cancellationToken);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new HostCommandResult(
            true,
            "exported",
            Path: output,
            Artifacts: [new HostArtifact(output, sha, bytes.LongLength)],
            Logs: [result.StdOut.Trim()]);
    }

    private async Task<HostProcessResult> ExportFreeCadAsync(
        string input,
        string output,
        string format,
        CancellationToken cancellationToken)
    {
        var script = Path.Combine(Path.GetTempPath(), $"emd-export-{Guid.NewGuid():N}.py");
        var exporter = format is "stl" or "mesh" ? "Mesh.export" : "Import.export";
        var scriptText = $"""
            import FreeCAD
            {exporter.Split('.')[0]}
            doc = FreeCAD.openDocument({JsonSerializer.Serialize(input)})
            {exporter}(doc.Objects, {JsonSerializer.Serialize(output)})
            """;
        await File.WriteAllTextAsync(script, scriptText, cancellationToken);
        try
        {
            return await _runner.RunAsync("freecadcmd", [script], null, cancellationToken);
        }
        finally
        {
            try { File.Delete(script); } catch (IOException) { }
        }
    }

    private Task<HostProcessResult> ExportKiCadAsync(
        string input,
        string output,
        string format,
        CancellationToken cancellationToken)
    {
        var args = format switch
        {
            "step" => new List<string> { "pcb", "export", "step", "-o", output, input },
            "gerbers" => new List<string> { "pcb", "export", "gerbers", "-o", output, input },
            "drc" => new List<string> { "pcb", "drc", "-o", output, input },
            "erc" => new List<string> { "sch", "erc", "-o", output, input },
            "bom" => new List<string> { "sch", "export", "bom", "-o", output, input },
            "netlist" => new List<string> { "sch", "export", "netlist", "-o", output, input },
            _ => [],
        };
        return args.Count == 0
            ? Task.FromResult(new HostProcessResult(-1, "", $"unsupported KiCad format '{format}'"))
            : _runner.RunAsync("kicad-cli", args, null, cancellationToken);
    }

    private async Task<HostCommandResult> RunScriptAsync(
        HostCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return new HostCommandResult(false, "path is required");
        var script = ResolveWorkspacePath(request.Path);
        var scriptsRoot = Path.Combine(WorkspaceRoot, ".emd", "scripts");
        if (!script.StartsWith(Path.GetFullPath(scriptsRoot) + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            return new HostCommandResult(false, "scripts must live under .emd/scripts");
        var tool = request.Tool?.ToLowerInvariant() switch
        {
            "freecadcmd" => "freecadcmd",
            "python3" => "python3",
            _ => null,
        };
        if (tool is null)
            return new HostCommandResult(false, "tool must be 'freecadcmd' or 'python3'");
        var result = await _runner.RunAsync(tool, [script], WorkspaceRoot, cancellationToken);
        return result.Success
            ? new HostCommandResult(true, "script completed", Logs: [result.StdOut.Trim()])
            : Failure($"{tool} script failed", result);
    }

    private string ResolveWorkspacePath(string path)
    {
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(WorkspaceRoot, path));
        if (!WithinWorkspace(full))
            throw new ArgumentException("path is outside the workspace root");
        return full;
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
