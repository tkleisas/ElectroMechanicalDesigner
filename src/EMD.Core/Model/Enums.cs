using System.Text.Json.Serialization;

namespace Emd.Core.Model;

public enum PartKind
{
    Component,
    Assembly,
    Board,
    Cable,
    Virtual,
    Reference
}

public enum PartCategory
{
    Structure,
    Motor,
    Transmission,
    LinearMotion,
    Sensor,
    Electronics,
    Tool,
    Fastener,
    Other
}

public enum PortDomain
{
    Mechanical,
    Electrical,
    Control,
    Thermal,
    Fluid,
    Simulation,
    Software
}

public enum PortDirection
{
    In,
    Out,
    Inout,
    Passive
}

public enum SignalType
{
    Analog,
    Digital,
    Power,
    Ground,
    Pwm,
    Differential,
    Mechanical,
    Thermal,
    Fluid,
    Software,
    Other
}

public enum ConnectionKind
{
    Mate,
    Wire,
    Net,
    Control,
    Thermal,
    Fluid,
    Mount,
    SimulationBinding,
    Harness,
    FirmwarePin,
    Other
}

public enum GeometryKind
{
    Box,
    Cylinder,
    Sphere,
    Capsule,
    Ellipsoid,
    Mesh
}

public enum MechanicalJointType
{
    Fixed,
    Hinge,
    Slide,
    Ball,
    Free
}

public enum ArtifactRole
{
    Source,
    Generated,
    Visualization,
    Simulation,
    Manufacturing,
    Report,
    Other
}

public enum NetClass
{
    Power,
    Ground,
    Signal,
    Differential,
    Bus,
    Rf,
    Other
}

public enum BuildSystem
{
    Platformio,
    Zephyr,
    Cmake,
    Arduino,
    Make,
    Other
}

public enum ModelKind
{
    Modelica,
    Spice,
    Fmu,
    Mechmaker,
    Lua,
    Python,
    Script,
    Other
}

public enum SimulationFidelity
{
    Kinematic,
    RigidBody,
    CoSimulation,
    HardwareInTheLoop
}

public enum SimulationStandard
{
    Fmi3,
    Ssp2,
    Native
}

public enum SimulationMaster
{
    Omsimulator,
    Fmpy,
    Openmcx,
    Mechmaker,
    Custom
}

public enum ProcessKind
{
    Cnc,
    Laser,
    [JsonStringEnumMemberName("3d_print")]
    ThreeDPrint,
    PcbFab,
    PcbAssembly,
    Assembly,
    Other
}
