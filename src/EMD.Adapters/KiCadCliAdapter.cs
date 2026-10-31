using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Adapters;

/// <summary>Headless KiCad operations through <c>kicad-cli</c>.</summary>
public sealed class KiCadCliAdapter
{
    private readonly IToolProcessRunner _runner;
    private readonly string _executable;
    private readonly TimeSpan _timeout;

    public KiCadCliAdapter(
        IToolProcessRunner runner,
        string executable = "kicad-cli",
        TimeSpan? timeout = null)
    {
        _runner = runner;
        _executable = executable;
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
    }

    public Task<AdapterResult> RunErcAsync(
        string schematicPath,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ["sch", "erc", "--output", outputPath, schematicPath],
            outputPath,
            "kicad-erc",
            ArtifactRole.Report,
            cancellationToken);

    public Task<AdapterResult> RunDrcAsync(
        string pcbPath,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ["pcb", "drc", "--output", outputPath, pcbPath],
            outputPath,
            "kicad-drc",
            ArtifactRole.Report,
            cancellationToken);

    public Task<AdapterResult> ExportNetlistAsync(
        string schematicPath,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ["sch", "export", "netlist", "--format", "kicadxml", "--output", outputPath, schematicPath],
            outputPath,
            "netlist",
            ArtifactRole.Generated,
            cancellationToken);

    public Task<AdapterResult> ExportBomAsync(
        string schematicPath,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ["sch", "export", "bom", "--output", outputPath, schematicPath],
            outputPath,
            "bom",
            ArtifactRole.Manufacturing,
            cancellationToken);

    public Task<AdapterResult> ExportGerbersAsync(
        string pcbPath,
        string outputDirectory,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ["pcb", "export", "gerbers", "--output", outputDirectory, pcbPath],
            outputDirectory,
            "gerber",
            ArtifactRole.Manufacturing,
            cancellationToken);

    public Task<AdapterResult> ExportStepAsync(
        string pcbPath,
        string outputPath,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ["pcb", "export", "step", "--output", outputPath, pcbPath],
            outputPath,
            "step",
            ArtifactRole.Generated,
            cancellationToken);

    private async Task<AdapterResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        string outputPath,
        string fileFormat,
        ArtifactRole role,
        CancellationToken cancellationToken)
    {
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
                "EMD-KICAD-010",
                $"kicad-cli failed with exit code {run.ExitCode}: {run.Stderr.Trim()}");
        }

        if (File.Exists(outputPath))
        {
            return new AdapterResult
            {
                Success = true,
                Artifacts =
                [
                    new AdapterArtifact
                    {
                        Path = Path.GetFullPath(outputPath),
                        Format = fileFormat,
                        Role = role
                    }
                ]
            };
        }

        if (Directory.Exists(outputPath))
        {
            var files = Directory.GetFiles(outputPath)
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToList();

            if (files.Count == 0)
            {
                return Failure(
                    "EMD-KICAD-011",
                    $"kicad-cli reported success but '{outputPath}' contains no files.");
            }

            return new AdapterResult
            {
                Success = true,
                Artifacts = files.Select(file => new AdapterArtifact
                {
                    Path = Path.GetFullPath(file),
                    Format = FormatFor(file),
                    Role = role
                }).ToList()
            };
        }

        return Failure(
            "EMD-KICAD-011",
            $"kicad-cli reported success but output '{outputPath}' was not created.");
    }

    private static string FormatFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".gbr" => "gerber",
        ".drl" => "drill",
        ".gbrjob" => "gerber-job",
        _ => "file"
    };

    private static AdapterResult Failure(string code, string message) => new()
    {
        Success = false,
        Diagnostics = [new Diagnostic(code, DiagnosticSeverity.Error, message)]
    };
}
