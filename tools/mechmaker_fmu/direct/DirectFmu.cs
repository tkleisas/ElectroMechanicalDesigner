using System.Reflection;
using System.Runtime.InteropServices;
using MechMaker.Core.Model;
using MechMaker.Engine;

namespace MechMaker.Fmu.Direct;

public static unsafe class DirectFmu
{
    private const int Ok = 0;
    private const int Error = 3;
    private const int CoSimulation = 1;
    private const int Channels = 4;

    private static readonly IntPtr TypesPlatform = Utf8("default");
    private static readonly IntPtr Version = Utf8("2.0");
    private static string _nativeDirectory = "";
    private static bool _nativeResolverSet;

    private static IntPtr Utf8(string value) => Marshal.StringToCoTaskMemUTF8(value);

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetTypesPlatform")]
    public static void* GetTypesPlatform() => (void*)TypesPlatform;

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetVersion")]
    public static void* GetVersion() => (void*)Version;

    [UnmanagedCallersOnly(EntryPoint = "fmi2SetDebugLogging")]
    public static int SetDebugLogging(void* component, int loggingOn, nuint categories, void* categoryNames) => Ok;

    [UnmanagedCallersOnly(EntryPoint = "fmi2Instantiate")]
    public static void* Instantiate(
        void* instanceName,
        int fmuType,
        void* fmuGuid,
        void* resourceLocation,
        void* callbacks,
        int visible,
        int loggingOn)
    {
        if (fmuType != CoSimulation)
            return null;

        try
        {
            var slave = new Slave
            {
                ResourceLocation = Marshal.PtrToStringUTF8((nint)resourceLocation) ?? ""
            };
            var handle = GCHandle.Alloc(slave, GCHandleType.Normal);
            return (void*)GCHandle.ToIntPtr(handle);
        }
        catch
        {
            return null;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2FreeInstance")]
    public static void FreeInstance(void* component)
    {
        var slave = Get(component);
        if (slave is null)
            return;
        slave.Dispose();
        GCHandle.FromIntPtr((nint)component).Free();
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2SetupExperiment")]
    public static int SetupExperiment(
        void* component,
        int toleranceDefined,
        double tolerance,
        double startTime,
        int stopTimeDefined,
        double stopTime) => Get(component) is null ? Error : Ok;

    [UnmanagedCallersOnly(EntryPoint = "fmi2EnterInitializationMode")]
    public static int EnterInitializationMode(void* component) => Get(component) is null ? Error : Ok;

    [UnmanagedCallersOnly(EntryPoint = "fmi2ExitInitializationMode")]
    public static int ExitInitializationMode(void* component)
    {
        var slave = Get(component);
        if (slave is null)
            return Error;
        try
        {
            return slave.Initialize();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"MechMakerDirect init failed: {exception}");
            return Error;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2Terminate")]
    public static int Terminate(void* component)
    {
        var slave = Get(component);
        if (slave is null)
            return Error;
        slave.Terminate();
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2Reset")]
    public static int Reset(void* component)
    {
        var slave = Get(component);
        if (slave is null)
            return Error;
        slave.Terminate();
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetReal")]
    public static int GetReal(void* component, void* references, nuint count, void* values)
    {
        var slave = Get(component);
        if (slave is null)
            return Error;

        var vrs = (uint*)references;
        var output = (double*)values;
        for (nuint i = 0; i < count; i++)
            output[i] = slave.GetReal(vrs[i]);
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2SetReal")]
    public static int SetReal(void* component, void* references, nuint count, void* values)
    {
        var slave = Get(component);
        if (slave is null)
            return Error;

        var vrs = (uint*)references;
        var input = (double*)values;
        for (nuint i = 0; i < count; i++)
            slave.SetReal(vrs[i], input[i]);
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetInteger")]
    public static int GetInteger(void* component, void* references, nuint count, void* values)
    {
        var slave = Get(component);
        if (slave is null)
            return Error;

        var vrs = (uint*)references;
        var output = (int*)values;
        for (nuint i = 0; i < count; i++)
            output[i] = slave.GetInteger(vrs[i]);
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2SetInteger")]
    public static int SetInteger(void* component, void* references, nuint count, void* values)
    {
        var slave = Get(component);
        if (slave is null)
            return Error;

        var vrs = (uint*)references;
        var input = (int*)values;
        for (nuint i = 0; i < count; i++)
            slave.SetInteger(vrs[i], input[i]);
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetBoolean")]
    public static int GetBoolean(void* component, void* references, nuint count, void* values)
    {
        var slave = Get(component);
        if (slave is null)
            return Error;

        var vrs = (uint*)references;
        var output = (int*)values;
        for (nuint i = 0; i < count; i++)
            output[i] = slave.GetBoolean(vrs[i]) ? 1 : 0;
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2SetBoolean")]
    public static int SetBoolean(void* component, void* references, nuint count, void* values)
    {
        var slave = Get(component);
        if (slave is null)
            return Error;

        var vrs = (uint*)references;
        var input = (int*)values;
        for (nuint i = 0; i < count; i++)
            slave.SetBoolean(vrs[i], input[i] != 0);
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetString")]
    public static int GetString(void* component, void* references, nuint count, void* values)
    {
        var slave = Get(component);
        if (slave is null)
            return Error;

        var vrs = (uint*)references;
        var output = (void**)values;
        for (nuint i = 0; i < count; i++)
            output[i] = (void*)slave.GetString(vrs[i]);
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2SetString")]
    public static int SetString(void* component, void* references, nuint count, void* values)
    {
        var slave = Get(component);
        if (slave is null)
            return Error;

        var vrs = (uint*)references;
        var input = (void**)values;
        for (nuint i = 0; i < count; i++)
            slave.SetString(vrs[i], Marshal.PtrToStringUTF8((nint)input[i]) ?? "");
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2DoStep")]
    public static int DoStep(
        void* component,
        double currentCommunicationPoint,
        double communicationStepSize,
        int noSetFmuStatePriorToCurrentPoint)
    {
        var slave = Get(component);
        if (slave is null)
            return Error;
        try
        {
            return slave.DoStep(communicationStepSize);
        }
        catch
        {
            return Error;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2CancelStep")]
    public static int CancelStep(void* component) => Ok;

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetStatus")]
    public static int GetStatus(void* component, int statusKind, void* value)
    {
        if (Get(component) is null)
            return Error;
        *(int*)value = Ok;
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetRealStatus")]
    public static int GetRealStatus(void* component, int statusKind, void* value)
    {
        if (Get(component) is null)
            return Error;
        *(double*)value = 0;
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetIntegerStatus")]
    public static int GetIntegerStatus(void* component, int statusKind, void* value)
    {
        if (Get(component) is null)
            return Error;
        *(int*)value = 0;
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetBooleanStatus")]
    public static int GetBooleanStatus(void* component, int statusKind, void* value)
    {
        if (Get(component) is null)
            return Error;
        *(int*)value = 0;
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetStringStatus")]
    public static int GetStringStatus(void* component, int statusKind, void* value)
    {
        if (Get(component) is null)
            return Error;
        *(void**)value = (void*)Utf8("");
        return Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "fmi2SetRealInputDerivatives")]
    public static int SetRealInputDerivatives(void* component, void* references, nuint count, int order, void* values) => Ok;

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetRealOutputDerivatives")]
    public static int GetRealOutputDerivatives(void* component, void* references, nuint count, int order, void* values) => Error;

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetDirectionalDerivative")]
    public static int GetDirectionalDerivative(void* component, void* unknowns, nuint nUnknowns, void* knowns, nuint nKnowns, void* seed, void* sensitivity) => Error;

    [UnmanagedCallersOnly(EntryPoint = "fmi2GetFMUstate")]
    public static int GetFmuState(void* component, void* state) => Error;

    [UnmanagedCallersOnly(EntryPoint = "fmi2SetFMUstate")]
    public static int SetFmuState(void* component, void* state) => Error;

    [UnmanagedCallersOnly(EntryPoint = "fmi2FreeFMUstate")]
    public static int FreeFmuState(void* component, void* state) => Error;

    [UnmanagedCallersOnly(EntryPoint = "fmi2SerializedFMUstateSize")]
    public static int SerializedFmuStateSize(void* component, void* state, void* size) => Error;

    [UnmanagedCallersOnly(EntryPoint = "fmi2SerializeFMUstate")]
    public static int SerializeFmuState(void* component, void* state, void* serializedState, nuint size) => Error;

    [UnmanagedCallersOnly(EntryPoint = "fmi2DeSerializeFMUstate")]
    public static int DeserializeFmuState(void* component, void* serializedState, nuint size, void* state) => Error;

    [UnmanagedCallersOnly(EntryPoint = "fmi2EnterEventMode")]
    public static int EnterEventMode(void* component) => Error;

    [UnmanagedCallersOnly(EntryPoint = "fmi2NewDiscreteStates")]
    public static int NewDiscreteStates(void* component, void* eventInfo) => Error;

    [UnmanagedCallersOnly(EntryPoint = "fmi2EnterContinuousTimeMode")]
    public static int EnterContinuousTimeMode(void* component) => Error;

    [UnmanagedCallersOnly(EntryPoint = "fmi2CompletedIntegratorStep")]
    public static int CompletedIntegratorStep(void* component, int noSetFmuStatePriorToCurrentPoint, void* enterEventMode, void* terminateSimulation) => Error;

    private static void EnsureNativeResolver(string directory)
    {
        _nativeDirectory = directory;
        if (_nativeResolverSet)
            return;

        try
        {
            NativeLibrary.SetDllImportResolver(
                typeof(Evergine.Bindings.MuJoCo.MuJoCo).Assembly,
                ResolveNative);
            _nativeResolverSet = true;
        }
        catch (InvalidOperationException)
        {
            _nativeResolverSet = true;
        }
    }

    private static IntPtr ResolveNative(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        var fileName = libraryName;
        if (OperatingSystem.IsWindows())
        {
            if (!fileName.EndsWith(".dll", StringComparison.Ordinal))
                fileName = $"{fileName}.dll";
        }
        else if (!fileName.EndsWith(".so", StringComparison.Ordinal) &&
                 !fileName.EndsWith(".dylib", StringComparison.Ordinal))
        {
            fileName = $"lib{fileName}.so";
        }

        foreach (var directory in new[]
                 {
                     _nativeDirectory,
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory()
                 })
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return NativeLibrary.Load(candidate);
        }

        return IntPtr.Zero;
    }

    private static Slave? Get(void* handle)
    {
        if (handle == null)
            return null;
        return GCHandle.FromIntPtr((nint)handle).Target as Slave;
    }

    private sealed class Slave : IDisposable
    {
        public string ResourceLocation = "";
        public string MachinePath = "";
        public string ServerPath = "";
        public string CatalogPath = "";
        public int Microsteps = 16;
        public double CarriageMPerRev = 0.04;
        public double XCarriage;
        public double ServoTarget;
        public double ServoPosition;
        public string EndstopJoint = "";
        public double EndstopTriggerM;
        public bool EndstopTriggered;
        public double FanDuty;
        public double FanRpm;
        public double HeaterDuty;
        public double HeaterTemperatureC;

        private readonly double[] _velocity = new double[Channels];
        private readonly bool[] _enable = new bool[Channels];
        private readonly double[] _angle = new double[Channels];
        private readonly List<StepperChannel> _channels = [];
        private MachineSimulation? _simulation;
        private ServoChannel? _servo;
        private EndstopChannel? _endstop;
        private FanChannel? _fan;
        private ThermalChannel? _heater;
        private IntPtr _machinePathPtr;
        private IntPtr _serverPathPtr;
        private IntPtr _catalogPathPtr;
        private IntPtr _endstopJointPtr;

        public int Initialize()
        {
            Terminate();
            var baseDirectory = ResolveResourcePath(ResourceLocation);
            var payload = Path.Combine(baseDirectory, "payload");
            if (!Directory.Exists(payload))
                payload = baseDirectory;

            var platform = OperatingSystem.IsWindows() ? "win64" : "linux64";
            var binaries = Path.GetFullPath(Path.Combine(
                baseDirectory, "..", "binaries", platform));
            EnsureNativeResolver(binaries);

            var machinePath = string.IsNullOrWhiteSpace(MachinePath)
                ? Path.Combine(payload, "machine.json")
                : MachinePath;
            var catalogPath = string.IsNullOrWhiteSpace(CatalogPath)
                ? Path.Combine(payload, "catalog")
                : CatalogPath;
            var machine = DirectJson.LoadMachine(machinePath);
            var catalog = DirectJson.LoadCatalog(catalogPath);
            _simulation = MachineSimulation.FromMachine(machine, catalog, Microsteps);
            _servo = _simulation.ServosByInstance.Values.FirstOrDefault();
            _fan = _simulation.FansByInstance.Values.FirstOrDefault();
            _heater = _simulation.HeatersByInstance.Values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(EndstopJoint))
                _endstop = _simulation.Mcu.AddEndstop(EndstopJoint, EndstopTriggerM);

            _channels.Clear();
            foreach (var instanceId in machine.Wiring
                         .Where(wire => wire.Signal == "step")
                         .Select(wire => wire.Component)
                         .Distinct())
            {
                if (_channels.Count >= Channels)
                    break;
                try
                {
                    var channel = _simulation.Stepper(instanceId);
                    channel.Enable(_enable[_channels.Count]);
                    channel.SetVelocityRevPerSec(_velocity[_channels.Count]);
                    _channels.Add(channel);
                }
                catch (KeyNotFoundException)
                {
                }
            }

            return Ok;
        }

        public int DoStep(double stepSize)
        {
            if (_simulation is null)
                return Error;

            for (var index = 0; index < _channels.Count; index++)
            {
                _channels[index].Enable(_enable[index]);
                _channels[index].SetVelocityRevPerSec(_velocity[index]);
            }

            if (_servo is not null)
            {
                if (_servo.IsSlide)
                    _servo.SetTargetPositionM(ServoTarget);
                else
                    _servo.SetTargetAngleDeg(ServoTarget);
            }
            _fan?.SetDuty(FanDuty);
            _heater?.SetDuty(HeaterDuty);

            _simulation.RunFor(stepSize);
            for (var index = 0; index < _channels.Count; index++)
                _angle[index] = _channels[index].RotorAngleRev;
            XCarriage = _angle[0] * CarriageMPerRev;
            ServoPosition = _servo?.AngleDeg ?? 0;
            FanRpm = (_fan?.RevPerSec ?? 0) * 60;
            HeaterTemperatureC = _heater?.TemperatureC ?? 0;
            EndstopTriggered = _endstop?.Pressed ?? false;
            return Ok;
        }

        public void Terminate()
        {
            _simulation?.Dispose();
            _simulation = null;
            _channels.Clear();
            _servo = null;
            _endstop = null;
            _fan = null;
            _heater = null;
        }

        public double GetReal(uint reference)
        {
            if (reference == 4)
                return CarriageMPerRev;
            if (reference == 17)
                return XCarriage;
            if (reference == 18)
                return ServoTarget;
            if (reference == 19)
                return ServoPosition;
            if (reference == 21)
                return EndstopTriggerM;
            if (reference == 23)
                return FanDuty;
            if (reference == 24)
                return FanRpm;
            if (reference == 25)
                return HeaterDuty;
            if (reference == 26)
                return HeaterTemperatureC;
            if (reference >= 5 && reference <= 16)
            {
                var offset = reference - 5;
                var channel = offset / 3;
                var field = offset % 3;
                return field switch
                {
                    0 => _velocity[channel],
                    2 => _angle[channel],
                    _ => 0
                };
            }
            return 0;
        }

        public void SetReal(uint reference, double value)
        {
            if (reference == 4)
                CarriageMPerRev = value;
            else if (reference == 18)
                ServoTarget = value;
            else if (reference == 21)
                EndstopTriggerM = value;
            else if (reference == 23)
                FanDuty = value;
            else if (reference == 25)
                HeaterDuty = value;
            else if (reference >= 5 && reference <= 16)
            {
                var offset = reference - 5;
                if (offset % 3 == 0)
                    _velocity[offset / 3] = value;
            }
        }

        public int GetInteger(uint reference) => reference == 3 ? Microsteps : 0;

        public void SetInteger(uint reference, int value)
        {
            if (reference == 3)
                Microsteps = value;
        }

        public bool GetBoolean(uint reference)
        {
            if (reference == 22)
                return EndstopTriggered;
            if (reference >= 6 && reference <= 15)
            {
                var offset = reference - 6;
                return offset % 3 == 0 && _enable[offset / 3];
            }
            return false;
        }

        public void SetBoolean(uint reference, bool value)
        {
            if (reference >= 6 && reference <= 15)
            {
                var offset = reference - 6;
                if (offset % 3 == 0)
                    _enable[offset / 3] = value;
            }
        }

        public IntPtr GetString(uint reference) => reference switch
        {
            0 => StringPtr(ref _machinePathPtr, MachinePath),
            1 => StringPtr(ref _serverPathPtr, ServerPath),
            2 => StringPtr(ref _catalogPathPtr, CatalogPath),
            20 => StringPtr(ref _endstopJointPtr, EndstopJoint),
            _ => StringPtr(ref _machinePathPtr, MachinePath)
        };

        public void SetString(uint reference, string value)
        {
            switch (reference)
            {
                case 0:
                    MachinePath = value;
                    break;
                case 1:
                    ServerPath = value;
                    break;
                case 2:
                    CatalogPath = value;
                    break;
                case 20:
                    EndstopJoint = value;
                    break;
            }
        }

        private static IntPtr StringPtr(ref IntPtr pointer, string value)
        {
            if (pointer == IntPtr.Zero)
                pointer = Utf8(value);
            return pointer;
        }

        private static string ResolveResourcePath(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return Directory.GetCurrentDirectory();
            location = Normalize(location);
            if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.IsFile)
                return Normalize(uri.LocalPath);
            return Path.GetFullPath(location);
        }

        private static string Normalize(string path)
        {
            if (OperatingSystem.IsWindows())
                return path;
            path = path.Replace('\\', '/');
            if (path.StartsWith("//", StringComparison.Ordinal))
                path = path[1..];
            return path;
        }

        public void Dispose()
        {
            Terminate();
            foreach (var pointer in new[]
                     {
                         _machinePathPtr, _serverPathPtr, _catalogPathPtr, _endstopJointPtr
                     })
            {
                if (pointer != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pointer);
            }
        }
    }
}
