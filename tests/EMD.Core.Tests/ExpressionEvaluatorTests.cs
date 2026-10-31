using Emd.Core.Simulation;

namespace Emd.Core.Tests;

public class ExpressionEvaluatorTests
{
    [Fact]
    public void Evaluates_abs_comparison()
    {
        var result = ExpressionEvaluator.Evaluate(
            "abs(x - 0.040) < 0.001",
            new Dictionary<string, double> { ["x"] = 0.0405 });

        Assert.True(result.Success);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluates_precedence()
    {
        var result = ExpressionEvaluator.Evaluate(
            "1 + 2 * 3 == 7",
            new Dictionary<string, double>());

        Assert.True(result.Success);
        Assert.True(result.Value);
    }

    [Fact]
    public void Unknown_signal_is_reported()
    {
        var result = ExpressionEvaluator.Evaluate(
            "missing > 1",
            new Dictionary<string, double> { ["x"] = 1 });

        Assert.False(result.Success);
        Assert.Equal("missing", result.UnknownSignal);
    }

    [Fact]
    public void Malformed_expression_is_reported()
    {
        var result = ExpressionEvaluator.Evaluate(
            "1 +",
            new Dictionary<string, double>());

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
