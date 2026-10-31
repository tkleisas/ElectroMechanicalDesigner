using System.Text.Json;
using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Core.Projects;

public sealed record ProjectLoadResult(
    EmdProject? Project,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Project is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed class ProjectLoader
{
    private readonly SchemaValidator _schema;

    public ProjectLoader(SchemaValidator schema)
    {
        _schema = schema;
    }

    public static ProjectLoader Default { get; } = new(SchemaValidator.Default);

    public ProjectLoadResult LoadFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ProjectLoadResult(null,
            [
                new Diagnostic("EMD-IO-001", DiagnosticSeverity.Error,
                    $"Could not read project '{path}': {exception.Message}", Entity: path)
            ]);
        }

        return Load(json, path);
    }

    public ProjectLoadResult Load(string json, string? source = null)
    {
        var schemaDiagnostics = _schema.Validate(json);
        if (schemaDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return new ProjectLoadResult(null, schemaDiagnostics);

        try
        {
            var project = JsonSerializer.Deserialize<EmdProject>(json, CoreJson.Options);
            if (project is null)
            {
                return new ProjectLoadResult(null,
                [
                    new Diagnostic("EMD-JSON-002", DiagnosticSeverity.Error,
                        "Project JSON deserialized to null.", Entity: source)
                ]);
            }

            return new ProjectLoadResult(project, schemaDiagnostics);
        }
        catch (JsonException exception)
        {
            return new ProjectLoadResult(null,
            [
                new Diagnostic("EMD-JSON-002", DiagnosticSeverity.Error,
                    $"Could not deserialize project: {exception.Message}", Entity: source)
            ]);
        }
    }
}
