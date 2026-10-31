using System.Text.Json;
using Emd.Core.Model;

namespace Emd.Core.Importers;

public sealed record MechMakerCatalogPart(string Id, string Name, PartCategory Category);

public sealed class MechMakerCatalog
{
    private readonly Dictionary<string, MechMakerCatalogPart> _parts;

    private MechMakerCatalog(string directoryPath, Dictionary<string, MechMakerCatalogPart> parts)
    {
        DirectoryPath = directoryPath;
        _parts = parts;
    }

    public string DirectoryPath { get; }

    public static MechMakerCatalog Load(string directoryPath)
    {
        var parts = new Dictionary<string, MechMakerCatalogPart>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.json"))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var idProperty))
                    continue;

                var id = idProperty.GetString();
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var name = root.TryGetProperty("name", out var nameProperty)
                    ? nameProperty.GetString() ?? id
                    : id;
                var category = root.TryGetProperty("category", out var categoryProperty)
                    ? MapCategory(categoryProperty.GetString())
                    : PartCategory.Other;

                parts[id] = new MechMakerCatalogPart(id, name, category);
            }
            catch (JsonException)
            {
                // A malformed catalog file is skipped; the importer falls back to inference.
            }
        }

        return new MechMakerCatalog(directoryPath, parts);
    }

    public bool TryGet(string id, out MechMakerCatalogPart part) =>
        _parts.TryGetValue(id, out part!);

    private static PartCategory MapCategory(string? category) => category switch
    {
        "structure" => PartCategory.Structure,
        "motor" => PartCategory.Motor,
        "transmission" => PartCategory.Transmission,
        "linear_motion" => PartCategory.LinearMotion,
        "sensor" => PartCategory.Sensor,
        "electronics" => PartCategory.Electronics,
        "tool" => PartCategory.Tool,
        "fastener" => PartCategory.Fastener,
        _ => PartCategory.Other
    };
}
