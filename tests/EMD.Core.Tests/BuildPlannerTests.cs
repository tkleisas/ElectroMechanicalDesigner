using Emd.Core.Build;

namespace Emd.Core.Tests;

public class BuildPlannerTests
{
    [Fact]
    public void Topological_order_respects_inputs()
    {
        var plan = BuildPlanner.Default.Plan(
        [
            new BuildNode("c", "C", ["b"]),
            new BuildNode("a", "A", []),
            new BuildNode("b", "B", ["a"])
        ]);

        Assert.Empty(plan.Diagnostics);
        Assert.Equal(["a", "b", "c"], plan.Order.Select(node => node.Id));
    }

    [Fact]
    public void Cycle_is_reported_as_EMD_BUILD_001()
    {
        var plan = BuildPlanner.Default.Plan(
        [
            new BuildNode("a", "A", ["b"]),
            new BuildNode("b", "B", ["a"])
        ]);

        Assert.Contains(plan.Diagnostics, d => d.Code == "EMD-BUILD-001");
        Assert.Empty(plan.Order);
    }

    [Fact]
    public void Missing_input_is_reported_as_EMD_BUILD_002()
    {
        var plan = BuildPlanner.Default.Plan(
        [
            new BuildNode("a", "A", ["missing"])
        ]);

        Assert.Contains(plan.Diagnostics, d => d.Code == "EMD-BUILD-002");
    }

    [Fact]
    public void Duplicate_node_id_is_reported_as_EMD_BUILD_003()
    {
        var plan = BuildPlanner.Default.Plan(
        [
            new BuildNode("a", "A", []),
            new BuildNode("a", "A again", [])
        ]);

        Assert.Contains(plan.Diagnostics, d => d.Code == "EMD-BUILD-003");
    }

    [Fact]
    public void Independent_nodes_keep_input_order()
    {
        var plan = BuildPlanner.Default.Plan(
        [
            new BuildNode("first", "First", []),
            new BuildNode("second", "Second", []),
            new BuildNode("third", "Third", [])
        ]);

        Assert.Equal(["first", "second", "third"], plan.Order.Select(node => node.Id));
    }
}
