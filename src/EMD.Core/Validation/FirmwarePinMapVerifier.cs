using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Core.Validation;

/// <summary>Cross-checks firmware pin maps against the board/net graph.</summary>
public sealed class FirmwarePinMapVerifier
{
    public static FirmwarePinMapVerifier Default { get; } = new();

    public IReadOnlyList<Diagnostic> Validate(EmdProject project)
    {
        var diagnostics = new List<Diagnostic>();
        var parts = new Dictionary<Guid, Part>();
        foreach (var part in project.Parts)
            parts.TryAdd(part.Id, part);
        var nets = new Dictionary<Guid, Net>();
        foreach (var net in project.Electrical?.Nets ?? [])
            nets.TryAdd(net.Id, net);
        var boardEndpoints = new Dictionary<Guid, List<(Guid PartId, string PortId)>>();
        foreach (var connection in project.Connections)
        {
            Add(connection.From, connection.To);
            Add(connection.To, connection.From);
        }

        foreach (var firmware in project.Firmware?.Projects ?? [])
        {
            if (firmware.PinMap.Count == 0)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-FW-006",
                    DiagnosticSeverity.Warning,
                    $"Firmware '{firmware.Key}' has an empty pin map.",
                    Entity: firmware.Key));
            }

            if (!parts.TryGetValue(firmware.BoardPartId, out var board) ||
                board.Kind != PartKind.Board)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-FW-007",
                    DiagnosticSeverity.Warning,
                    $"Firmware '{firmware.Key}' board is not a board part.",
                    Entity: firmware.Key));
            }

            foreach (var pin in firmware.PinMap)
            {
                if (string.IsNullOrWhiteSpace(pin.McuPin))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-FW-004",
                        DiagnosticSeverity.Error,
                        $"Firmware '{firmware.Key}' has a pin binding without an MCU pin.",
                        Entity: firmware.Key));
                }

                if (pin.BoardPartId is { } pinBoard && pinBoard != firmware.BoardPartId)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-FW-004",
                        DiagnosticSeverity.Error,
                        $"Firmware '{firmware.Key}' pin '{pin.McuPin}' targets a different board.",
                        Entity: firmware.Key));
                }

                if (!nets.ContainsKey(pin.NetId))
                    continue;

                if (boardEndpoints.TryGetValue(pin.NetId, out var endpoints) &&
                    endpoints.Count(endpoint => endpoint.PartId == firmware.BoardPartId) > 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-FW-005",
                        DiagnosticSeverity.Error,
                        $"Firmware '{firmware.Key}' pin '{pin.McuPin}' maps multiple ports on its MCU board.",
                        Entity: firmware.Key));
                }
            }
        }

        return diagnostics;

        void Add(Endpoint netSide, Endpoint partSide)
        {
            if (netSide.NetId is not { } netId || partSide.PartId is not { } partId ||
                partSide.PortId is not { } portId)
                return;
            if (!parts.TryGetValue(partId, out var part) || part.Kind != PartKind.Board)
                return;
            if (!boardEndpoints.TryGetValue(netId, out var endpoints))
                boardEndpoints[netId] = endpoints = [];
            if (endpoints.All(endpoint => endpoint.PartId != partId || endpoint.PortId != portId))
                endpoints.Add((partId, portId));
        }
    }
}
