using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Adapters;

public sealed class SlicerAdapter
{
    private readonly IToolProcessRunner _runner;
    private readonly TimeSpan _timeout;

    public SlicerAdapter(IToolProcessRunner runner, TimeSpan? timeout = null)
    {
        _runner = runner;
        _timeout = timeout ?? TimeSpan.FromMinutes(30);
    }

    public async Task<AdapterResult> SliceAsync(
        string modelPath,
        string outputPath,
        string flavor,
        IReadOnlyList<string>? profiles = null,
        CancellationToken cancellationToken = default)
    {
        var executable = flavor.ToLowerInvariant() switch
        {
            "orca" or "orcaslicer" => "orca-slicer",
            "prusa" or "prusaslicer" => "prusa-slicer",
            _ => null
        };
        if (executable is null)
            return Failure("EMD-SLICER-001", $"Unknown slicer flavor '{flavor}'.");

        var arguments = new List<string> { "--export-gcode", "--output", outputPath };
        foreach (var profile in profiles ?? [])
            arguments.AddRange(["--load", profile]);
        arguments.Add(modelPath);

        var run = await _runner.RunAsync(
            executable,
            arguments,
            stdin: null,
            timeout: _timeout,
            cancellationToken: cancellationToken);
        if (run.Diagnostics.Count > 0)
            return new AdapterResult { Success = false, Diagnostics = run.Diagnostics };
        if (run.ExitCode != 0)
            return Failure(
                "EMD-SLICER-001",
                $"{executable} failed with exit code {run.ExitCode}: {run.Stderr.Trim()}");
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            return Failure(
                "EMD-SLICER-002",
                $"{executable} reported success but '{outputPath}' was not created.");

        return new AdapterResult
        {
            Success = true,
            Artifacts =
            [
                new AdapterArtifact
                {
                    Path = Path.GetFullPath(outputPath),
                    Format = "gcode",
                    Role = ArtifactRole.Manufacturing
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
