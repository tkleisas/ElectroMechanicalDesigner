using Emd.Core.Diagnostics;
using Emd.Core.Projects;
using Emd.Core.Simulation;

namespace Emd.Core.Tests;

public class TraceAssertionEvaluatorTests
{
    [Fact]
    public void Passing_trace_has_no_errors()
    {
        var project = LoadExample();
        var trace = LoadTrace("time,x_carriage_m\n0,0\n1,0.0405\n");

        var result = TraceAssertionEvaluator.Default.Evaluate(project, trace, "move_40mm");

        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Failing_assertion_is_reported_as_EMD_TRACE_003()
    {
        var project = LoadExample();
        var trace = LoadTrace("time,x_carriage_m\n0,0\n1,0.050\n");

        var result = TraceAssertionEvaluator.Default.Evaluate(project, trace, "move_40mm");

        Assert.False(result.Passed);
        Assert.Contains(result.Diagnostics, d => d.Code == "EMD-TRACE-003");
    }

    [Fact]
    public void Unknown_signal_is_reported_as_EMD_TRACE_002()
    {
        var project = LoadExample();
        var trace = LoadTrace("time,unknown_signal\n0,0\n1,1\n");

        var result = TraceAssertionEvaluator.Default.Evaluate(project, trace, "move_40mm");

        Assert.Contains(result.Diagnostics, d => d.Code == "EMD-TRACE-002");
    }

    [Fact]
    public void Empty_trace_is_reported_as_EMD_TRACE_004()
    {
        var project = LoadExample();
        var trace = new SimulationTrace(["time"], []);

        var result = TraceAssertionEvaluator.Default.Evaluate(project, trace, "move_40mm");

        Assert.Contains(result.Diagnostics, d => d.Code == "EMD-TRACE-004");
    }

    [Fact]
    public void Missing_scenario_is_reported_as_EMD_TRACE_005()
    {
        var project = LoadExample();
        var trace = LoadTrace("time,x_carriage_m\n0,0\n1,0.0405\n");

        var result = TraceAssertionEvaluator.Default.Evaluate(project, trace, "missing");

        Assert.Contains(result.Diagnostics, d => d.Code == "EMD-TRACE-005");
    }

    [Fact]
    public void Malformed_csv_is_reported_by_the_loader()
    {
        var result = TraceCsvLoader.Default.Load("time,x\n0,not-a-number\n");

        Assert.Null(result.Trace);
        Assert.Contains(result.Diagnostics, d => d.Code == "EMD-TRACE-001");
    }

    private static Emd.Core.Model.EmdProject LoadExample()
    {
        var loaded = ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        return loaded.Project!;
    }

    private static SimulationTrace LoadTrace(string csv)
    {
        var loaded = TraceCsvLoader.Default.Load(csv, "trace.csv");
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        return loaded.Trace!;
    }
}
