using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Adapters;

/// <summary>Runs a SPICE netlist through ngspice in batch mode.</summary>
public sealed class NgSpiceAdapter
{
    private readonly IToolProcessRunner _runner;
    private readonly string _executable;
    private readonly TimeSpan _timeout;

    public NgSpiceAdapter(
        IToolProcessRunner runner,
        string executable = "ngspice",
        TimeSpan? timeout = null)
    {
        _runner = runner;
        _executable = executable;
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
    }

    public async Task<AdapterResult> RunAsync(
        string netlistPath,
        string outputLogPath,
        CancellationToken cancellationToken = default)
    {
        var run = await _runner.RunAsync(
            _executable,
            ["-b", "-o", outputLogPath, netlistPath],
            stdin: null,
            timeout: _timeout,
            cancellationToken: cancellationToken);

        if (run.Diagnostics.Count > 0)
        {
            return new AdapterResult
            {
                Success = false,
                Diagnostics = run.Diagnostics
            };
        }

        if (run.ExitCode != 0)
        {
            return Failure(
                "EMD-SPICE-001",
                $"ngspice failed with exit code {run.ExitCode}: {run.Stderr.Trim()}");
        }

        if (!File.Exists(outputLogPath))
        {
            return Failure(
                "EMD-SPICE-002",
                $"ngspice reported success but log '{outputLogPath}' was not created.");
        }

        return new AdapterResult
        {
            Success = true,
            Artifacts =
            [
                new AdapterArtifact
                {
                    Path = Path.GetFullPath(outputLogPath),
                    Format = "spice-log",
                    Role = ArtifactRole.Report
                }
            ]
        };
    }

    private static AdapterResult Failure(string code, string message) => new()
    {
        Success = false,
        Diagnostics = [new Diagnostic(code, DiagnosticSeverity.Error, message)]
    };
}
