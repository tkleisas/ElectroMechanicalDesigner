using Emd.Core.Diagnostics;

namespace Emd.Core.Build;

public sealed record BuildNode(string Id, string Name, IReadOnlyList<string> Inputs);

public sealed record BuildPlan(IReadOnlyList<BuildNode> Order, IReadOnlyList<Diagnostic> Diagnostics);

public sealed class BuildPlanner
{
    public static BuildPlanner Default { get; } = new();

    public BuildPlan Plan(IEnumerable<BuildNode> nodes)
    {
        var diagnostics = new List<Diagnostic>();
        var byId = new Dictionary<string, BuildNode>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (!byId.TryAdd(node.Id, node))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-BUILD-003",
                    DiagnosticSeverity.Error,
                    $"Build graph declares node '{node.Id}' more than once.",
                    Entity: node.Id));
            }
        }

        var indegree = byId.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var dependents = byId.Keys.ToDictionary(
            id => id,
            _ => new List<string>(),
            StringComparer.Ordinal);

        foreach (var node in byId.Values)
        {
            foreach (var input in node.Inputs.Distinct(StringComparer.Ordinal))
            {
                if (!byId.ContainsKey(input))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-BUILD-002",
                        DiagnosticSeverity.Error,
                        $"Build node '{node.Id}' references missing input '{input}'.",
                        Entity: node.Id));
                    continue;
                }

                indegree[node.Id]++;
                dependents[input].Add(node.Id);
            }
        }

        var ready = new List<string>(byId.Keys.Where(id => indegree[id] == 0));
        var order = new List<BuildNode>();

        while (ready.Count > 0)
        {
            var id = ready[0];
            ready.RemoveAt(0);
            order.Add(byId[id]);

            foreach (var dependent in dependents[id])
            {
                indegree[dependent]--;
                if (indegree[dependent] == 0)
                    ready.Add(dependent);
            }
        }

        if (order.Count != byId.Count)
        {
            var ordered = order.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            var remaining = byId.Keys.Where(id => !ordered.Contains(id));
            diagnostics.Add(new Diagnostic(
                "EMD-BUILD-001",
                DiagnosticSeverity.Error,
                $"Build graph contains a cycle among: {string.Join(", ", remaining)}."));
        }

        return new BuildPlan(order, diagnostics);
    }
}
