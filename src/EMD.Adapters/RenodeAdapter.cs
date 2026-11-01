using System.Text;
using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Adapters;

public sealed class RenodeAdapter
{
    private readonly IToolProcessRunner _runner;
    private readonly string _executable;
    private readonly TimeSpan _timeout;

    public RenodeAdapter(
        IToolProcessRunner runner,
        string executable = "renode",
        TimeSpan? timeout = null)
    {
        _runner = runner;
        _executable = executable;
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    public async Task<AdapterResult> RunAsync(
        string firmwarePath,
        string platformPath,
        string outputLogPath,
        double durationSeconds = 2,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(firmwarePath))
            return Failure("EMD-RENODE-001", $"Firmware '{firmwarePath}' was not found.");
        if (!File.Exists(platformPath))
            return Failure("EMD-RENODE-001", $"Renode platform '{platformPath}' was not found.");
        if (durationSeconds <= 0)
            return Failure("EMD-RENODE-001", "Renode duration must be positive.");

        var fullFirmware = Path.GetFullPath(firmwarePath);
        var fullPlatform = Path.GetFullPath(platformPath);
        var script = $"""
            :name: emd-firmware
            mach create
            machine LoadPlatformDescription @{fullPlatform}
            sysbus LoadELF @{fullFirmware}
            emulation RunFor "00:00:{durationSeconds:0.###}"
            quit
            """;
        var scriptPath = Path.Combine(
            Path.GetTempPath(), $"emd-renode-{Guid.NewGuid():N}.resc");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

        try
        {
            var run = await _runner.RunAsync(
                _executable,
                ["--console", "--disable-gui", scriptPath],
                stdin: null,
                timeout: _timeout,
                cancellationToken: cancellationToken);
            var directory = Path.GetDirectoryName(Path.GetFullPath(outputLogPath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(
                outputLogPath,
                run.Stdout + (run.Stderr.Length == 0 ? "" : Environment.NewLine + run.Stderr));

            if (run.Diagnostics.Count > 0)
                return new AdapterResult { Success = false, Diagnostics = run.Diagnostics };
            if (run.ExitCode != 0)
                return Failure(
                    "EMD-RENODE-001",
                    $"Renode failed with exit code {run.ExitCode}: {run.Stderr.Trim()}");

            return new AdapterResult
            {
                Success = true,
                Artifacts =
                [
                    new AdapterArtifact
                    {
                        Path = Path.GetFullPath(outputLogPath),
                        Format = "renode-log",
                        Role = ArtifactRole.Report
                    }
                ]
            };
        }
        finally
        {
            try { File.Delete(scriptPath); } catch (IOException) { }
        }
    }

    private static AdapterResult Failure(string code, string message) => new()
    {
        Success = false,
        Diagnostics = [new Diagnostic(code, DiagnosticSeverity.Error, message)]
    };
}
