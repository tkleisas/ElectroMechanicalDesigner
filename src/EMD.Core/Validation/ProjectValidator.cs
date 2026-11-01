using Emd.Core.Diagnostics;
using Emd.Core.Model;

namespace Emd.Core.Validation;

public sealed class ProjectValidator
{
    public static ProjectValidator Default { get; } = new();

    public IReadOnlyList<Diagnostic> Validate(EmdProject project)
    {
        var diagnostics = new List<Diagnostic>();

        var parts = Index(project.Parts, part => part.Id, "part", diagnostics);
        var connections = Index(project.Connections, connection => connection.Id, "connection", diagnostics);
        var artifacts = Index(project.Artifacts, artifact => artifact.Id, "artifact", diagnostics);
        var nets = Index(project.Electrical?.Nets ?? [], net => net.Id, "net", diagnostics);
        var boards = Index(project.Electrical?.Boards ?? [], board => board.Id, "board", diagnostics);
        var firmwareProjects = Index(project.Firmware?.Projects ?? [], firmware => firmware.Id, "firmware project", diagnostics);
        var behaviorModels = Index(project.Behavior?.Models ?? [], model => model.Id, "behavior model", diagnostics);
        var simSystems = Index(project.Simulation?.Systems ?? [], system => system.Id, "simulation system", diagnostics);
        var scenarios = Index(project.Simulation?.Scenarios ?? [], scenario => scenario.Id, "scenario", diagnostics);
        var processes = Index(project.Manufacturing?.Processes ?? [], process => process.Id, "manufacturing process", diagnostics);
        _ = Index(project.Mechanical?.Joints ?? [], joint => joint.Id, "mechanical joint", diagnostics);

        ValidateDuplicatePorts(project, diagnostics);
        ValidateConnectionEndpoints(project, parts, nets, artifacts, diagnostics);
        ValidateArtifactInputs(project, artifacts, diagnostics);
        ValidateDomainReferences(project, artifacts, diagnostics);
        ValidateElectricalRefdes(project, diagnostics);
        ValidatePartGeometry(project, artifacts, diagnostics);
        ValidateMechanical(project, parts, artifacts, diagnostics);
        ValidateElectrical(project, parts, artifacts, diagnostics);
        ValidateElectricalPortConnectivity(project, diagnostics);
        ValidateElectricalVoltages(project, parts, nets, diagnostics);
        ValidateMotorTopology(project, diagnostics);
        ValidateFirmware(project, parts, nets, artifacts, diagnostics);
        ValidateBehavior(project, parts, artifacts, diagnostics);
        ValidateSimulation(project, connections, artifacts, diagnostics);
        ValidateSimulationBindings(project, diagnostics);
        ValidateManufacturing(project, parts, artifacts, diagnostics);
        diagnostics.AddRange(MechanicalValidator.Default.Validate(project));

        _ = boards;
        _ = firmwareProjects;
        _ = behaviorModels;
        _ = simSystems;
        _ = scenarios;
        _ = processes;

        return diagnostics;
    }

    private static void ValidateDuplicatePorts(EmdProject project, List<Diagnostic> diagnostics)
    {
        foreach (var part in project.Parts)
        {
            foreach (var group in part.Ports.GroupBy(port => port.Id).Where(group => group.Count() > 1))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-PORT-001",
                    DiagnosticSeverity.Error,
                    $"Part '{Entity(part)}' declares port '{group.Key}' more than once.",
                    Entity: Entity(part)));
            }
        }
    }

    private static void ValidateConnectionEndpoints(
        EmdProject project,
        IReadOnlyDictionary<Guid, Part> parts,
        IReadOnlyDictionary<Guid, Net> nets,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        List<Diagnostic> diagnostics)
    {
        var netNodeCounts = new Dictionary<Guid, int>();

        foreach (var connection in project.Connections)
        {
            ValidateEndpoint(connection, connection.From, "from");
            ValidateEndpoint(connection, connection.To, "to");

            CountNet(connection.From.NetId);
            CountNet(connection.To.NetId);
        }

        foreach (var net in project.Electrical?.Nets ?? [])
        {
            if (!netNodeCounts.TryGetValue(net.Id, out var count) || count < 2)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-ELEC-001",
                    DiagnosticSeverity.Error,
                    $"Net '{Entity(net)}' has {count} connected endpoint(s); a net needs at least two.",
                    Entity: Entity(net)));
            }
        }

        void ValidateEndpoint(Connection connection, Endpoint endpoint, string side)
        {
            var connectionName = Entity(connection);

            if (endpoint.PartId is { } partId)
            {
                if (!parts.TryGetValue(partId, out var part))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-XREF-001",
                        DiagnosticSeverity.Error,
                        $"Connection '{connectionName}' {side} references missing part '{partId}'.",
                        Entity: connectionName));
                }
                else if (endpoint.PortId is { } portId && part.Ports.All(port => port.Id != portId))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-XREF-001",
                        DiagnosticSeverity.Error,
                        $"Connection '{connectionName}' {side} references missing port '{portId}' on part '{Entity(part)}'.",
                        Entity: connectionName));
                }
            }

            if (endpoint.NetId is { } netId && !nets.ContainsKey(netId))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-XREF-001",
                    DiagnosticSeverity.Error,
                    $"Connection '{connectionName}' {side} references missing net '{netId}'.",
                    Entity: connectionName));
            }

            if (endpoint.ArtifactId is { } artifactId && !artifacts.ContainsKey(artifactId))
            {
                var code = connection.Kind == ConnectionKind.SimulationBinding
                    ? "EMD-SIM-001"
                    : "EMD-ART-001";
                diagnostics.Add(new Diagnostic(
                    code,
                    DiagnosticSeverity.Error,
                    $"Connection '{connectionName}' {side} references missing artifact '{artifactId}'.",
                    Entity: connectionName));
            }
        }

        void CountNet(Guid? netId)
        {
            if (netId is not { } id)
                return;

            netNodeCounts[id] = netNodeCounts.TryGetValue(id, out var count) ? count + 1 : 1;
        }
    }

    private static void ValidateArtifactInputs(
        EmdProject project,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        List<Diagnostic> diagnostics)
    {
        foreach (var artifact in project.Artifacts)
        {
            foreach (var input in artifact.Inputs)
            {
                RequireArtifact(input, artifacts, $"Artifact '{Entity(artifact)}' input", diagnostics);
            }
        }
    }

    private static void ValidateDomainReferences(
        EmdProject project,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        List<Diagnostic> diagnostics)
    {
        foreach (var catalog in project.Catalogs ?? [])
        {
            RequireArtifact(catalog.ArtifactId, artifacts, $"Catalog '{catalog.Id}'", diagnostics);
        }

        foreach (var part in project.Parts)
        {
            var refs = part.DomainRefs;
            if (refs is null)
                continue;

            RequireArtifact(refs.Mechanical?.ArtifactId, artifacts, $"Part '{Entity(part)}' mechanical ref", diagnostics);
            RequireArtifact(refs.Electrical?.ArtifactId, artifacts, $"Part '{Entity(part)}' electrical ref", diagnostics);
            RequireArtifact(refs.Firmware?.ArtifactId, artifacts, $"Part '{Entity(part)}' firmware ref", diagnostics);
            RequireArtifact(refs.Behavior?.ArtifactId, artifacts, $"Part '{Entity(part)}' behavior ref", diagnostics);
            RequireArtifact(refs.Simulation?.ArtifactId, artifacts, $"Part '{Entity(part)}' simulation ref", diagnostics);
            RequireArtifact(refs.Manufacturing?.ArtifactId, artifacts, $"Part '{Entity(part)}' manufacturing ref", diagnostics);
        }
    }

    private static void ValidateElectricalRefdes(
        EmdProject project,
        List<Diagnostic> diagnostics)
    {
        var refs = new Dictionary<(string Tool, string Ref), List<Part>>();
        foreach (var part in project.Parts)
        {
            var electrical = part.DomainRefs?.Electrical;
            if (electrical is null || string.IsNullOrWhiteSpace(electrical.Ref))
                continue;

            var key = (electrical.Tool, electrical.Ref);
            if (!refs.TryGetValue(key, out var parts))
            {
                parts = [];
                refs[key] = parts;
            }

            parts.Add(part);
        }

        foreach (var (key, parts) in refs)
        {
            if (parts.Count < 2)
                continue;

            diagnostics.Add(new Diagnostic(
                "EMD-XREF-002",
                DiagnosticSeverity.Error,
                $"Electrical refdes '{key.Ref}' is used by multiple parts: " +
                string.Join(", ", parts.Select(Entity)) + ".",
                Entity: key.Ref));
        }
    }

    private static void ValidateElectricalVoltages(
        EmdProject project,
        IReadOnlyDictionary<Guid, Part> parts,
        IReadOnlyDictionary<Guid, Net> nets,
        List<Diagnostic> diagnostics)
    {
        var tolerance = project.Validation?.Tolerances?.GetValueOrDefault("voltage_v") ?? 0.5;

        foreach (var connection in project.Connections)
        {
            Check(connection.From, connection.To);
            Check(connection.To, connection.From);
        }

        void Check(Endpoint netSide, Endpoint partSide)
        {
            if (netSide.NetId is not { } netId ||
                partSide.PartId is not { } partId ||
                partSide.PortId is not { } portId)
            {
                return;
            }

            if (!nets.TryGetValue(netId, out var net) || net.VoltageV is not { } netVoltage)
                return;
            if (!parts.TryGetValue(partId, out var part))
                return;

            var port = part.Ports.FirstOrDefault(candidate => candidate.Id == portId);
            if (port?.Signal?.VoltageV is not { } portVoltage)
                return;

            if (Math.Abs(netVoltage - portVoltage) <= tolerance)
                return;

            diagnostics.Add(new Diagnostic(
                "EMD-ELEC-005",
                DiagnosticSeverity.Error,
                $"Net '{net.Key}' is {netVoltage} V but port '{portId}' on part " +
                $"'{Entity(part)}' is {portVoltage} V.",
                Entity: $"{Entity(part)}.{portId}"));
        }
    }

    private static void ValidateMotorTopology(
        EmdProject project,
        List<Diagnostic> diagnostics)
    {
        var boardPartIds = new HashSet<Guid>();
        foreach (var board in project.Electrical?.Boards ?? [])
            boardPartIds.Add(board.PartId);
        foreach (var part in project.Parts)
        {
            if (part.Kind == PartKind.Board)
                boardPartIds.Add(part.Id);
        }

        var netEndpoints = BuildNetEndpoints(project);
        var partsById = new Dictionary<Guid, Part>();
        foreach (var part in project.Parts)
            partsById.TryAdd(part.Id, part);
        var motorPartIds = project.Parts
            .Where(part => part.Category == PartCategory.Motor)
            .Select(part => part.Id)
            .ToHashSet();

        foreach (var (netId, endpoints) in netEndpoints)
        {
            var motorPhases = endpoints.Count(endpoint =>
                motorPartIds.Contains(endpoint.PartId) &&
                partsById.TryGetValue(endpoint.PartId, out var part) &&
                part.Ports.Any(port => port.Id == endpoint.PortId && port.Domain == PortDomain.Electrical));

            if (motorPhases > 1)
            {
                var netName = project.Electrical?.Nets.FirstOrDefault(net => net.Id == netId)?.Key
                    ?? netId.ToString();
                diagnostics.Add(new Diagnostic(
                    "EMD-ELEC-007",
                    DiagnosticSeverity.Error,
                    $"Net '{netName}' connects {motorPhases} motor phases; expected one motor per phase net.",
                    Entity: netName));
            }
        }

        foreach (var part in project.Parts)
        {
            if (part.Category != PartCategory.Motor)
                continue;

            var phasePorts = part.Ports
                .Where(port => port.Domain == PortDomain.Electrical)
                .ToList();
            var phaseNets = new HashSet<Guid>();
            var driverEndpoints = new HashSet<(Guid PartId, string PortId)>();
            var hasMappingMismatch = false;
            var hasMultipleDriverEndpoints = false;

            foreach (var port in phasePorts)
            {
                var nets = NetsOfPort(project, part.Id, port.Id).Distinct().ToList();
                if (nets.Count == 0)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-ELEC-006",
                        DiagnosticSeverity.Warning,
                        $"Motor phase '{port.Id}' on part '{Entity(part)}' is not connected to a net.",
                        Entity: $"{Entity(part)}.{port.Id}"));
                    continue;
                }

                if (nets.Count > 1)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-ELEC-009",
                        DiagnosticSeverity.Error,
                        $"Motor phase '{port.Id}' on part '{Entity(part)}' maps to multiple nets.",
                        Entity: $"{Entity(part)}.{port.Id}"));
                    continue;
                }

                var netId = nets[0];
                phaseNets.Add(netId);
                if (!netEndpoints.TryGetValue(netId, out var endpoints))
                    continue;

                var motorEndpoints = endpoints.Count(endpoint => endpoint.PartId == part.Id);
                if (motorEndpoints > 1)
                    continue;

                var boardEndpoints = endpoints
                    .Where(endpoint => boardPartIds.Contains(endpoint.PartId))
                    .ToList();
                if (boardEndpoints.Count > 1)
                    hasMultipleDriverEndpoints = true;

                var hasBoardEndpoint = boardEndpoints.Count > 0;
                if (!hasBoardEndpoint)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-ELEC-006",
                        DiagnosticSeverity.Warning,
                        $"Motor phase '{port.Id}' on part '{Entity(part)}' has no board/driver endpoint.",
                        Entity: $"{Entity(part)}.{port.Id}"));
                }

                foreach (var driver in boardEndpoints)
                {
                    driverEndpoints.Add(driver);
                    var motorPhase = PhaseToken(port.Id);
                    var driverPhase = PhaseToken(driver.PortId);
                    if (motorPhase is not null && driverPhase is not null &&
                        !string.Equals(motorPhase, driverPhase, StringComparison.Ordinal))
                    {
                        hasMappingMismatch = true;
                    }
                }
            }

            if (phaseNets.Count != phasePorts.Count || driverEndpoints.Count != phasePorts.Count)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-ELEC-008",
                    DiagnosticSeverity.Error,
                    $"Motor '{Entity(part)}' has {phasePorts.Count} phase port(s), " +
                    $"{phaseNets.Count} phase net(s), and {driverEndpoints.Count} driver endpoint(s).",
                    Entity: Entity(part)));
            }

            if (hasMappingMismatch)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-ELEC-009",
                    DiagnosticSeverity.Error,
                    $"Motor '{Entity(part)}' phase mapping does not match the driver phase names.",
                    Entity: Entity(part)));
            }

            if (hasMultipleDriverEndpoints)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-ELEC-010",
                    DiagnosticSeverity.Error,
                    $"Motor '{Entity(part)}' phase net has multiple driver endpoints.",
                    Entity: Entity(part)));
            }
        }
    }

    private static string? PhaseToken(string portId)
    {
        var id = portId.Trim().ToLowerInvariant();
        if (id.Length == 0)
            return null;

        var last = id[^1];
        if (!char.IsLetter(last))
            return null;

        var prefix = id[..^1].TrimEnd('_', '-', '.');
        if (prefix is "phase" or "ph" ||
            (prefix.Length > 1 && prefix[0] == 'm' &&
             prefix[1..].All(char.IsDigit)))
        {
            return last.ToString();
        }

        return null;
    }

    private static Dictionary<Guid, List<(Guid PartId, string PortId)>> BuildNetEndpoints(EmdProject project)
    {
        var endpointsByNet = new Dictionary<Guid, List<(Guid PartId, string PortId)>>();
        foreach (var connection in project.Connections)
        {
            Add(connection.From, connection.To);
            Add(connection.To, connection.From);
        }

        return endpointsByNet;

        void Add(Endpoint netSide, Endpoint partSide)
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

    private static IEnumerable<Guid> NetsOfPort(EmdProject project, Guid partId, string portId)
    {
        foreach (var connection in project.Connections)
        {
            if (connection.From.PartId == partId && connection.From.PortId == portId &&
                connection.To.NetId is { } fromNet)
            {
                yield return fromNet;
            }

            if (connection.To.PartId == partId && connection.To.PortId == portId &&
                connection.From.NetId is { } toNet)
            {
                yield return toNet;
            }
        }
    }

    private static void ValidatePartGeometry(
        EmdProject project,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        List<Diagnostic> diagnostics)
    {
        foreach (var part in project.Parts)
        {
            var geometry = part.Geometry;
            if (geometry is null)
                continue;

            if (geometry.Kind == GeometryKind.Mesh)
            {
                if (geometry.MeshArtifactId is null)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-MECH-004",
                        DiagnosticSeverity.Error,
                        $"Part '{Entity(part)}' has mesh geometry without a mesh artifact.",
                        Entity: Entity(part)));
                }
                else
                {
                    RequireArtifact(
                        geometry.MeshArtifactId,
                        artifacts,
                        $"Part '{Entity(part)}' mesh geometry",
                        diagnostics);
                }
            }
            else if (geometry.Size is not { Length: 3 })
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-004",
                    DiagnosticSeverity.Error,
                    $"Part '{Entity(part)}' {geometry.Kind} geometry needs a three-component size.",
                    Entity: Entity(part)));
            }
        }
    }

    private static void ValidateMechanical(
        EmdProject project,
        IReadOnlyDictionary<Guid, Part> parts,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        List<Diagnostic> diagnostics)
    {
        var mechanical = project.Mechanical;
        if (mechanical is null)
            return;

        RequireArtifact(mechanical.SourceArtifactId, artifacts, "Mechanical source", diagnostics);

        if (mechanical.AssemblyRootPartId is { } rootId && !parts.ContainsKey(rootId))
        {
            diagnostics.Add(new Diagnostic(
                "EMD-MECH-001",
                DiagnosticSeverity.Error,
                $"Mechanical assembly root references missing part '{rootId}'.",
                Entity: "mechanical.assembly_root_part_id"));
        }

        foreach (var (name, artifactId) in mechanical.Exports ?? [])
            RequireArtifact(artifactId, artifacts, $"Mechanical export '{name}'", diagnostics);

        RequireArtifact(mechanical.Simulation?.ArtifactId, artifacts, "Mechanical simulation", diagnostics);

        foreach (var binding in mechanical.Simulation?.JointBindings ?? [])
        {
            if (!parts.TryGetValue(binding.PartId, out var part))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-001",
                    DiagnosticSeverity.Error,
                    $"Joint binding '{binding.Joint}' references missing part '{binding.PartId}'.",
                    Entity: binding.Joint));
                continue;
            }

            if (binding.PortId is { } portId && part.Ports.All(port => port.Id != portId))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-001",
                    DiagnosticSeverity.Error,
                    $"Joint binding '{binding.Joint}' references missing port '{portId}' on part '{Entity(part)}'.",
                    Entity: binding.Joint));
            }
        }

        foreach (var joint in mechanical.Joints ?? [])
        {
            var jointName = Entity(joint);

            if (!parts.ContainsKey(joint.ParentPartId))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-004",
                    DiagnosticSeverity.Error,
                    $"Joint '{jointName}' references missing parent part '{joint.ParentPartId}'.",
                    Entity: jointName));
            }

            if (!parts.ContainsKey(joint.ChildPartId))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-004",
                    DiagnosticSeverity.Error,
                    $"Joint '{jointName}' references missing child part '{joint.ChildPartId}'.",
                    Entity: jointName));
            }

            if (joint.ParentPartId == joint.ChildPartId)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-004",
                    DiagnosticSeverity.Error,
                    $"Joint '{jointName}' connects part '{joint.ParentPartId}' to itself.",
                    Entity: jointName));
            }

            if (joint.Type is MechanicalJointType.Hinge or MechanicalJointType.Slide)
            {
                if (joint.Axis is not { Length: 3 } axis ||
                    (axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2]) < 1e-18)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-MECH-004",
                        DiagnosticSeverity.Error,
                        $"Joint '{jointName}' is a {joint.Type} joint but has no usable axis.",
                        Entity: jointName));
                }
            }

            if (joint.Range is { Length: 2 } range && range[0] > range[1])
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MECH-004",
                    DiagnosticSeverity.Error,
                    $"Joint '{jointName}' has a range whose minimum is greater than its maximum.",
                    Entity: jointName));
            }
        }
    }

    private static void ValidateElectrical(
        EmdProject project,
        IReadOnlyDictionary<Guid, Part> parts,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        List<Diagnostic> diagnostics)
    {
        var electrical = project.Electrical;
        if (electrical is null)
            return;

        RequireArtifact(electrical.ProjectArtifactId, artifacts, "KiCad project", diagnostics);
        RequireArtifact(electrical.SchematicArtifactId, artifacts, "Schematic", diagnostics);
        RequireArtifact(electrical.PcbArtifactId, artifacts, "PCB", diagnostics);
        RequireArtifact(electrical.NetlistArtifactId, artifacts, "Netlist", diagnostics);
        RequireArtifact(electrical.SpiceArtifactId, artifacts, "SPICE netlist", diagnostics);
        RequireArtifact(electrical.BomArtifactId, artifacts, "Electrical BOM", diagnostics);
        RequireArtifact(electrical.BoardStepArtifactId, artifacts, "Board STEP", diagnostics);

        foreach (var board in electrical.Boards)
        {
            if (!parts.TryGetValue(board.PartId, out var part))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-ELEC-002",
                    DiagnosticSeverity.Error,
                    $"Board '{Entity(board)}' references missing part '{board.PartId}'.",
                    Entity: Entity(board)));
            }
            else if (part.Kind != PartKind.Board)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-ELEC-002",
                    DiagnosticSeverity.Error,
                    $"Board '{Entity(board)}' references part '{Entity(part)}' of kind '{part.Kind}', expected 'board'.",
                    Entity: Entity(board)));
            }

            RequireArtifact(board.OutlineArtifactId, artifacts, $"Board '{Entity(board)}' outline", diagnostics);
        }
    }

    private static void ValidateElectricalPortConnectivity(
        EmdProject project,
        List<Diagnostic> diagnostics)
    {
        var connected = new HashSet<(Guid PartId, string PortId)>();
        foreach (var connection in project.Connections)
        {
            if (connection.From.PartId is { } fromPart && connection.From.PortId is { } fromPort)
                connected.Add((fromPart, fromPort));
            if (connection.To.PartId is { } toPart && connection.To.PortId is { } toPort)
                connected.Add((toPart, toPort));
        }

        foreach (var part in project.Parts)
        {
            foreach (var port in part.Ports)
            {
                if (port.Domain is not (PortDomain.Electrical or PortDomain.Control))
                    continue;
                if (connected.Contains((part.Id, port.Id)))
                    continue;

                diagnostics.Add(new Diagnostic(
                    "EMD-ELEC-004",
                    DiagnosticSeverity.Warning,
                    $"Port '{port.Id}' on part '{Entity(part)}' is not connected to any net.",
                    Entity: $"{Entity(part)}.{port.Id}"));
            }
        }
    }

    private static void ValidateFirmware(
        EmdProject project,
        IReadOnlyDictionary<Guid, Part> parts,
        IReadOnlyDictionary<Guid, Net> nets,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        List<Diagnostic> diagnostics)
    {
        var portNets = new HashSet<(Guid PartId, string PortId, Guid NetId)>();
        foreach (var connection in project.Connections)
        {
            AddPortNet(connection.From, connection.To);
            AddPortNet(connection.To, connection.From);
        }

        foreach (var firmware in project.Firmware?.Projects ?? [])
        {
            var assignedPins = new HashSet<(Guid? Board, string Pin)>();
            RequireArtifact(firmware.SourceArtifactId, artifacts, $"Firmware '{Entity(firmware)}' source", diagnostics);
            RequireArtifact(firmware.BinaryArtifactId, artifacts, $"Firmware '{Entity(firmware)}' binary", diagnostics);

            if (!parts.TryGetValue(firmware.BoardPartId, out var board))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-FW-001",
                    DiagnosticSeverity.Error,
                    $"Firmware '{Entity(firmware)}' references missing board part '{firmware.BoardPartId}'.",
                    Entity: Entity(firmware)));
            }

            foreach (var pin in firmware.PinMap)
            {
                if (!assignedPins.Add((pin.BoardPartId, pin.McuPin)))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-FW-002",
                        DiagnosticSeverity.Error,
                        $"Firmware '{Entity(firmware)}' assigns MCU pin '{pin.McuPin}' more than once.",
                        Entity: Entity(firmware)));
                }

                if (!nets.ContainsKey(pin.NetId))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-FW-001",
                        DiagnosticSeverity.Error,
                        $"Firmware '{Entity(firmware)}' pin '{pin.McuPin}' references missing net '{pin.NetId}'.",
                        Entity: Entity(firmware)));
                }

                if (pin.BoardPartId is { } boardPartId)
                {
                    if (!parts.TryGetValue(boardPartId, out var pinBoard))
                    {
                        diagnostics.Add(new Diagnostic(
                            "EMD-FW-001",
                            DiagnosticSeverity.Error,
                            $"Firmware '{Entity(firmware)}' pin '{pin.McuPin}' references missing board part '{boardPartId}'.",
                            Entity: Entity(firmware)));
                    }
                    else if (pin.PortId is { } portId && pinBoard.Ports.All(port => port.Id != portId))
                    {
                        diagnostics.Add(new Diagnostic(
                            "EMD-FW-001",
                            DiagnosticSeverity.Error,
                            $"Firmware '{Entity(firmware)}' pin '{pin.McuPin}' references missing port '{portId}' on board '{Entity(pinBoard)}'.",
                            Entity: Entity(firmware)));
                    }
                    else if (pin.PortId is { } pinPortId &&
                             nets.ContainsKey(pin.NetId) &&
                             !portNets.Contains((boardPartId, pinPortId, pin.NetId)))
                    {
                        diagnostics.Add(new Diagnostic(
                            "EMD-FW-003",
                            DiagnosticSeverity.Error,
                            $"Firmware '{Entity(firmware)}' pin '{pin.McuPin}' maps net '{pin.NetId}' " +
                            $"but board port '{pinPortId}' is not connected to that net.",
                            Entity: Entity(firmware)));
                    }
                }
            }

            _ = board;
        }

        void AddPortNet(Endpoint netSide, Endpoint partSide)
        {
            if (netSide.NetId is { } netId &&
                partSide.PartId is { } partId &&
                partSide.PortId is { } portId)
            {
                portNets.Add((partId, portId, netId));
            }
        }
    }

    private static void ValidateBehavior(
        EmdProject project,
        IReadOnlyDictionary<Guid, Part> parts,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        List<Diagnostic> diagnostics)
    {
        foreach (var model in project.Behavior?.Models ?? [])
        {
            RequireArtifact(model.ArtifactId, artifacts, $"Behavior model '{Entity(model)}'", diagnostics);

            foreach (var port in (model.Inputs ?? []).Concat(model.Outputs ?? []))
            {
                if (port.PartId is not { } partId)
                    continue;

                if (!parts.TryGetValue(partId, out var part))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-BEHAVIOR-001",
                        DiagnosticSeverity.Error,
                        $"Behavior model '{Entity(model)}' port '{port.Name}' references missing part '{partId}'.",
                        Entity: Entity(model)));
                }
                else if (port.PortId is { } portId && part.Ports.All(candidate => candidate.Id != portId))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-BEHAVIOR-001",
                        DiagnosticSeverity.Error,
                        $"Behavior model '{Entity(model)}' port '{port.Name}' references missing port '{portId}' on part '{Entity(part)}'.",
                        Entity: Entity(model)));
                }
            }
        }
    }

    private static void ValidateSimulation(
        EmdProject project,
        IReadOnlyDictionary<Guid, Connection> connections,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        List<Diagnostic> diagnostics)
    {
        foreach (var system in project.Simulation?.Systems ?? [])
        {
            RequireArtifact(system.ArtifactId, artifacts, $"Simulation system '{Entity(system)}'", diagnostics, "EMD-SIM-001");

            if (system.StepSizeS is <= 0)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-SIM-002",
                    DiagnosticSeverity.Error,
                    $"Simulation system '{Entity(system)}' has a non-positive step size.",
                    Entity: Entity(system)));
            }

            foreach (var bindingId in system.Bindings ?? [])
            {
                if (!connections.TryGetValue(bindingId, out var binding))
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-SIM-001",
                        DiagnosticSeverity.Error,
                        $"Simulation system '{Entity(system)}' references missing binding '{bindingId}'.",
                        Entity: Entity(system)));
                }
                else if (binding.Kind != ConnectionKind.SimulationBinding)
                {
                    diagnostics.Add(new Diagnostic(
                        "EMD-SIM-001",
                        DiagnosticSeverity.Error,
                        $"Simulation system '{Entity(system)}' references connection '{Entity(binding)}' which is not a simulation binding.",
                        Entity: Entity(system)));
                }
            }
        }

        foreach (var scenario in project.Simulation?.Scenarios ?? [])
        {
            RequireArtifact(scenario.ArtifactId, artifacts, $"Scenario '{Entity(scenario)}'", diagnostics);
            if (scenario.TraceArtifactId is { } traceId && !artifacts.ContainsKey(traceId))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-SIM-004",
                    DiagnosticSeverity.Error,
                    $"Scenario '{Entity(scenario)}' references missing trace artifact '{traceId}'.",
                    Entity: Entity(scenario)));
            }
        }
    }

    private static void ValidateSimulationBindings(
        EmdProject project,
        List<Diagnostic> diagnostics)
    {
        var declared = new HashSet<(Guid ArtifactId, string Variable)>();
        foreach (var model in project.Behavior?.Models ?? [])
        {
            foreach (var port in (model.Inputs ?? []).Concat(model.Outputs ?? []))
            {
                if (!string.IsNullOrWhiteSpace(port.Variable))
                    declared.Add((model.ArtifactId, port.Variable));
            }
        }

        foreach (var connection in project.Connections)
        {
            if (connection.Kind != ConnectionKind.SimulationBinding)
                continue;

            Check(connection, connection.From);
            Check(connection, connection.To);
        }

        void Check(Connection connection, Endpoint endpoint)
        {
            if (endpoint.ArtifactId is not { } artifactId ||
                string.IsNullOrWhiteSpace(endpoint.Variable))
            {
                return;
            }

            if (declared.Contains((artifactId, endpoint.Variable)))
                return;

            diagnostics.Add(new Diagnostic(
                "EMD-SIM-003",
                DiagnosticSeverity.Error,
                $"Simulation binding '{connection.Key ?? connection.Id.ToString()}' references " +
                $"undeclared variable '{endpoint.Variable}' for artifact '{artifactId}'.",
                Entity: connection.Key ?? connection.Id.ToString()));
        }
    }

    private static void ValidateManufacturing(
        EmdProject project,
        IReadOnlyDictionary<Guid, Part> parts,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        List<Diagnostic> diagnostics)
    {
        var manufacturing = project.Manufacturing;
        if (manufacturing is null)
            return;

        RequireArtifact(manufacturing.BomArtifactId, artifacts, "Manufacturing BOM", diagnostics, "EMD-MFG-001");

        foreach (var process in manufacturing.Processes)
        {
            RequireArtifact(process.SourceArtifactId, artifacts,
                $"Manufacturing process '{Entity(process)}' source", diagnostics, "EMD-MFG-001");

            foreach (var output in process.OutputArtifactIds ?? [])
            {
                RequireArtifact(output, artifacts,
                    $"Manufacturing process '{Entity(process)}' output", diagnostics, "EMD-MFG-001");
            }

            if (process.MachinePartId is { } machinePartId && !parts.ContainsKey(machinePartId))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-MFG-001",
                    DiagnosticSeverity.Error,
                    $"Manufacturing process '{Entity(process)}' references missing machine part '{machinePartId}'.",
                    Entity: Entity(process)));
            }
        }
    }

    private static void RequireArtifact(
        Guid? artifactId,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        string context,
        List<Diagnostic> diagnostics,
        string code = "EMD-ART-001")
    {
        if (artifactId is { } id && !artifacts.ContainsKey(id))
        {
            diagnostics.Add(new Diagnostic(
                code,
                DiagnosticSeverity.Error,
                $"{context} references missing artifact '{id}'.",
                Entity: context));
        }
    }

    private static Dictionary<Guid, T> Index<T>(
        IEnumerable<T> items,
        Func<T, Guid> idSelector,
        string kind,
        List<Diagnostic> diagnostics)
    {
        var index = new Dictionary<Guid, T>();
        foreach (var item in items)
        {
            var id = idSelector(item);
            if (!index.TryAdd(id, item))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-ID-001",
                    DiagnosticSeverity.Error,
                    $"Duplicate {kind} id '{id}'.",
                    Entity: $"{kind}:{id}"));
            }
        }

        return index;
    }

    private static string Entity(EmdProject project) => project.Key ?? project.Id.ToString();

    private static string Entity(Part part) => part.Key ?? part.Id.ToString();

    private static string Entity(Connection connection) => connection.Key ?? connection.Id.ToString();

    private static string Entity(Artifact artifact) => artifact.Key ?? artifact.Id.ToString();

    private static string Entity(Net net) => net.Key ?? net.Id.ToString();

    private static string Entity(Board board) => board.Key ?? board.Id.ToString();

    private static string Entity(FirmwareProject firmware) => firmware.Key ?? firmware.Id.ToString();

    private static string Entity(BehaviorModel model) => model.Key ?? model.Id.ToString();

    private static string Entity(SimSystem system) => system.Key ?? system.Id.ToString();

    private static string Entity(Scenario scenario) => scenario.Key ?? scenario.Id.ToString();

    private static string Entity(ManufacturingProcess process) => process.Key ?? process.Id.ToString();

    private static string Entity(MechanicalJoint joint) =>
        string.IsNullOrWhiteSpace(joint.Key) ? joint.Id.ToString() : joint.Key;
}
