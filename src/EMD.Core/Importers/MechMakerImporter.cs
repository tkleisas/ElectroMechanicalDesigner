using System.Text.Json;
using System.Text.Json.Nodes;
using Emd.Core.Diagnostics;
using Emd.Core.Identity;
using Emd.Core.Model;

namespace Emd.Core.Importers;

public sealed record MechMakerImportResult(
    EmdProject? Project,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Project is not null
        && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>
/// Imports a MechMaker <c>machine.json</c> into an EMD project. IDs are deterministic
/// (UUID v5), so repeated imports of the same machine are stable and diffable.
/// </summary>
public sealed class MechMakerImporter
{
    public static MechMakerImporter Default { get; } = new();

    public MechMakerImportResult ImportFile(string path, MechMakerCatalog? catalog = null)
    {
        try
        {
            return Import(File.ReadAllText(path), path, catalog);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new MechMakerImportResult(null,
            [
                new Diagnostic("EMD-IO-001", DiagnosticSeverity.Error,
                    $"Could not read MechMaker machine '{path}': {exception.Message}", Entity: path)
            ]);
        }
    }

    public MechMakerImportResult Import(string json, string? source = null, MechMakerCatalog? catalog = null)
    {
        MechMachine? machine;
        try
        {
            machine = JsonSerializer.Deserialize<MechMachine>(json, CoreJson.Options);
        }
        catch (JsonException exception)
        {
            return new MechMakerImportResult(null,
            [
                new Diagnostic("EMD-IMPORT-002", DiagnosticSeverity.Error,
                    $"Invalid MechMaker machine JSON: {exception.Message}", Entity: source)
            ]);
        }

        if (machine is null)
        {
            return new MechMakerImportResult(null,
            [
                new Diagnostic("EMD-IMPORT-002", DiagnosticSeverity.Error,
                    "MechMaker machine JSON deserialized to null.", Entity: source)
            ]);
        }

        var diagnostics = new List<Diagnostic>();
        var ns = EmdNamespaces.MechMaker;
        var name = string.IsNullOrWhiteSpace(machine.Name) ? "mechmaker_machine" : machine.Name.Trim();

        var partIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var part in machine.Parts ?? [])
        {
            if (string.IsNullOrWhiteSpace(part.Id))
            {
                diagnostics.Add(new Diagnostic("EMD-IMPORT-003", DiagnosticSeverity.Error,
                    "MechMaker part with an empty id was skipped."));
                continue;
            }

            if (!partIds.TryAdd(part.Id, DeterministicGuid.Create(ns, $"part:{part.Id}")))
            {
                diagnostics.Add(new Diagnostic("EMD-IMPORT-003", DiagnosticSeverity.Error,
                    $"Duplicate MechMaker part id '{part.Id}' was skipped.", Entity: part.Id));
            }
        }

        var boardIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var boardEntries = new List<Board>();

        foreach (var board in machine.Boards ?? [])
        {
            if (string.IsNullOrWhiteSpace(board.Id))
            {
                diagnostics.Add(new Diagnostic("EMD-IMPORT-003", DiagnosticSeverity.Error,
                    "MechMaker board with an empty id was skipped."));
                continue;
            }

            var boardPartId = DeterministicGuid.Create(ns, $"board:{board.Id}");
            if (!boardIds.TryAdd(board.Id, boardPartId))
            {
                diagnostics.Add(new Diagnostic("EMD-IMPORT-003", DiagnosticSeverity.Error,
                    $"Duplicate MechMaker board id '{board.Id}' was skipped.", Entity: board.Id));
                continue;
            }

            boardEntries.Add(new Board
            {
                Id = DeterministicGuid.Create(ns, $"board-entry:{board.Id}"),
                Key = board.Id,
                PartId = boardPartId,
                Refdes = board.Id
            });
        }

        var partPorts = partIds.Keys.ToDictionary(
            id => id,
            _ => new Dictionary<string, Port>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var boardPorts = boardIds.Keys.ToDictionary(
            id => id,
            _ => new Dictionary<string, Port>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var board in machine.Boards ?? [])
        {
            if (!boardPorts.TryGetValue(board.Id, out var ports))
                continue;

            foreach (var pin in board.Pins ?? [])
            {
                var portId = Slug.Key(pin.Value, "pin");
                ports[portId] = new Port
                {
                    Id = portId,
                    Key = pin.Value,
                    Name = pin.Key,
                    Domain = PortDomain.Control,
                    Kind = "pin",
                    Direction = PortDirection.Inout
                };
            }
        }

        var connections = new List<Connection>();

        foreach (var mechConnection in machine.Connections ?? [])
        {
            if (string.IsNullOrWhiteSpace(mechConnection.Id) ||
                string.IsNullOrWhiteSpace(mechConnection.PartA) ||
                string.IsNullOrWhiteSpace(mechConnection.PartB))
            {
                diagnostics.Add(new Diagnostic("EMD-IMPORT-003", DiagnosticSeverity.Error,
                    "MechMaker connection with missing id/part was skipped."));
                continue;
            }

            if (!partIds.ContainsKey(mechConnection.PartA) ||
                !partIds.ContainsKey(mechConnection.PartB))
            {
                diagnostics.Add(new Diagnostic("EMD-IMPORT-001", DiagnosticSeverity.Error,
                    $"MechMaker connection '{mechConnection.Id}' references a missing part.",
                    Entity: mechConnection.Id));
                continue;
            }

            var portA = EnsureMechanicalPort(partPorts, mechConnection.PartA, mechConnection.ConnectorA);
            var portB = EnsureMechanicalPort(partPorts, mechConnection.PartB, mechConnection.ConnectorB);

            connections.Add(new Connection
            {
                Id = DeterministicGuid.Create(ns, $"connection:{mechConnection.Id}"),
                Key = Slug.Key(mechConnection.Id, "connection"),
                Kind = ConnectionKind.Mate,
                From = new Endpoint { PartId = partIds[mechConnection.PartA], PortId = portA },
                To = new Endpoint { PartId = partIds[mechConnection.PartB], PortId = portB }
            });
        }

        var nets = new Dictionary<string, Net>(StringComparer.Ordinal);

        foreach (var wire in machine.Wiring ?? [])
        {
            if (string.IsNullOrWhiteSpace(wire.Component) || string.IsNullOrWhiteSpace(wire.Board) ||
                string.IsNullOrWhiteSpace(wire.Signal) || string.IsNullOrWhiteSpace(wire.Pin))
            {
                diagnostics.Add(new Diagnostic("EMD-IMPORT-003", DiagnosticSeverity.Error,
                    "MechMaker wiring entry with missing component/board/signal/pin was skipped."));
                continue;
            }

            if (!partIds.ContainsKey(wire.Component))
            {
                diagnostics.Add(new Diagnostic("EMD-IMPORT-001", DiagnosticSeverity.Error,
                    $"MechMaker wiring references missing component '{wire.Component}'.",
                    Entity: wire.Component));
                continue;
            }

            if (!boardIds.ContainsKey(wire.Board))
            {
                diagnostics.Add(new Diagnostic("EMD-IMPORT-001", DiagnosticSeverity.Error,
                    $"MechMaker wiring references missing board '{wire.Board}'.",
                    Entity: wire.Board));
                continue;
            }

            var componentPort = EnsureSignalPort(partPorts, wire.Component, wire.Signal);
            var boardPort = EnsureBoardPinPort(boardPorts, wire.Board, wire.Pin, wire.Signal);

            var netKey = Slug.Key($"{wire.Component}.{wire.Signal}", "net");
            if (!nets.TryGetValue(netKey, out var net))
            {
                net = new Net
                {
                    Id = DeterministicGuid.Create(ns, $"net:{wire.Component}:{wire.Signal}"),
                    Key = netKey,
                    Name = $"{wire.Board}.{wire.Pin}",
                    Class = ClassifySignal(wire.Signal)
                };
                nets[netKey] = net;
            }

            var wireKey = Slug.Key($"{wire.Component}.{wire.Signal}.{wire.Board}.{wire.Pin}", "wire");
            connections.Add(new Connection
            {
                Id = DeterministicGuid.Create(ns, $"wire:{wire.Component}:{wire.Signal}:{wire.Board}:{wire.Pin}:component"),
                Key = $"{wireKey}.component",
                Kind = ConnectionKind.Wire,
                From = new Endpoint { PartId = partIds[wire.Component], PortId = componentPort },
                To = new Endpoint { NetId = net.Id }
            });
            connections.Add(new Connection
            {
                Id = DeterministicGuid.Create(ns, $"wire:{wire.Component}:{wire.Signal}:{wire.Board}:{wire.Pin}:board"),
                Key = $"{wireKey}.board",
                Kind = ConnectionKind.Wire,
                From = new Endpoint { NetId = net.Id },
                To = new Endpoint { PartId = boardIds[wire.Board], PortId = boardPort }
            });
        }

        var parts = new List<Part>();

        foreach (var mechPart in machine.Parts ?? [])
        {
            if (!partIds.TryGetValue(mechPart.Id, out var partId))
                continue;

            var catalogPart = catalog is not null && catalog.TryGet(mechPart.Part, out var found)
                ? found
                : null;

            parts.Add(new Part
            {
                Id = partId,
                Key = Slug.Key(mechPart.Id, "part"),
                Name = catalogPart?.Name ?? mechPart.Id,
                Kind = PartKind.Component,
                Category = catalogPart?.Category ?? InferCategory(mechPart.Part),
                CatalogRef = new PartCatalogRef { Catalog = "mechmaker", Part = mechPart.Part },
                Pose = ToPose(mechPart.Pose),
                DomainRefs = new PartDomainRefs
                {
                    Mechanical = new DomainRef { Tool = "mechmaker", Ref = mechPart.Id }
                },
                Ports = partPorts[mechPart.Id].Values
                    .OrderBy(port => port.Id, StringComparer.Ordinal)
                    .ToList()
            });
        }

        foreach (var board in machine.Boards ?? [])
        {
            if (!boardIds.TryGetValue(board.Id, out var partId))
                continue;

            parts.Add(new Part
            {
                Id = partId,
                Key = Slug.Key(board.Id, "board"),
                Name = string.IsNullOrWhiteSpace(board.Type) ? board.Id : board.Type,
                Kind = PartKind.Board,
                Category = PartCategory.Electronics,
                DomainRefs = new PartDomainRefs
                {
                    Electrical = new DomainRef { Tool = "mechmaker", Ref = board.Id }
                },
                Ports = boardPorts[board.Id].Values
                    .OrderBy(port => port.Id, StringComparer.Ordinal)
                    .ToList()
            });
        }

        var mechanical = partIds.Count > 0
            ? new MechanicalSection { AssemblyRootPartId = partIds.Values.First() }
            : null;
        var electrical = new ElectricalSection
        {
            Boards = boardEntries,
            Nets = nets.Values.OrderBy(net => net.Key, StringComparer.Ordinal).ToList()
        };

        var metadata = new JsonObject
        {
            ["source"] = "mechmaker",
            ["source_name"] = name
        };
        if (!string.IsNullOrWhiteSpace(source))
            metadata["source_path"] = source;
        if (!string.IsNullOrWhiteSpace(machine.SchemaVersion))
            metadata["mechmaker_schema_version"] = machine.SchemaVersion;

        var project = new EmdProject
        {
            Id = DeterministicGuid.Create(ns, $"project:{source ?? name}"),
            Key = Slug.Create(name),
            Name = name,
            Description = $"Imported from MechMaker machine '{name}'.",
            License = "GPL-3.0-only",
            Units = new Units(),
            CoordinateSystem = new CoordinateSystem(),
            Parts = parts,
            Connections = connections,
            Mechanical = mechanical,
            Electrical = electrical,
            Metadata = metadata
        };

        return new MechMakerImportResult(project, diagnostics);
    }

    private static string EnsureMechanicalPort(
        Dictionary<string, Dictionary<string, Port>> partPorts,
        string partId,
        string? connector)
    {
        var ports = partPorts[partId];
        var label = string.IsNullOrWhiteSpace(connector) ? "connector" : connector.Trim();
        var id = Slug.Key(label, "connector");

        if (ports.TryGetValue(id, out var existing) && existing.Domain != PortDomain.Mechanical)
            id = $"mech_{id}";

        ports[id] = new Port
        {
            Id = id,
            Key = label,
            Name = label,
            Domain = PortDomain.Mechanical,
            Kind = "mount",
            Direction = PortDirection.Passive
        };
        return id;
    }

    private static string EnsureSignalPort(
        Dictionary<string, Dictionary<string, Port>> partPorts,
        string partId,
        string signal)
    {
        var ports = partPorts[partId];
        var id = Slug.Key(signal, "signal");

        if (ports.TryGetValue(id, out var existing) && existing.Domain == PortDomain.Mechanical)
            id = $"signal_{id}";

        var domain = IsElectricalSignal(signal) ? PortDomain.Electrical : PortDomain.Control;
        ports[id] = new Port
        {
            Id = id,
            Key = signal.Trim(),
            Name = signal.Trim(),
            Domain = domain,
            Kind = signal.Trim(),
            Direction = PortDirection.Inout,
            Signal = BuildSignal(signal)
        };
        return id;
    }

    private static string EnsureBoardPinPort(
        Dictionary<string, Dictionary<string, Port>> boardPorts,
        string boardId,
        string pin,
        string signal)
    {
        var ports = boardPorts[boardId];
        var id = Slug.Key(pin, "pin");
        ports.TryGetValue(id, out var existing);

        var domain = IsElectricalSignal(signal) ? PortDomain.Electrical : PortDomain.Control;
        ports[id] = new Port
        {
            Id = id,
            Key = pin.Trim(),
            Name = existing?.Name ?? pin.Trim(),
            Domain = domain,
            Kind = "pin",
            Direction = PortDirection.Inout,
            Signal = BuildSignal(signal)
        };
        return id;
    }

    private static Pose? ToPose(MechPose? pose) =>
        pose is null ? null : new Pose { Pos = pose.Pos, Rot = pose.Rot };

    private static PartCategory InferCategory(string? catalogPartId)
    {
        var id = catalogPartId?.ToLowerInvariant() ?? "";
        if (id.Contains("stepper") || id.Contains("motor") || id.Contains("servo"))
            return PartCategory.Motor;
        if (id.Contains("pulley") || id.Contains("belt") || id.Contains("gear") || id.Contains("leadscrew"))
            return PartCategory.Transmission;
        if (id.Contains("rail") || id.Contains("carriage"))
            return PartCategory.LinearMotion;
        if (id.Contains("endstop"))
            return PartCategory.Sensor;
        if (id.Contains("beam") || id.Contains("plate") || id.Contains("bracket") ||
            id.Contains("frame") || id.Contains("rod"))
            return PartCategory.Structure;
        if (id.Contains("board"))
            return PartCategory.Electronics;
        return PartCategory.Other;
    }

    private static NetClass ClassifySignal(string signal)
    {
        var normalized = signal.ToLowerInvariant();
        if (normalized is "ground" or "gnd")
            return NetClass.Ground;
        if (normalized is "power" or "vmot" or "vcc" or "heater")
            return NetClass.Power;
        if (normalized is "can" or "uart" or "i2c" or "spi" or "usb")
            return NetClass.Bus;
        return NetClass.Signal;
    }

    private static bool IsElectricalSignal(string signal) =>
        signal.ToLowerInvariant() is "power" or "ground" or "gnd" or "vmot" or "vcc" or "heater";

    private static Signal BuildSignal(string signal)
    {
        var type = signal.ToLowerInvariant() switch
        {
            "power" or "vmot" or "vcc" or "heater" => SignalType.Power,
            "ground" or "gnd" => SignalType.Ground,
            "pwm" => SignalType.Pwm,
            _ => SignalType.Digital
        };

        return new Signal { Type = type };
    }

    private sealed record MechMachine
    {
        public string? SchemaVersion { get; init; }

        public string? Name { get; init; }

        public List<MechPart>? Parts { get; init; }

        public List<MechConnection>? Connections { get; init; }

        public List<MechBoard>? Boards { get; init; }

        public List<MechWire>? Wiring { get; init; }
    }

    private sealed record MechPart
    {
        public string Id { get; init; } = "";

        public string Part { get; init; } = "";

        public MechPose? Pose { get; init; }
    }

    private sealed record MechPose
    {
        public double[]? Pos { get; init; }

        public double[]? Rot { get; init; }
    }

    private sealed record MechConnection
    {
        public string Id { get; init; } = "";

        public string PartA { get; init; } = "";

        public string ConnectorA { get; init; } = "";

        public string PartB { get; init; } = "";

        public string ConnectorB { get; init; } = "";
    }

    private sealed record MechBoard
    {
        public string Id { get; init; } = "";

        public string? Type { get; init; }

        public Dictionary<string, string>? Pins { get; init; }
    }

    private sealed record MechWire
    {
        public string Component { get; init; } = "";

        public string Signal { get; init; } = "";

        public string Board { get; init; } = "";

        public string Pin { get; init; } = "";
    }
}
