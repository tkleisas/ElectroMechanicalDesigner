using Emd.Adapters.MechMaker;
using Emd.Cli;
using Emd.Core.Model;
using Emd.Core.Projects;

namespace Emd.Core.Tests;

public class MechMakerSimulateCommandTests
{
    [Fact]
    public async Task Simulate_command_writes_a_trace()
    {
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "trace.csv");
        var client = new FakeClient();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await MechMakerSimulateCommand.RunAsync(
            ["mechmaker", "--machine", "machine.json", "--out", output,
             "--duration", "0.2", "--interval", "0.1", "--motor", "x_motor:1"],
            stdout,
            stderr,
            client);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(output));
        Assert.Contains("simulated:", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(output);
        Assert.Equal(3, lines.Length);
        Assert.Contains("x_motor.actual_rev", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Project_scenario_is_checked_against_the_recorded_trace()
    {
        using var temp = new TempDirectory();
        var projectPath = Path.Combine(temp.Path, "project.json");
        ProjectWriter.Save(
            ScenarioProject("abs(x_motor.actual_rev - 0.3) < 0.01"),
            projectPath);
        var output = Path.Combine(temp.Path, "trace.csv");
        var workDir = Path.Combine(temp.Path, "work");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await MechMakerSimulateCommand.RunAsync(
            ["mechmaker", "--project", projectPath, "--out", output,
             "--duration", "0.3", "--interval", "0.1", "--motor", "x_motor:1",
             "--scenario", "run", "--work-dir", workDir],
            stdout,
            stderr,
            new FakeClient());

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(output));
        Assert.True(File.Exists(Path.Combine(workDir, "machine.json")));
        Assert.Contains("scenario 'run' passed", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Failing_project_scenario_returns_nonzero()
    {
        using var temp = new TempDirectory();
        var projectPath = Path.Combine(temp.Path, "project.json");
        ProjectWriter.Save(
            ScenarioProject("abs(x_motor.actual_rev - 0.5) < 0.01"),
            projectPath);
        var output = Path.Combine(temp.Path, "trace.csv");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await MechMakerSimulateCommand.RunAsync(
            ["mechmaker", "--project", projectPath, "--out", output,
             "--duration", "0.3", "--interval", "0.1", "--motor", "x_motor:1",
             "--scenario", "run"],
            stdout,
            stderr,
            new FakeClient());

        Assert.Equal(1, exitCode);
        Assert.Contains("EMD-TRACE-003", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_machine_returns_usage_error()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await MechMakerSimulateCommand.RunAsync(
            ["mechmaker", "--out", "trace.csv"], stdout, stderr, new FakeClient());

        Assert.Equal(2, exitCode);
    }

    private static EmdProject ScenarioProject(string expression) => new()
    {
        Id = Guid.NewGuid(),
        Name = "scenario simulation",
        Parts =
        [
            new Part
            {
                Id = Guid.NewGuid(),
                Key = "x_motor",
                Name = "X motor",
                Kind = PartKind.Component,
                CatalogRef = new PartCatalogRef
                {
                    Catalog = "mechmaker",
                    Part = "nema17_stepper"
                },
                Ports = []
            }
        ],
        Simulation = new SimulationSection
        {
            Scenarios =
            [
                new Scenario
                {
                    Id = Guid.NewGuid(),
                    Key = "run",
                    Name = "Run",
                    Assertions =
                    [
                        new Assertion
                        {
                            Name = "motor reaches the target",
                            Expression = expression
                        }
                    ]
                }
            ]
        }
    };

    private sealed class FakeClient : IMechMakerToolClient
    {
        private int _runs;

        public Task<MechMakerToolResult> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? arguments = null,
            CancellationToken cancellationToken = default)
        {
            if (toolName != "run_for")
                return Task.FromResult(new MechMakerToolResult(false, "ok"));

            _runs++;
            var time = _runs * 0.1;
            var status =
                $"t = {time:0.###} s\n" +
                $"x_motor: commanded {time:0.###} rev / actual {time:0.###} rev, " +
                "velocity 1 rev/s (target 1 rev/s), missed steps 0\n" +
                "endstop x_carriage: open";
            return Task.FromResult(new MechMakerToolResult(false, status));
        }
    }
}
