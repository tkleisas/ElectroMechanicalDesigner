using System.Globalization;
using System.Text;
using System.Text.Json;
using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Core.Exporters;

public sealed record BomExportResult(
    string? Csv,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Csv is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed class BomExporter
{
    private const string Header =
        "item,quantity,refs,name,category,mass_each_kg,mass_total_kg,material,catalog";

    public static BomExporter Default { get; } = new();

    public BomExportResult Export(EmdProject project)
    {
        if (project.Parts.Count == 0)
        {
            return new BomExportResult(null,
            [
                new Diagnostic("EMD-BOM-001", DiagnosticSeverity.Error,
                    "Cannot export a BOM: the project has no parts.")
            ]);
        }

        var groups = new Dictionary<string, BomGroup>(StringComparer.Ordinal);
        foreach (var part in project.Parts)
        {
            var key = GroupKey(part);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new BomGroup(key, part, []);
                groups[key] = group;
            }

            group.Parts.Add(part);
        }

        var builder = new StringBuilder();
        builder.AppendLine(Header);
        var item = 0;

        foreach (var group in groups.Values
                     .OrderBy(group => group.Sample.Category)
                     .ThenBy(group => group.Sample.Name, StringComparer.Ordinal)
                     .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            item++;
            var parts = group.Parts
                .OrderBy(part => part.Key ?? part.Id.ToString(), StringComparer.Ordinal)
                .ToList();
            var massEach = parts.Select(part => part.MassKg).FirstOrDefault(mass => mass is not null);
            var massTotal = massEach is { } each ? each * parts.Count : (double?)null;
            var catalog = parts[0].CatalogRef?.Part;

            builder.AppendLine(string.Join(",",
                Csv(item.ToString(CultureInfo.InvariantCulture)),
                Csv(parts.Count.ToString(CultureInfo.InvariantCulture)),
                Csv(string.Join(";", parts.Select(part => part.Key ?? part.Id.ToString()))),
                Csv(group.Sample.Name),
                Csv(CategoryName(group.Sample.Category)),
                Csv(FormatNumber(massEach)),
                Csv(FormatNumber(massTotal)),
                Csv(group.Sample.Material?.Name),
                Csv(catalog)));
        }

        return new BomExportResult(builder.ToString(), []);
    }

    private static string GroupKey(Part part)
    {
        if (part.CatalogRef is { } catalog)
            return $"{catalog.Catalog}:{catalog.Part}";

        return $"{part.Name}|{part.Material?.Name}";
    }

    private static string CategoryName(PartCategory? category) =>
        category is null
            ? ""
            : JsonNamingPolicy.SnakeCaseLower.ConvertName(category.Value.ToString());

    private static string FormatNumber(double? value) =>
        value is null
            ? ""
            : value.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Csv(string? value)
    {
        value ??= "";
        if (!value.Contains(',', StringComparison.Ordinal) &&
            !value.Contains('"', StringComparison.Ordinal) &&
            !value.Contains('\n', StringComparison.Ordinal))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record BomGroup(string Key, Part Sample, List<Part> Parts);
}
