using System.Text.RegularExpressions;
using Emd.Adapters;
using Emd.Cli;

namespace Emd.Core.Tests;

public class SpiceFmuCommandTests
{
    [Fact]
    public void Run_builds_a_spice_trace_fmu()
    {
        using var temp = new TempDirectory();
        var netlist = Path.Combine(temp.Path, "rc.cir");
        File.WriteAllText(netlist, "* rc\nV1 in 0 1\nR1 in out 1k\nC1 out 0 1u\n");
        var output = Path.Combine(temp.Path, "rc.fmu");
        var runner = new FakeRunner();

        var (exitCode, stdout, stderr) = Run(
            ["--netlist", netlist, "--out", output, "--signals", "v(out)"], runner);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(output), stderr);
        Assert.Contains("exported SPICE FMU", stdout);
        Assert.Contains(".control", runner.Wrapper!);
        Assert.Contains("tran 0.001 1", runner.Wrapper);
        Assert.Contains("wrdata", runner.Wrapper);
        Assert.Contains("pythonfmu", string.Join(' ', runner.PythonArguments!));
    }

    [Fact]
    public void Missing_signals_is_a_usage_error()
    {
        using var temp = new TempDirectory();
        var netlist = Path.Combine(temp.Path, "rc.cir");
        File.WriteAllText(netlist, "* rc");

        var (exitCode, _, stderr) = Run(
            ["--netlist", netlist, "--out", Path.Combine(temp.Path, "rc.fmu")],
            new FakeRunner());

        Assert.Equal(2, exitCode);
        Assert.Contains("--signals", stderr);
    }

    [Fact]
    public void Ngspice_failure_returns_one()
    {
        using var temp = new TempDirectory();
        var netlist = Path.Combine(temp.Path, "rc.cir");
        File.WriteAllText(netlist, "* rc");

        var (exitCode, _, stderr) = Run(
            ["--netlist", netlist, "--out", Path.Combine(temp.Path, "rc.fmu"),
             "--signals", "v(out)"],
            new FakeRunner(ngspiceExitCode: 2));

        Assert.Equal(1, exitCode);
        Assert.Contains("ngspice failed", stderr);
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(
        string[] args, IToolProcessRunner runner)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = SpiceFmuCommand.Run(args, stdout, stderr, runner);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed class FakeRunner : IToolProcessRunner
    {
        private readonly int _ngspiceExitCode;

        public FakeRunner(int ngspiceExitCode = 0)
        {
            _ngspiceExitCode = ngspiceExitCode;
        }

        public string? Wrapper { get; private set; }

        public string[]? PythonArguments { get; private set; }

        public Task<ToolProcessResult> RunAsync(
            string executable,
            IEnumerable<string>? arguments = null,
            string? stdin = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var values = (arguments ?? []).ToList();
            if (executable == "ngspice")
            {
                if (_ngspiceExitCode != 0)
                    return Task.FromResult(new ToolProcessResult(
                        _ngspiceExitCode, "", "ngspice failed", []));

                Wrapper = File.ReadAllText(values[^1]);
                var logPath = values[values.IndexOf("-o") + 1];
                File.WriteAllText(logPath, "ok");
                var match = Regex.Match(Wrapper, @"wrdata\s+(?:""([^""]+)""|(\S+))");
                var csvPath = match.Groups[1].Success
                    ? match.Groups[1].Value
                    : match.Groups[2].Value;
                File.WriteAllText(csvPath, "time v(out)\n0 1\n0.5 2\n1 3\n");
                return Task.FromResult(new ToolProcessResult(0, "", "", []));
            }

            PythonArguments = values.ToArray();
            var destination = values[values.IndexOf("--dest") + 1];
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "SpiceTraceSlave.fmu"), "fmu");
            return Task.FromResult(new ToolProcessResult(0, "", "", []));
        }
    }
}
