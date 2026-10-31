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

        ValidateDuplicatePorts(project, diagnostics);
        ValidateConnectionEndpoints(project, parts, nets, artifacts, diagnostics);
        ValidateArtifactInputs(project, artifacts, diagnostics);
        ValidateDomainReferences(project, artifacts, diagnostics);
        ValidateMechanical(project, parts, artifacts, diagnostics);
        ValidateElectrical(project, parts, artifacts, diagnostics);
        ValidateFirmware(project, parts, nets, artifacts, diagnostics);
        ValidateBehavior(project, parts, artifacts, diagnostics);
        ValidateSimulation(project, connections, artifacts, diagnostics);
        ValidateManufacturing(project, parts, artifacts, diagnostics);

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

    private static void ValidateFirmware(
        EmdProject project,
        IReadOnlyDictionary<Guid, Part> parts,
        IReadOnlyDictionary<Guid, Net> nets,
        IReadOnlyDictionary<Guid, Artifact> artifacts,
        List<Diagnostic> diagnostics)
    {
        foreach (var firmware in project.Firmware?.Projects ?? [])
        {
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
                }
            }

            _ = board;
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
            RequireArtifact(scenario.ArtifactId, artifacts, $"Scenario '{Entity(scenario)}'", diagnostics);
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
}
