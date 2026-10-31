using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Adapters;

/// <summary>Compiles an exported MechMaker machine.json through the MechMaker CLI.</summary>
public sealed class MechMakerCompileAdapter
{
    private readonly IToolProcessRunner _runner;
    private readonly string _executable;
    private readonly IReadOnlyList<string> _argumentsPrefix;
    private readonly TimeSpan _timeout;

    public MechMakerCompileAdapter(
        IToolProcessRunner runner,
        string executable = "dotnet",
        IReadOnlyList<string>? argumentsPrefix = null,
        TimeSpan? timeout = null)
    {
        _runner = runner;
        _executable = executable;
        _argumentsPrefix = argumentsPrefix ?? [];
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
    }

    public async Task<AdapterResult> CompileAsync(
        string machineJsonPath,
        string outputMjcfPath,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>(_argumentsPrefix)
        {
            machineJsonPath,
            outputMjcfPath
        };

        var run = await _runner.RunAsync(
            _executable,
            arguments,
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
                "EMD-MECHMAKER-010",
                $"MechMaker compilation failed with exit code {run.ExitCode}: {run.Stderr.Trim()}");
        }

        if (!File.Exists(outputMjcfPath))
        {
            return Failure(
                "EMD-MECHMAKER-011",
                $"MechMaker reported success but MJCF '{outputMjcfPath}' was not created.");
        }

        return new AdapterResult
        {
            Success = true,
            Artifacts =
            [
                new AdapterArtifact
                {
                    Path = Path.GetFullPath(outputMjcfPath),
                    Format = "mjcf",
                    Role = ArtifactRole.Simulation
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
