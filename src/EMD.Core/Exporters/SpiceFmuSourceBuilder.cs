using System.Globalization;
using System.Text;
using Emd.Core.Simulation;

namespace Emd.Core.Exporters;

public sealed record SpiceFmuSource(
    string ClassName,
    string Source,
    IReadOnlyList<string> Variables);

public sealed class SpiceFmuSourceBuilder
{
    public const string ClassName = "SpiceTraceSlave";

    public static SpiceFmuSourceBuilder Default { get; } = new();

    public SpiceFmuSource Build(SpiceTrace trace)
    {
        var names = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var signal in trace.Signals)
        {
            var name = SpiceTraceLoader.Sanitize(signal);
            var candidate = name;
            var suffix = 2;
            while (!used.Add(candidate))
                candidate = $"{name}_{suffix++}";
            names.Add(candidate);
        }

        var source = new StringBuilder();
        source.AppendLine("from bisect import bisect_left");
        source.AppendLine("from pythonfmu import Fmi2Causality, Fmi2Slave, Real");
        source.AppendLine();
        source.AppendLine($"TIMES = [{Numbers(trace.Times)}]");
        source.AppendLine("VALUES = {");
        for (var index = 0; index < names.Count; index++)
            source.AppendLine($"    \"{names[index]}\": [{Numbers(trace.Values[index])}],");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine($"class {ClassName}(Fmi2Slave):");
        source.AppendLine("    author = \"ElectroMechanicalDesigner\"");
        source.AppendLine("    description = \"SPICE trace source FMU\"");
        source.AppendLine();
        source.AppendLine("    def __init__(self, **kwargs):");
        source.AppendLine("        super().__init__(**kwargs)");
        source.AppendLine("        for name in VALUES:");
        source.AppendLine("            setattr(self, name, 0.0)");
        source.AppendLine("            self.register_variable(Real(name, causality=Fmi2Causality.output))");
        source.AppendLine();
        source.AppendLine("    def do_step(self, current_time, step_size):");
        source.AppendLine("        time = current_time + step_size");
        source.AppendLine("        for name, values in VALUES.items():");
        source.AppendLine("            index = bisect_left(TIMES, time)");
        source.AppendLine("            if index <= 0:");
        source.AppendLine("                value = values[0]");
        source.AppendLine("            elif index >= len(TIMES):");
        source.AppendLine("                value = values[-1]");
        source.AppendLine("            else:");
        source.AppendLine("                t0, t1 = TIMES[index - 1], TIMES[index]");
        source.AppendLine("                v0, v1 = values[index - 1], values[index]");
        source.AppendLine("                value = v0 if t1 == t0 else v0 + (v1 - v0) * (time - t0) / (t1 - t0)");
        source.AppendLine("            setattr(self, name, value)");
        source.AppendLine("        return True");

        return new SpiceFmuSource(ClassName, source.ToString(), names);
    }

    private static string Numbers(IEnumerable<double> values) =>
        string.Join(", ", values.Select(value =>
            value.ToString("R", CultureInfo.InvariantCulture)));
}
