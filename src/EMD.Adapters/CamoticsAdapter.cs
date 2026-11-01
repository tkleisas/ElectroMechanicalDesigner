using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Adapters;

public sealed class CamoticsAdapter
{
    private readonly IToolProcessRunner _runner;
    private readonly TimeSpan _timeout;

    public CamoticsAdapter(IToolProcessRunner runner, TimeSpan? timeout = null)
    {
        _runner = runner;
        _timeout = timeout ?? TimeSpan.FromMinutes(10);
    }

    public async Task<AdapterResult> VerifyAsync(
        string gcodePath,
        string outputPath,
        string executable = "camotics",
        CancellationToken cancellationToken = default)
    {
        var run = await _runner.RunAsync(
            executable,
            [gcodePath, outputPath],
            stdin: null,
            timeout: _timeout,
            cancellationToken: cancellationToken);
        if (run.Diagnostics.Count > 0)
            return new AdapterResult { Success = false, Diagnostics = run.Diagnostics };
        if (run.ExitCode != 0)
        {
            return new AdapterResult
            {
                Success = false,
                Diagnostics =
                [
                    new Diagnostic(
                        "EMD-CAM-006",
                        DiagnosticSeverity.Error,
                        $"CAMotics failed with exit code {run.ExitCode}: {run.Stderr.Trim()}")
                ]
            };
        }

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            return new AdapterResult
            {
                Success = false,
                Diagnostics =
                [
                    new Diagnostic(
                        "EMD-CAM-007",
                        DiagnosticSeverity.Error,
                        $"CAMotics did not create '{outputPath}'.")
                ]
            };
        }

        return new AdapterResult
        {
            Success = true,
            Artifacts =
            [
                new AdapterArtifact
                {
                    Path = Path.GetFullPath(outputPath),
                    Format = "stl",
                    Role = ArtifactRole.Simulation
                }
            ]
        };
    }
}
