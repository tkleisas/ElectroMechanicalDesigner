using System.Text.Json.Nodes;
using Emd.Adapters;
using Emd.Cli;
using Emd.Core.Model;
using Emd.Core.Projects;

namespace Emd.Core.Tests;

public class CamCommandTests
{
    [Fact]
    public void Verify_runs_builtin_checks()
    {
        using var temp = new TempDirectory();
        var gcode = Path.Combine(temp.Path, "part.gcode");
        File.WriteAllText(gcode, "G21\nG1 X5 Y5 F500\n");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = CamCommand.Run(
            ["verify", "--gcode", gcode, "--envelope", "0,0,0,10,10,10"],
            stdout, stderr, new CamoticsAdapter(new Runner(null)));

        Assert.Equal(0, exitCode);
        Assert.Contains("cam verification: valid", stdout.ToString());
    }

    [Fact]
    public void Verify_rejects_out_of_envelope_paths()
    {
        using var temp = new TempDirectory();
        var gcode = Path.Combine(temp.Path, "part.gcode");
        File.WriteAllText(gcode, "G21\nG1 X20 F500\n");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = CamCommand.Run(
            ["verify", "--gcode", gcode, "--envelope", "0,0,0,10,10,10"],
            stdout, stderr, new CamoticsAdapter(new Runner(null)));

        Assert.Equal(1, exitCode);
        Assert.Contains("EMD-CAM-001", stderr.ToString());
    }

    [Fact]
    public void Simulate_reports_gouges_and_remaining_material()
    {
        using var temp = new TempDirectory();
        var gcode = Path.Combine(temp.Path, "part.gcode");
        var target = Path.Combine(temp.Path, "part.stl");
        var reportPath = Path.Combine(temp.Path, "report.json");
        File.WriteAllText(gcode, "G21\nG90\nG0 X5 Y5 Z10 F1000\nG1 Z3 F500\n");
        File.WriteAllText(target, """
            solid top
            facet normal 0 0 1
              outer loop
                vertex 0 0 5
                vertex 10 0 5
                vertex 10 10 5
              endloop
            endfacet
            facet normal 0 0 1
              outer loop
                vertex 0 0 5
                vertex 10 10 5
                vertex 0 10 5
              endloop
            endfacet
            endsolid top
            """);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = CamCommand.Run(
            ["simulate", "--gcode", gcode, "--target", target,
             "--stock", "0,0,0,10,10,10", "--tool-diameter", "40", "--cell", "1",
             "--out", reportPath],
            stdout, stderr, new CamoticsAdapter(new Runner(null)));

        Assert.Equal(1, exitCode);
        Assert.True(File.Exists(reportPath));
        var report = File.ReadAllText(reportPath);
        Assert.Contains("\"gouge_cells\":100", report);
        Assert.Contains("\"max_gouge_mm\":2", report);
    }

    [Fact]
    public void Verify_uses_path_job_stock_from_the_project()
    {
        using var temp = new TempDirectory();
        var gcode = Path.Combine(temp.Path, "part.gcode");
        var projectPath = Path.Combine(temp.Path, "project.json");
        File.WriteAllText(gcode, "G21\nG1 X20 F500\n");
        var project = new EmdProject
        {
            Id = Guid.NewGuid(),
            Name = "CAM project",
            Parts = [new Part { Id = Guid.NewGuid(), Name = "frame", Kind = PartKind.Component, Ports = [] }],
            Manufacturing = new ManufacturingSection
            {
                Processes =
                [
                    new ManufacturingProcess
                    {
                        Id = Guid.NewGuid(),
                        Key = "path_job",
                        Kind = ProcessKind.Cnc,
                        Parameters = new JsonObject
                        {
                            ["stock_min_mm"] = "0,0,0",
                            ["stock_max_mm"] = "10,10,10"
                        }
                    }
                ]
            }
        };
        ProjectWriter.Save(project, projectPath);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = CamCommand.Run(
            ["verify", "--gcode", gcode, "--project", projectPath],
            stdout, stderr, new CamoticsAdapter(new Runner(null)));

        Assert.Equal(1, exitCode);
        Assert.Contains("EMD-CAM-001", stderr.ToString());
    }

    private sealed class Runner : IToolProcessRunner
    {
        private readonly string? _output;
        public Runner(string? output) { _output = output; }
        public Task<ToolProcessResult> RunAsync(
            string executable, IEnumerable<string>? arguments = null, string? stdin = null,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            if (_output is not null)
                File.WriteAllText(_output, "solid");
            return Task.FromResult(new ToolProcessResult(0, "", "", []));
        }
    }
}
