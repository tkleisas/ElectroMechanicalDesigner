using System.Reflection;
using System.Text.Json;
using Emd.Core.Diagnostics;
using Json.Schema;

namespace Emd.Core.Projects;

public sealed class SchemaValidator
{
    private const string EmbeddedSchemaName = "EMD.Core.Schema.emd-project.schema.json";

    private readonly JsonSchema _schema;

    private SchemaValidator(JsonSchema schema)
    {
        _schema = schema;
    }

    public static SchemaValidator Default { get; } = new(LoadEmbedded());

    public static SchemaValidator FromFile(string path) => new(JsonSchema.FromFile(path));

    public IReadOnlyList<Diagnostic> Validate(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return
            [
                new Diagnostic(
                    "EMD-JSON-001",
                    DiagnosticSeverity.Error,
                    $"Invalid JSON: {exception.Message}")
            ];
        }

        using (document)
        {
            return Validate(document.RootElement);
        }
    }

    public IReadOnlyList<Diagnostic> Validate(JsonElement instance)
    {
        var result = _schema.Evaluate(instance, new EvaluationOptions
        {
            OutputFormat = OutputFormat.Hierarchical,
            RequireFormatValidation = true
        });

        if (result.IsValid)
            return [];

        var diagnostics = new List<Diagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Collect(result, diagnostics, seen);

        if (diagnostics.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-SCHEMA-001",
                DiagnosticSeverity.Error,
                "Project does not match the EMD project schema."));
        }

        return diagnostics;
    }

    private static void Collect(EvaluationResults result, List<Diagnostic> diagnostics, HashSet<string> seen)
    {
        if (!result.IsValid && result.Errors is { Count: > 0 })
        {
            var path = result.InstanceLocation.ToString();
            foreach (var error in result.Errors)
            {
                var message = $"{error.Key}: {error.Value}";
                if (!seen.Add($"{path}|{message}"))
                    continue;

                diagnostics.Add(new Diagnostic(
                    "EMD-SCHEMA-001",
                    DiagnosticSeverity.Error,
                    message,
                    Path: string.IsNullOrWhiteSpace(path) ? null : path));
            }
        }

        if (result.Details is null)
            return;

        // A valid parent can still contain failed `anyOf`/`oneOf` branches in its
        // details. Recurse only into invalid children so those annotations do not
        // surface as false errors.
        foreach (var detail in result.Details)
        {
            if (!detail.IsValid)
                Collect(detail, diagnostics, seen);
        }
    }

    private static JsonSchema LoadEmbedded()
    {
        var assembly = typeof(SchemaValidator).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedSchemaName)
            ?? throw new InvalidOperationException(
                $"Embedded schema '{EmbeddedSchemaName}' was not found. Available resources: " +
                string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
