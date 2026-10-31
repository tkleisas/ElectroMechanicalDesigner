using System.ComponentModel;
using System.Diagnostics;
using Emd.Core.Diagnostics;

namespace Emd.Adapters;

public sealed record ToolProcessResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Succeeded => Diagnostics.Count == 0 && ExitCode == 0;
}

/// <summary>
/// Runs a pinned external tool as a child process. All adapters use this seam so
/// timeouts, cancellation, argument boundaries, and stderr capture are consistent.
/// </summary>
public sealed class ProcessToolHost : IToolProcessRunner
{
    public static ProcessToolHost Default { get; } = new();

    public async Task<ToolProcessResult> RunAsync(
        string executable,
        IEnumerable<string>? arguments = null,
        string? stdin = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return Failure("EMD-ADAPTER-001", "Executable path is empty.");

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        if (effectiveTimeout <= TimeSpan.Zero)
            return Failure("EMD-ADAPTER-001", "Timeout must be positive.", executable);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments ?? [])
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
                return Failure("EMD-ADAPTER-001", $"Could not start tool '{executable}'.", executable);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return Failure("EMD-ADAPTER-001", $"Could not start tool '{executable}': {exception.Message}", executable);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            if (stdin is not null)
                await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken);
        }
        catch (IOException)
        {
            // The process may exit without reading stdin; its exit code and output still matter.
        }
        finally
        {
            process.StandardInput.Close();
        }

        using var timeoutSource = new CancellationTokenSource(effectiveTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await KillAsync(process);
            return new ToolProcessResult(
                -1,
                await SafeReadAsync(stdoutTask),
                await SafeReadAsync(stderrTask),
                [
                    new Diagnostic(
                        "EMD-ADAPTER-002",
                        DiagnosticSeverity.Error,
                        $"Tool '{executable}' timed out after {effectiveTimeout.TotalMilliseconds:0} ms.",
                        Entity: executable)
                ]);
        }
        catch (OperationCanceledException)
        {
            await KillAsync(process);
            throw;
        }

        return new ToolProcessResult(
            process.ExitCode,
            await SafeReadAsync(stdoutTask),
            await SafeReadAsync(stderrTask),
            []);
    }

    private static async Task KillAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            // Best effort: the process may have exited between the check and the kill.
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            // Best effort cleanup only.
        }
    }

    private static async Task<string> SafeReadAsync(Task<string> readTask)
    {
        try
        {
            return await readTask;
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            return "";
        }
    }

    private static ToolProcessResult Failure(string code, string message, string? entity = null) =>
        new(-1, "", "",
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message, Entity: entity)
        ]);
}
