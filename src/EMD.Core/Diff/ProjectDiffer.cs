using System.Text.Json;
using Emd.Core.Model;

namespace Emd.Core.Diff;

public sealed record ProjectChange(
    string Kind,
    string EntityType,
    string Id,
    string? Key,
    string? Before,
    string? After);

public sealed record ProjectDiff(IReadOnlyList<ProjectChange> Changes)
{
    public bool HasChanges => Changes.Count > 0;
}

public sealed class ProjectDiffer
{
    public static ProjectDiffer Default { get; } = new();

    public ProjectDiff Compare(EmdProject before, EmdProject after)
    {
        var changes = new List<ProjectChange>();

        CompareCollection("part", before.Parts, after.Parts,
            part => part.Id, part => part.Key, SummarizePart);
        CompareCollection("connection", before.Connections, after.Connections,
            connection => connection.Id, connection => connection.Key, connection => connection.Kind.ToString());
        CompareCollection("artifact", before.Artifacts, after.Artifacts,
            artifact => artifact.Id, artifact => artifact.Key, SummarizeArtifact);
        CompareCollection("net", before.Electrical?.Nets ?? [], after.Electrical?.Nets ?? [],
            net => net.Id, net => net.Key, net => $"{net.Name} [{net.Class}]");
        CompareCollection("board", before.Electrical?.Boards ?? [], after.Electrical?.Boards ?? [],
            board => board.Id, board => board.Key, board => board.Refdes ?? board.Key ?? "");
        CompareCollection("mechanical joint", before.Mechanical?.Joints ?? [], after.Mechanical?.Joints ?? [],
            joint => joint.Id, joint => joint.Key, joint => joint.Type.ToString());
        CompareCollection("firmware project", before.Firmware?.Projects ?? [], after.Firmware?.Projects ?? [],
            firmware => firmware.Id, firmware => firmware.Key, firmware => firmware.Name ?? firmware.Key);
        CompareCollection("behavior model", before.Behavior?.Models ?? [], after.Behavior?.Models ?? [],
            model => model.Id, model => model.Key, model => model.Name ?? model.Key);
        CompareCollection("simulation system", before.Simulation?.Systems ?? [], after.Simulation?.Systems ?? [],
            system => system.Id, system => system.Key, system => $"{system.Standard}/{system.Master}");
        CompareCollection("scenario", before.Simulation?.Scenarios ?? [], after.Simulation?.Scenarios ?? [],
            scenario => scenario.Id, scenario => scenario.Key, scenario => scenario.Name);
        CompareCollection("manufacturing process", before.Manufacturing?.Processes ?? [], after.Manufacturing?.Processes ?? [],
            process => process.Id, process => process.Key, process => process.Name ?? process.Key);

        if (before.Name != after.Name ||
            before.Revision != after.Revision ||
            before.License != after.License)
        {
            changes.Add(new ProjectChange(
                "changed",
                "project",
                before.Id.ToString(),
                before.Key,
                $"{before.Name} {before.Revision}".Trim(),
                $"{after.Name} {after.Revision}".Trim()));
        }

        changes.Sort((left, right) =>
        {
            var type = string.CompareOrdinal(left.EntityType, right.EntityType);
            return type != 0
                ? type
                : string.CompareOrdinal(left.Key ?? left.Id, right.Key ?? right.Id);
        });

        return new ProjectDiff(changes);

        void CompareCollection<T>(
            string entityType,
            IEnumerable<T> beforeItems,
            IEnumerable<T> afterItems,
            Func<T, Guid> idSelector,
            Func<T, string?> keySelector,
            Func<T, string> summary)
        {
            var beforeMap = beforeItems
                .GroupBy(idSelector)
                .ToDictionary(group => group.Key, group => group.First());
            var afterMap = afterItems
                .GroupBy(idSelector)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (var id in beforeMap.Keys.Union(afterMap.Keys).OrderBy(id => id))
            {
                beforeMap.TryGetValue(id, out var beforeItem);
                afterMap.TryGetValue(id, out var afterItem);

                if (beforeItem is null && afterItem is not null)
                {
                    changes.Add(new ProjectChange(
                        "added",
                        entityType,
                        id.ToString(),
                        keySelector(afterItem),
                        null,
                        summary(afterItem)));
                    continue;
                }

                if (beforeItem is not null && afterItem is null)
                {
                    changes.Add(new ProjectChange(
                        "removed",
                        entityType,
                        id.ToString(),
                        keySelector(beforeItem),
                        summary(beforeItem),
                        null));
                    continue;
                }

                if (beforeItem is not null && afterItem is not null)
                {
                    var beforeJson = JsonSerializer.Serialize(beforeItem, CoreJson.Options);
                    var afterJson = JsonSerializer.Serialize(afterItem, CoreJson.Options);
                    if (beforeJson != afterJson)
                    {
                        changes.Add(new ProjectChange(
                            "changed",
                            entityType,
                            id.ToString(),
                            keySelector(afterItem) ?? keySelector(beforeItem),
                            summary(beforeItem),
                            summary(afterItem)));
                    }
                }
            }
        }
    }

    private static string SummarizePart(Part part) =>
        $"{part.Name} [{part.Kind}]";

    private static string SummarizeArtifact(Artifact artifact) =>
        $"{artifact.Format} [{artifact.Role}] {ShortHash(artifact.Sha256)}".Trim();

    private static string ShortHash(string? hash) =>
        string.IsNullOrWhiteSpace(hash) ? "" : hash[..Math.Min(8, hash.Length)];
}
