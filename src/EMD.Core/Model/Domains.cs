using System.Text.Json.Nodes;

namespace Emd.Core.Model;

public sealed record MechanicalSection
{
    public Guid? SourceArtifactId { get; init; }

    public Guid? AssemblyRootPartId { get; init; }

    public List<MechanicalJoint>? Joints { get; init; }

    public Dictionary<string, Guid>? Exports { get; init; }

    public MechanicalSimulation? Simulation { get; init; }

    public List<Material>? Materials { get; init; }

    public Dictionary<string, double>? Tolerances { get; init; }
}

public sealed record MechanicalSimulation
{
    public string? Format { get; init; }

    public Guid? ArtifactId { get; init; }

    public List<JointBinding>? JointBindings { get; init; }
}

public sealed record JointBinding
{
    public string Joint { get; init; } = "";

    public Guid PartId { get; init; }

    public string? PortId { get; init; }

    public string? Actuator { get; init; }

    public string? Axis { get; init; }
}

public sealed record MechanicalJoint
{
    public Guid Id { get; init; }

    public string Key { get; init; } = "";

    public string? Name { get; init; }

    public MechanicalJointType Type { get; init; }

    public Guid ParentPartId { get; init; }

    public Guid ChildPartId { get; init; }

    public double[]? Axis { get; init; }

    public Pose? Anchor { get; init; }

    public double[]? Range { get; init; }

    public string? Actuator { get; init; }

    public double? Damping { get; init; }

    public double? Stiffness { get; init; }

    public JsonObject? Metadata { get; init; }
}

public sealed record ElectricalSection
{
    public Guid? ProjectArtifactId { get; init; }

    public Guid? SchematicArtifactId { get; init; }

    public Guid? PcbArtifactId { get; init; }

    public Guid? NetlistArtifactId { get; init; }

    public Guid? SpiceArtifactId { get; init; }

    public Guid? BomArtifactId { get; init; }

    public Guid? BoardStepArtifactId { get; init; }

    public List<Net> Nets { get; init; } = [];

    public List<Board> Boards { get; init; } = [];

    public JsonObject? Rules { get; init; }
}

public sealed record Net
{
    public Guid Id { get; init; }

    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    public NetClass Class { get; init; }

    public double? VoltageV { get; init; }

    public double? CurrentA { get; init; }

    public string? Protocol { get; init; }

    public JsonObject? Metadata { get; init; }
}

public sealed record Board
{
    public Guid Id { get; init; }

    public string? Key { get; init; }

    public Guid PartId { get; init; }

    public string? Refdes { get; init; }

    public string? Stackup { get; init; }

    public int? Layers { get; init; }

    public Guid? OutlineArtifactId { get; init; }
}

public sealed record FirmwareSection
{
    public List<FirmwareProject> Projects { get; init; } = [];
}

public sealed record FirmwareProject
{
    public Guid Id { get; init; }

    public string Key { get; init; } = "";

    public string? Name { get; init; }

    public Guid SourceArtifactId { get; init; }

    public BuildSystem? BuildSystem { get; init; }

    public Guid BoardPartId { get; init; }

    public string? Mcu { get; init; }

    public Guid? BinaryArtifactId { get; init; }

    public string? RenodePlatform { get; init; }

    public string? BuildCommand { get; init; }

    public List<PinBinding> PinMap { get; init; } = [];
}

public sealed record PinBinding
{
    public string McuPin { get; init; } = "";

    public Guid NetId { get; init; }

    public Guid? BoardPartId { get; init; }

    public string? PortId { get; init; }

    public string? Function { get; init; }
}

public sealed record BehaviorSection
{
    public List<BehaviorModel> Models { get; init; } = [];
}

public sealed record BehaviorModel
{
    public Guid Id { get; init; }

    public string Key { get; init; } = "";

    public string? Name { get; init; }

    public ModelKind Kind { get; init; }

    public Guid ArtifactId { get; init; }

    public JsonObject? Parameters { get; init; }

    public List<ModelPort>? Inputs { get; init; }

    public List<ModelPort>? Outputs { get; init; }
}

public sealed record ModelPort
{
    public string Name { get; init; } = "";

    public string? Unit { get; init; }

    public Guid? PartId { get; init; }

    public string? PortId { get; init; }

    public string? Variable { get; init; }
}

public sealed record SimulationSection
{
    public SimulationFidelity? Fidelity { get; init; }

    public List<SimSystem> Systems { get; init; } = [];

    public List<Scenario> Scenarios { get; init; } = [];
}

public sealed record SimSystem
{
    public Guid Id { get; init; }

    public string Key { get; init; } = "";

    public string? Name { get; init; }

    public SimulationStandard Standard { get; init; }

    public Guid? ArtifactId { get; init; }

    public SimulationMaster Master { get; init; }

    public double? StepSizeS { get; init; }

    public double? StartTimeS { get; init; }

    public double? StopTimeS { get; init; }

    public SimulationSolver? Solver { get; init; }

    public List<Guid>? Bindings { get; init; }
}

public sealed record SimulationSolver
{
    public string? Name { get; init; }

    public double? Tolerance { get; init; }
}

public sealed record Scenario
{
    public Guid Id { get; init; }

    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    public Guid? ArtifactId { get; init; }

    public Guid? TraceArtifactId { get; init; }

    public double? DurationS { get; init; }

    public long? Seed { get; init; }

    public List<Assertion>? Assertions { get; init; }

    public List<string>? Record { get; init; }
}

public sealed record Assertion
{
    public string Name { get; init; } = "";

    public string? Expression { get; init; }

    public string? Unit { get; init; }

    public double? Tolerance { get; init; }

    public string? Severity { get; init; }
}

public sealed record ManufacturingSection
{
    public Guid? BomArtifactId { get; init; }

    public List<ManufacturingProcess> Processes { get; init; } = [];
}

public sealed record ManufacturingProcess
{
    public Guid Id { get; init; }

    public string Key { get; init; } = "";

    public string? Name { get; init; }

    public ProcessKind Kind { get; init; }

    public Guid? MachinePartId { get; init; }

    public Guid? SourceArtifactId { get; init; }

    public string? Postprocessor { get; init; }

    public List<Guid>? OutputArtifactIds { get; init; }

    public JsonObject? Parameters { get; init; }
}

public sealed record ValidationSection
{
    public List<ValidationRule>? Rules { get; init; }

    public List<string>? RequiredChecks { get; init; }

    public Dictionary<string, double>? Tolerances { get; init; }
}

public sealed record ValidationRule
{
    public string Id { get; init; } = "";

    public Diagnostics.DiagnosticSeverity Severity { get; init; }

    public bool? Enabled { get; init; }

    public JsonObject? Params { get; init; }
}
