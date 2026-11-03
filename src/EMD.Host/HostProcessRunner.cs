using System.Diagnostics;

namespace Emd.Host;

public sealed record HostProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

public interface IHostProcessRunner
{
    Task<HostProcessResult> RunAsync(
        string file,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default);
}

public sealed class HostProcessRunner : IHostProcessRunner
{
    public async Task<HostProcessResult> RunAsync(
        string file,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {file}.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new HostProcessResult(process.ExitCode, await stdout, await stderr);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HostProcessResult(-1, "", exception.Message);
        }
    }
}
