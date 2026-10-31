using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Core.Exporters;

public sealed record HarnessExportResult(
    string? Yaml,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Yaml is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>Generates a WireViz-compatible YAML harness from point-to-point nets.</summary>
public sealed class HarnessExporter
{
    public static HarnessExporter Default { get; } = new();

    public HarnessExportResult Export(EmdProject project)
    {
        var diagnostics = new List<Diagnostic>();
        var partsById = new Dictionary<Guid, Part>();
        foreach (var part in project.Parts)
            partsById.TryAdd(part.Id, part);

        var endpointsByNet = new Dictionary<Guid, List<(Part Part, Port Port)>>();

        foreach (var connection in project.Connections)
        {
            AddEndpoint(connection.From, connection.To);
            AddEndpoint(connection.To, connection.From);
        }

        var connectors = new SortedDictionary<string, ConnectorInfo>(StringComparer.Ordinal);
        var wires = new List<(Net Net, (Part Part, Port Port) A, (Part Part, Port Port) B)>();

        foreach (var net in (project.Electrical?.Nets ?? []).OrderBy(net => net.Key, StringComparer.Ordinal))
        {
            if (!endpointsByNet.TryGetValue(net.Id, out var endpoints) || endpoints.Count == 0)
                continue;

            if (endpoints.Count != 2)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-HARNESS-001",
                    DiagnosticSeverity.Warning,
                    $"Net '{net.Key}' has {endpoints.Count} endpoints; WireViz export supports point-to-point nets.",
                    Entity: net.Key));
                continue;
            }

            var ordered = endpoints
                .OrderBy(endpoint => endpoint.Part.Key ?? endpoint.Part.Id.ToString(), StringComparer.Ordinal)
                .ThenBy(endpoint => endpoint.Port.Id, StringComparer.Ordinal)
                .ToList();

            wires.Add((net, ordered[0], ordered[1]));
            AddConnector(ordered[0]);
            AddConnector(ordered[1]);
        }

        var builder = new StringBuilder();
        AppendConnectors(builder, connectors);

        if (wires.Count == 0)
        {
            builder.AppendLine("cables: {}");
            builder.AppendLine("connections: []");
        }
        else
        {
            AppendCables(builder, wires.Count);
            AppendConnections(builder, wires);
        }

        return new HarnessExportResult(builder.ToString(), diagnostics);

        void AddEndpoint(Endpoint netSide, Endpoint partSide)
        {
            if (netSide.NetId is not { } netId)
                return;
            if (partSide.PartId is not { } partId || partSide.PortId is not { } portId)
                return;

            if (!partsById.TryGetValue(partId, out var part))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-HARNESS-002",
                    DiagnosticSeverity.Warning,
                    $"Net endpoint references missing part '{partId}'.",
                    Entity: netId.ToString()));
                return;
            }

            var port = part.Ports.FirstOrDefault(candidate => candidate.Id == portId);
            if (port is null)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-HARNESS-002",
                    DiagnosticSeverity.Warning,
                    $"Net endpoint references missing port '{portId}' on part '{part.Key ?? part.Id.ToString()}'.",
                    Entity: netId.ToString()));
                return;
            }

            if (!endpointsByNet.TryGetValue(netId, out var endpoints))
            {
                endpoints = [];
                endpointsByNet[netId] = endpoints;
            }

            if (endpoints.All(endpoint => endpoint.Part.Id != partId || endpoint.Port.Id != portId))
                endpoints.Add((part, port));
        }

        void AddConnector((Part Part, Port Port) endpoint)
        {
            var name = endpoint.Part.Key ?? endpoint.Part.Id.ToString();
            if (!connectors.TryGetValue(name, out var connector))
            {
                connector = new ConnectorInfo(
                    name,
                    endpoint.Part.CatalogRef?.Part ?? endpoint.Part.Name);
                connectors[name] = connector;
            }

            connector.Ports.TryAdd(endpoint.Port.Id, endpoint.Port);
        }
    }

    private static void AppendConnectors(
        StringBuilder builder,
        IReadOnlyDictionary<string, ConnectorInfo> connectors)
    {
        if (connectors.Count == 0)
        {
            builder.AppendLine("connectors: {}");
            return;
        }

        builder.AppendLine("connectors:");
        foreach (var connector in connectors.Values)
        {
            builder.Append("  ").AppendLine(Quote(connector.Name) + ":");
            builder.Append("    type: ").AppendLine(Quote(connector.Type));
            builder.Append("    pincount: ").AppendLine(connector.Ports.Count.ToString());
            builder.Append("    pins: [")
                .Append(string.Join(", ", connector.Ports.Values.OrderBy(port => port.Key, StringComparer.Ordinal).Select(port => Quote(port.Key))))
                .AppendLine("]");
        }
    }

    private static void AppendCables(StringBuilder builder, int count)
    {
        builder.AppendLine("cables:");
        for (var index = 1; index <= count; index++)
        {
            builder.Append("  ").AppendLine(Quote($"W{index}") + ":");
            builder.AppendLine("    wirecount: 1");
            builder.AppendLine("    colors: [\"BK\"]");
        }
    }

    private static void AppendConnections(
        StringBuilder builder,
        IReadOnlyList<(Net Net, (Part Part, Port Port) A, (Part Part, Port Port) B)> wires)
    {
        builder.AppendLine("connections:");
        for (var index = 0; index < wires.Count; index++)
        {
            var wire = wires[index];
            var cable = $"W{index + 1}";
            builder.AppendLine("  -");
            builder.Append("    - ").Append(Quote(ConnectorName(wire.A.Part)))
                .Append(": [").Append(Quote(wire.A.Port.Key)).AppendLine("]");
            builder.Append("    - ").Append(Quote(cable)).AppendLine(": [1]");
            builder.Append("    - ").Append(Quote(ConnectorName(wire.B.Part)))
                .Append(": [").Append(Quote(wire.B.Port.Key)).AppendLine("]");
        }
    }

    private static string ConnectorName(Part part) => part.Key ?? part.Id.ToString();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string Quote(string value) => JsonSerializer.Serialize(value, JsonOptions);

    private sealed class ConnectorInfo
    {
        public ConnectorInfo(string name, string type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }

        public string Type { get; }

        public SortedDictionary<string, Port> Ports { get; } = new(StringComparer.Ordinal);
    }
}
