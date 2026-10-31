using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Core.Simulation;

public sealed record TraceEvaluationResult(
    bool Passed,
    IReadOnlyList<Diagnostic> Diagnostics);

public sealed class TraceAssertionEvaluator
{
    public static TraceAssertionEvaluator Default { get; } = new();

    public TraceEvaluationResult Evaluate(
        EmdProject project,
        SimulationTrace trace,
        string? scenarioKey = null)
    {
        var diagnostics = new List<Diagnostic>();
        if (trace.Rows.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-TRACE-004",
                DiagnosticSeverity.Error,
                "Trace has no data rows."));
            return new TraceEvaluationResult(false, diagnostics);
        }

        var scenarios = project.Simulation?.Scenarios ?? [];
        List<Scenario> selected;
        if (!string.IsNullOrWhiteSpace(scenarioKey))
        {
            selected = scenarios
                .Where(scenario => scenario.Key == scenarioKey || scenario.Id.ToString() == scenarioKey)
                .ToList();
            if (selected.Count == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-TRACE-005",
                    DiagnosticSeverity.Error,
                    $"Scenario '{scenarioKey}' was not found.",
                    Entity: scenarioKey));
                return new TraceEvaluationResult(false, diagnostics);
            }
        }
        else
        {
            selected = scenarios;
        }

        var checkedScenarios = 0;
        foreach (var scenario in selected)
        {
            if (scenario.Assertions is not { Count: > 0 })
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-TRACE-006",
                    DiagnosticSeverity.Warning,
                    $"Scenario '{scenario.Key}' has no assertions.",
                    Entity: scenario.Key));
                continue;
            }

            checkedScenarios++;
            foreach (var assertion in scenario.Assertions)
            {
                if (string.IsNullOrWhiteSpace(assertion.Expression))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-TRACE-001",
                        DiagnosticSeverity.Error,
                        $"Scenario '{scenario.Key}' assertion '{assertion.Name}' has no expression.",
                        Entity: assertion.Name));
                    continue;
                }

                var evaluation = ExpressionEvaluator.Evaluate(assertion.Expression, trace.Rows[^1]);
                if (!evaluation.Success)
                {
                    diagnostics.Add(new Diagnostic(
                        evaluation.UnknownSignal is not null ? "EMD-TRACE-002" : "EMD-TRACE-001",
                        DiagnosticSeverity.Error,
                        $"Scenario '{scenario.Key}' assertion '{assertion.Name}' could not be evaluated: {evaluation.Error}",
                        Entity: assertion.Name));
                    continue;
                }

                if (!evaluation.Value)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-TRACE-003",
                        Severity(assertion.Severity),
                        $"Scenario '{scenario.Key}' assertion '{assertion.Name}' failed: {assertion.Expression}",
                        Entity: assertion.Name));
                }
            }
        }

        if (checkedScenarios == 0 && diagnostics.All(diagnostic => diagnostic.Code != "EMD-TRACE-006"))
        {
            diagnostics.Add(new Diagnostic(
                "EMD-TRACE-006",
                DiagnosticSeverity.Warning,
                "No scenario assertions were evaluated."));
        }

        var passed = diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
        return new TraceEvaluationResult(passed, diagnostics);
    }

    private static DiagnosticSeverity Severity(string? severity) => severity switch
    {
        "warning" => DiagnosticSeverity.Warning,
        "info" => DiagnosticSeverity.Info,
        _ => DiagnosticSeverity.Error
    };
}
