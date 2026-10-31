using System.Text.Json.Nodes;
using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Core.Exporters;

public sealed record MechMakerExportResult(
    JsonObject? Machine,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Machine is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>Exports the subset of an EMD project that MechMaker can compile.</summary>
public sealed class MechMakerMachineExporter
{
    private static readonly string[] KnownSignals =
        ["step", "dir", "enable", "endstop", "pwm", "power", "heater"];

    public static MechMakerMachineExporter Default { get; } = new();

    public MechMakerExportResult Export(EmdProject project)
    {
        var diagnostics = new List<Diagnostic>();
        var partsById = new Dictionary<Guid, Part>();
        foreach (var part in project.Parts)
            partsById.TryAdd(part.Id, part);

        var partKeys = new Dictionary<Guid, string>();
        var usedPartIds = new HashSet<string>(StringComparer.Ordinal);
        var machineParts = new JsonArray();

        foreach (var part in project.Parts)
        {
            if (part.Kind == PartKind.Board)
                continue;

            var catalog = part.CatalogRef;
            if (catalog is null ||
                string.IsNullOrWhiteSpace(catalog.Part) ||
                (catalog.Catalog is not null &&
                 !string.Equals(catalog.Catalog, "mechmaker", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECHMAKER-001",
                    DiagnosticSeverity.Warning,
                    $"Part '{Entity(part)}' has no MechMaker catalog reference; skipped.",
                    Entity: Entity(part)));
                continue;
            }

            var key = part.Key ?? $"part_{part.Id:N}";
            if (!usedPartIds.Add(key))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECHMAKER-004",
                    DiagnosticSeverity.Warning,
                    $"Duplicate MechMaker part id '{key}'; skipped.",
                    Entity: key));
                continue;
            }

            partKeys[part.Id] = key;
            var machinePart = new JsonObject
            {
                ["id"] = key,
                ["part"] = catalog.Part
            };

            if (part.Pose is { } pose)
            {
                var poseObject = new JsonObject();
                if (pose.Pos is not null)
                    poseObject["pos"] = ToJsonArray(pose.Pos);
                if (pose.Rot is not null)
                    poseObject["rot"] = ToJsonArray(pose.Rot);
                if (poseObject.Count > 0)
                    machinePart["pose"] = poseObject;
            }

            machineParts.Add(machinePart);
        }

        var boardKeys = new Dictionary<Guid, string>();
        var usedBoardIds = new HashSet<string>(StringComparer.Ordinal);
        var machineBoards = new JsonArray();

        foreach (var board in project.Electrical?.Boards ?? [])
        {
            if (!partsById.TryGetValue(board.PartId, out var boardPart))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECHMAKER-002",
                    DiagnosticSeverity.Warning,
                    $"Board '{board.Key ?? board.Id.ToString()}' references a missing part; skipped.",
                    Entity: board.Key ?? board.Id.ToString()));
                continue;
            }

            var key = board.Key ?? boardPart.Key ?? $"board_{board.Id:N}";
            if (!usedBoardIds.Add(key))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECHMAKER-004",
                    DiagnosticSeverity.Warning,
                    $"Duplicate MechMaker board id '{key}'; skipped.",
                    Entity: key));
                continue;
            }

            boardKeys[boardPart.Id] = key;
            var pins = new JsonObject();
            foreach (var port in boardPart.Ports)
            {
                var logical = string.IsNullOrWhiteSpace(port.Name) ? port.Id : port.Name;
                var physical = string.IsNullOrWhiteSpace(port.Key) ? port.Id : port.Key;
                pins[logical] = physical;
            }

            machineBoards.Add(new JsonObject
            {
                ["id"] = key,
                ["type"] = boardPart.CatalogRef?.Part ?? boardPart.Name,
                ["pins"] = pins
            });
        }

        var machineConnections = new JsonArray();
        var usedConnectionIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var connection in project.Connections)
        {
            if (connection.Kind is not (ConnectionKind.Mate or ConnectionKind.Mount))
                continue;
            if (connection.From.PartId is not { } fromPart ||
                connection.To.PartId is not { } toPart ||
                connection.From.PortId is not { } fromPort ||
                connection.To.PortId is not { } toPort)
            {
                continue;
            }

            if (!partKeys.TryGetValue(fromPart, out var fromKey) ||
                !partKeys.TryGetValue(toPart, out var toKey))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECHMAKER-002",
                    DiagnosticSeverity.Warning,
                    $"Connection '{connection.Key ?? connection.Id.ToString()}' references a skipped part; omitted.",
                    Entity: connection.Key ?? connection.Id.ToString()));
                continue;
            }

            var id = connection.Key ?? $"conn_{connection.Id:N}";
            if (!usedConnectionIds.Add(id))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECHMAKER-004",
                    DiagnosticSeverity.Warning,
                    $"Duplicate MechMaker connection id '{id}'; skipped.",
                    Entity: id));
                continue;
            }

            machineConnections.Add(new JsonObject
            {
                ["id"] = id,
                ["part_a"] = fromKey,
                ["connector_a"] = fromPort,
                ["part_b"] = toKey,
                ["connector_b"] = toPort
            });
        }

        var endpointsByNet = new Dictionary<Guid, List<(Guid PartId, string PortId)>>();
        foreach (var connection in project.Connections)
        {
            AddEndpoint(connection.From, connection.To);
            AddEndpoint(connection.To, connection.From);
        }

        var machineWiring = new JsonArray();
        var usedWiring = new HashSet<string>(StringComparer.Ordinal);

        foreach (var net in (project.Electrical?.Nets ?? []).OrderBy(net => net.Key, StringComparer.Ordinal))
        {
            if (!endpointsByNet.TryGetValue(net.Id, out var endpoints))
                continue;

            var boardEndpoints = endpoints.Where(endpoint => boardKeys.ContainsKey(endpoint.PartId)).ToList();
            var componentEndpoints = endpoints.Where(endpoint => partKeys.ContainsKey(endpoint.PartId)).ToList();
            if (componentEndpoints.Count == 0 || boardEndpoints.Count == 0)
                continue;

            foreach (var component in componentEndpoints)
            {
                if (!partsById.TryGetValue(component.PartId, out var componentPart))
                    continue;
                var componentPort = componentPart.Ports.FirstOrDefault(port => port.Id == component.PortId);
                if (componentPort is null)
                    continue;

                var signal = MapSignal(componentPort, net);
                if (signal is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-MECHMAKER-003",
                        DiagnosticSeverity.Warning,
                        $"Net '{net.Key}' cannot be mapped to a MechMaker signal; omitted.",
                        Entity: net.Key));
                    continue;
                }

                foreach (var board in boardEndpoints)
                {
                    if (!partsById.TryGetValue(board.PartId, out var boardPart))
                        continue;
                    var boardPort = boardPart.Ports.FirstOrDefault(port => port.Id == board.PortId);
                    if (boardPort is null)
                        continue;

                    var pin = string.IsNullOrWhiteSpace(boardPort.Key) ? boardPort.Id : boardPort.Key;
                    var wiringKey = $"{partKeys[component.PartId]}|{signal}|{boardKeys[board.PartId]}|{pin}";
                    if (!usedWiring.Add(wiringKey))
                        continue;

                    machineWiring.Add(new JsonObject
                    {
                        ["component"] = partKeys[component.PartId],
                        ["signal"] = signal,
                        ["board"] = boardKeys[board.PartId],
                        ["pin"] = pin
                    });
                }
            }
        }

        if (machineParts.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-MECHMAKER-005",
                DiagnosticSeverity.Error,
                "No parts with MechMaker catalog references were found."));
            return new MechMakerExportResult(null, diagnostics);
        }

        var machine = new JsonObject
        {
            ["schema_version"] = "0.1",
            ["name"] = project.Name,
            ["parts"] = machineParts
        };
        if (machineConnections.Count > 0)
            machine["connections"] = machineConnections;
        if (machineBoards.Count > 0)
            machine["boards"] = machineBoards;
        if (machineWiring.Count > 0)
            machine["wiring"] = machineWiring;

        return new MechMakerExportResult(machine, diagnostics);

        void AddEndpoint(Endpoint netSide, Endpoint partSide)
        {
            if (netSide.NetId is not { } netId ||
                partSide.PartId is not { } partId ||
                partSide.PortId is not { } portId)
            {
                return;
            }

            if (!endpointsByNet.TryGetValue(netId, out var endpoints))
            {
                endpoints = [];
                endpointsByNet[netId] = endpoints;
            }

            if (endpoints.All(endpoint => endpoint.PartId != partId || endpoint.PortId != portId))
                endpoints.Add((partId, portId));
        }
    }

    private static string? MapSignal(Port port, Net net)
    {
        var kind = port.Kind.ToLowerInvariant();
        if (KnownSignals.Contains(kind))
            return kind;

        var key = net.Key.ToLowerInvariant();
        return KnownSignals.FirstOrDefault(signal => key.Contains(signal, StringComparison.Ordinal));
    }

    private static JsonArray ToJsonArray(IEnumerable<double> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }

    private static string Entity(Part part) => part.Key ?? part.Id.ToString();
}
