using System.Text;
using Emd.Core.Simulation;

namespace Emd.Core.Exporters;

public sealed record SpiceClosedLoopSource(
    string ClassName,
    string Source,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Variables);

public sealed class SpiceClosedLoopSourceBuilder
{
    public const string ClassName = "ClosedLoopSpiceSlave";

    public static SpiceClosedLoopSourceBuilder Default { get; } = new();

    public SpiceClosedLoopSource Build(
        string template,
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs)
    {
        var cleanInputs = inputs.Select(SpiceTraceLoader.Sanitize).Distinct().ToList();
        var signals = outputs.Select(SpiceTraceLoader.Sanitize).Distinct().ToList();
        if (cleanInputs.Count != inputs.Count || signals.Count != outputs.Count)
            throw new InvalidOperationException("SPICE input/output names must be unique.");

        var source = new StringBuilder();
        source.AppendLine("import re");
        source.AppendLine("import subprocess");
        source.AppendLine("import tempfile");
        source.AppendLine("from pathlib import Path");
        source.AppendLine("from pythonfmu import Fmi2Causality, Fmi2Slave, Fmi2Variability, Real, String");
        source.AppendLine();
        source.AppendLine($"NETLIST = {PythonString(template)}");
        source.AppendLine($"INPUTS = {PythonList(cleanInputs)}");
        source.AppendLine($"SIGNALS = {PythonList(outputs)}");
        source.AppendLine($"VARIABLES = {PythonList(signals)}");
        source.AppendLine();
        source.AppendLine($"class {ClassName}(Fmi2Slave):");
        source.AppendLine("    author = \"ElectroMechanicalDesigner\"");
        source.AppendLine("    description = \"Closed-loop SPICE co-simulation FMU\"");
        source.AppendLine();
        source.AppendLine("    def __init__(self, **kwargs):");
        source.AppendLine("        super().__init__(**kwargs)");
        source.AppendLine("        self.ngspice_path = \"ngspice\"");
        source.AppendLine("        self.register_variable(String(\"ngspice_path\", start=\"ngspice\", causality=Fmi2Causality.parameter, variability=Fmi2Variability.tunable))");
        source.AppendLine("        for name in INPUTS:");
        source.AppendLine("            setattr(self, name, 0.0)");
        source.AppendLine("            self.register_variable(Real(name, causality=Fmi2Causality.input))");
        source.AppendLine("        for name in VARIABLES:");
        source.AppendLine("            setattr(self, name, 0.0)");
        source.AppendLine("            self.register_variable(Real(name, causality=Fmi2Causality.output))");
        source.AppendLine();
        source.AppendLine("    def do_step(self, current_time, step_size):");
        source.AppendLine("        text = NETLIST");
        source.AppendLine("        for name in INPUTS:");
        source.AppendLine("            text = text.replace(\"{{\" + name + \"}}\", repr(float(getattr(self, name))))");
        source.AppendLine("        with tempfile.TemporaryDirectory() as directory:");
        source.AppendLine("            directory = Path(directory)");
        source.AppendLine("            netlist = directory / \"circuit.cir\"");
        source.AppendLine("            log = directory / \"ngspice.log\"");
        source.AppendLine("            netlist.write_text(text, encoding=\"utf-8\")");
        source.AppendLine("            subprocess.run([self.ngspice_path, \"-b\", \"-o\", str(log), str(netlist)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)");
        source.AppendLine("            content = log.read_text(encoding=\"utf-8\", errors=\"replace\")");
        source.AppendLine("        for signal, variable in zip(SIGNALS, VARIABLES):");
        source.AppendLine("            setattr(self, variable, read_signal(content, signal))");
        source.AppendLine("        return True");
        source.AppendLine();
        source.AppendLine("def read_signal(content, signal):");
        source.AppendLine("    match = re.search(re.escape(signal) + r\"\\s*=\\s*([-+0-9.eE]+)\", content)");
        source.AppendLine("    if match: return float(match.group(1))");
        source.AppendLine("    lines = content.splitlines()");
        source.AppendLine("    for index, line in enumerate(lines):");
        source.AppendLine("        if signal in line and index + 1 < len(lines):");
        source.AppendLine("            numbers = re.findall(r\"[-+]?(?:\\d+\\.?\\d*|\\.\\d+)(?:[eE][-+]?\\d+)?\", lines[index + 1])");
        source.AppendLine("            if numbers: return float(numbers[-1])");
        source.AppendLine("    node = signal[2:-1] if signal.startswith(\"v(\") and signal.endswith(\")\") else signal");
        source.AppendLine("    for line in lines:");
        source.AppendLine("        if line.strip().startswith(node + \" \"):");
        source.AppendLine("            numbers = re.findall(r\"[-+]?(?:\\d+\\.?\\d*|\\.\\d+)(?:[eE][-+]?\\d+)?\", line)");
        source.AppendLine("            if numbers: return float(numbers[-1])");
        source.AppendLine("    return 0.0");

        return new SpiceClosedLoopSource(
            ClassName,
            source.ToString(),
            cleanInputs,
            signals);
    }

    private static string PythonString(string value) =>
        "\"\"\"" + value.Replace("\"\"\"", "\\\"\\\"\\\"", StringComparison.Ordinal) + "\"\"\"";

    private static string PythonList(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.Select(value => $"\"{value}\"")) + "]";
}
