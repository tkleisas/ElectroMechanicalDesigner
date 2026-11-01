using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Emd.Core.Diagnostics;
using Emd.Core.Importers;
using Emd.Core.Model;

namespace Emd.Core.Exporters;

public sealed record SspExportResult(
    byte[]? Archive,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Archive is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>Composes an SSP 2.0 archive with FMU resources from behavior models.</summary>
public sealed class SspComposer
{
    private static readonly XNamespace Ssd = "http://ssp-standard.org/SSP1/SystemStructureDescription";
    private static readonly XNamespace Ssc = "http://ssp-standard.org/SSP1/SystemStructureCommon";
    private static readonly XNamespace Ssv = "http://ssp-standard.org/SSP1/SystemStructureParameterValues";
    private static readonly DateTimeOffset FixedTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static SspComposer Default { get; } = new();

    public SspExportResult Compose(EmdProject project, string version = "2.0")
    {
        if (version is not ("1.0" or "2.0"))
        {
            return new SspExportResult(null,
            [
                new Diagnostic(
                    "EMD-SSP-004",
                    DiagnosticSeverity.Error,
                    $"Unsupported SSP version '{version}'; use 1.0 or 2.0.")
            ]);
        }

        var diagnostics = new List<Diagnostic>();
        var components = new List<ComponentInfo>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var connectorByArtifactVariable = new Dictionary<(Guid ArtifactId, string Variable), string>();

        foreach (var model in project.Behavior?.Models ?? [])
        {
            var artifact = project.Artifacts.FirstOrDefault(candidate => candidate.Id == model.ArtifactId);
            if (artifact is null ||
                !string.Equals(artifact.Format, "fmu", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new Diagnostic(
                    model.Kind == ModelKind.Spice ? "EMD-SSP-005" : "EMD-SSP-001",
                    DiagnosticSeverity.Warning,
                    model.Kind == ModelKind.Spice
                        ? $"SPICE model '{model.Key}' has no FMU artifact; " +
                          "export the netlist as an FMU before composing."
                        : $"Behavior model '{model.Key}' has no FMU artifact; omitted.",
                    Entity: model.Key));
                continue;
            }

            if (artifact.Path.StartsWith("artifact://", StringComparison.Ordinal) ||
                !File.Exists(artifact.Path))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-SSP-002",
                    DiagnosticSeverity.Error,
                    $"FMU artifact '{artifact.Key ?? artifact.Id.ToString()}' for model '{model.Key}' " +
                    "could not be read.",
                    Entity: model.Key));
                continue;
            }

            var baseName = Slug.Key(model.Key, "component");
            var componentName = baseName;
            var suffix = 2;
            while (!usedNames.Add(componentName))
                componentName = $"{baseName}_{suffix++}";

            var connectors = new List<XElement>();
            var assignedVariables = new HashSet<string>(StringComparer.Ordinal);

            void AddConnector(ModelPort port, string kind)
            {
                var variable = string.IsNullOrWhiteSpace(port.Variable) ? port.Name : port.Variable;
                if (string.IsNullOrWhiteSpace(variable) || !assignedVariables.Add(variable))
                    return;

                connectorByArtifactVariable[(model.ArtifactId, variable)] = variable;
                connectors.Add(new XElement(Ssd + "Connector",
                    new XAttribute("name", variable),
                    new XAttribute("kind", kind),
                    new XElement(Ssc + ConnectorTypeName(port.Type))));
            }

            foreach (var input in model.Inputs ?? [])
                AddConnector(input, "input");
            foreach (var output in model.Outputs ?? [])
                AddConnector(output, "output");

            components.Add(new ComponentInfo(
                model,
                artifact,
                componentName,
                $"resources/{componentName}.fmu",
                connectors));
        }

        if (components.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-SSP-001",
                DiagnosticSeverity.Error,
                "No behavior models with readable FMU artifacts were found."));
            return new SspExportResult(null, diagnostics);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return new SspExportResult(null, diagnostics);

        var componentByArtifact = components.ToDictionary(
            component => component.Model.ArtifactId,
            component => component.Name);

        var connections = new List<XElement>();
        foreach (var connection in project.Connections)
        {
            if (connection.Kind != ConnectionKind.SimulationBinding)
                continue;
            if (connection.From.ArtifactId is not { } fromArtifact ||
                connection.To.ArtifactId is not { } toArtifact ||
                string.IsNullOrWhiteSpace(connection.From.Variable) ||
                string.IsNullOrWhiteSpace(connection.To.Variable))
            {
                continue;
            }

            if (!componentByArtifact.TryGetValue(fromArtifact, out var fromComponent) ||
                !componentByArtifact.TryGetValue(toArtifact, out var toComponent) ||
                !connectorByArtifactVariable.ContainsKey((fromArtifact, connection.From.Variable)) ||
                !connectorByArtifactVariable.ContainsKey((toArtifact, connection.To.Variable)))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-SSP-003",
                    DiagnosticSeverity.Warning,
                    $"Simulation binding '{connection.Key ?? connection.Id.ToString()}' " +
                    "does not match known FMU connectors; omitted.",
                    Entity: connection.Key ?? connection.Id.ToString()));
                continue;
            }

            connections.Add(new XElement(Ssd + "Connection",
                new XAttribute("startElement", fromComponent),
                new XAttribute("startConnector", connection.From.Variable),
                new XAttribute("endElement", toComponent),
                new XAttribute("endConnector", connection.To.Variable)));
        }

        var system = project.Simulation?.Systems
            .FirstOrDefault(candidate => candidate.Standard == SimulationStandard.Ssp2)
            ?? project.Simulation?.Systems.FirstOrDefault();

        var root = new XElement(Ssd + "SystemStructureDescription",
            new XAttribute(XNamespace.Xmlns + "ssd", Ssd),
            new XAttribute(XNamespace.Xmlns + "ssc", Ssc),
            new XAttribute(XNamespace.Xmlns + "ssv", Ssv),
            new XAttribute("version", version),
            new XAttribute("name", project.Name),
            new XElement(Ssd + "System",
                new XAttribute("name", project.Name),
                new XElement(Ssd + "Elements",
                    components.Select(component =>
                        new XElement(Ssd + "Component",
                            new XAttribute("name", component.Name),
                            new XAttribute("type", "application/x-fmu-sharedlibrary"),
                            new XAttribute("source", component.Source),
                            component.Connectors.Count == 0
                                ? null
                                : new XElement(Ssd + "Connectors", component.Connectors),
                            ParameterBindings(component)))),
                connections.Count == 0
                    ? null
                    : new XElement(Ssd + "Connections", connections)),
            new XElement(Ssd + "DefaultExperiment",
                new XAttribute("startTime", Format(system?.StartTimeS ?? 0)),
                new XAttribute("stopTime", Format(system?.StopTimeS ?? 1))));

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            root);

        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(archive, "SystemStructure.ssd",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + document.Root!.ToString());
            foreach (var component in components)
            {
                AddBytes(
                    archive,
                    component.Source,
                    File.ReadAllBytes(component.Artifact.Path));
            }
        }

        return new SspExportResult(memory.ToArray(), diagnostics);
    }

    private static void AddText(ZipArchive archive, string name, string text) =>
        AddBytes(archive, name, Encoding.UTF8.GetBytes(text));

    private static void AddBytes(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTimestamp;
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static XElement? ParameterBindings(ComponentInfo component)
    {
        if (component.Model.Parameters is not { Count: > 0 } parameters)
            return null;

        var values = parameters
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .Select(parameter => ParameterElement(parameter.Key, parameter.Value))
            .Where(parameter => parameter is not null)
            .ToList();
        if (values.Count == 0)
            return null;

        return new XElement(Ssd + "ParameterBindings",
            new XElement(Ssd + "ParameterBinding",
                new XElement(Ssd + "ParameterValues",
                    new XElement(Ssv + "ParameterSet",
                        new XAttribute("version", "1.0"),
                        new XAttribute("name", $"{component.Name}.parameters"),
                        new XElement(Ssv + "Parameters", values)))));
    }

    private static XElement? ParameterElement(string name, JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;

        XElement? typed;
        if (value.TryGetValue<bool>(out var boolean))
            typed = new XElement(Ssv + "Boolean",
                new XAttribute("value", boolean ? "true" : "false"));
        else if (value.TryGetValue<int>(out var integer))
            typed = new XElement(Ssv + "Integer",
                new XAttribute("value", integer.ToString(CultureInfo.InvariantCulture)));
        else if (value.TryGetValue<double>(out var real))
            typed = new XElement(Ssv + "Real",
                new XAttribute("value", real.ToString("R", CultureInfo.InvariantCulture)));
        else if (value.TryGetValue<string>(out var text))
            typed = new XElement(Ssv + "String", new XAttribute("value", text));
        else
            return null;

        return new XElement(Ssv + "Parameter",
            new XAttribute("name", name),
            typed);
    }

    private static string ConnectorTypeName(string? type) =>
        type?.ToLowerInvariant() switch
        {
            "integer" => "Integer",
            "boolean" => "Boolean",
            "string" => "String",
            _ => "Real"
        };

    private static string Format(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private sealed record ComponentInfo(
        BehaviorModel Model,
        Artifact Artifact,
        string Name,
        string Source,
        IReadOnlyList<XElement> Connectors);
}
