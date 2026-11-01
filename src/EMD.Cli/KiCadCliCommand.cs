using Emd.Adapters;
using Emd.Core.Diagnostics;
using Emd.Core.Exporters;

namespace Emd.Cli;

public static class KiCadCliCommand
{
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        KiCadCliAdapter adapter)
    {
        if (args.Length == 0)
            return Usage(stderr, "kicad expects erc, drc, netlist, bom, gerbers, pos, step, or fabrication.");

        var operation = args[0].ToLowerInvariant();
        var schematic = GetOption(args, "--schematic");
        var pcb = GetOption(args, "--pcb");
        var output = GetOption(args, "--out");

        if (string.IsNullOrWhiteSpace(output))
            return Usage(stderr, $"kicad {operation} requires --out <path>.");

        if (operation == "fabrication")
            return Fabrication(args, stdout, stderr, adapter, schematic, pcb, output);

        AdapterResult result;
        switch (operation)
        {
            case "erc":
                if (string.IsNullOrWhiteSpace(schematic))
                    return Usage(stderr, "kicad erc requires --schematic <file.kicad_sch>.");
                result = adapter.RunErcAsync(schematic, output).GetAwaiter().GetResult();
                break;
            case "drc":
                if (string.IsNullOrWhiteSpace(pcb))
                    return Usage(stderr, "kicad drc requires --pcb <file.kicad_pcb>.");
                result = adapter.RunDrcAsync(pcb, output).GetAwaiter().GetResult();
                break;
            case "netlist":
                if (string.IsNullOrWhiteSpace(schematic))
                    return Usage(stderr, "kicad netlist requires --schematic <file.kicad_sch>.");
                result = adapter.ExportNetlistAsync(schematic, output).GetAwaiter().GetResult();
                break;
            case "bom":
                if (string.IsNullOrWhiteSpace(schematic))
                    return Usage(stderr, "kicad bom requires --schematic <file.kicad_sch>.");
                result = adapter.ExportBomAsync(schematic, output).GetAwaiter().GetResult();
                break;
            case "gerbers":
                if (string.IsNullOrWhiteSpace(pcb))
                    return Usage(stderr, "kicad gerbers requires --pcb <file.kicad_pcb>.");
                result = adapter.ExportGerbersAsync(pcb, output).GetAwaiter().GetResult();
                break;
            case "step":
                if (string.IsNullOrWhiteSpace(pcb))
                    return Usage(stderr, "kicad step requires --pcb <file.kicad_pcb>.");
                result = adapter.ExportStepAsync(pcb, output).GetAwaiter().GetResult();
                break;
            case "pos":
                if (string.IsNullOrWhiteSpace(pcb))
                    return Usage(stderr, "kicad pos requires --pcb <file.kicad_pcb>.");
                result = adapter.ExportPosAsync(pcb, output).GetAwaiter().GetResult();
                break;
            default:
                return Usage(stderr, $"Unknown kicad operation '{operation}'.");
        }

        foreach (var diagnostic in result.Diagnostics)
            stderr.WriteLine(diagnostic);

        foreach (var artifact in result.Artifacts)
            stdout.WriteLine($"artifact: {artifact.Format} {artifact.Path}");

        stdout.WriteLine($"kicad {operation}: {result.Artifacts.Count} artifact(s)");
        return result.Success ? 0 : 1;
    }

    private static int Fabrication(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        KiCadCliAdapter adapter,
        string? schematic,
        string? pcb,
        string output)
    {
        if (string.IsNullOrWhiteSpace(schematic) || string.IsNullOrWhiteSpace(pcb))
            return Usage(stderr, "kicad fabrication requires --schematic and --pcb.");

        var work = Directory.CreateTempSubdirectory("emd-kicad-fab-");
        try
        {
            var gerberDirectory = Path.Combine(work.FullName, "gerbers");
            Directory.CreateDirectory(gerberDirectory);
            var bom = Path.Combine(work.FullName, "bom.csv");
            var positions = Path.Combine(work.FullName, "positions.csv");
            var step = Path.Combine(work.FullName, "board.step");

            var gerbers = adapter.ExportGerbersAsync(pcb, gerberDirectory).GetAwaiter().GetResult();
            var bomResult = adapter.ExportBomAsync(schematic, bom).GetAwaiter().GetResult();
            var posResult = adapter.ExportPosAsync(pcb, positions).GetAwaiter().GetResult();
            var stepResult = adapter.ExportStepAsync(pcb, step).GetAwaiter().GetResult();
            foreach (var result in new[] { gerbers, bomResult, posResult, stepResult })
            {
                foreach (var diagnostic in result.Diagnostics)
                    stderr.WriteLine(diagnostic);
                if (!result.Success)
                    return 1;
            }

            var package = KiCadFabricationPackageExporter.Default.Export(
                new KiCadFabricationInputs(
                    GetOption(args, "--name") ?? Path.GetFileNameWithoutExtension(pcb),
                    gerbers.Artifacts.Select(artifact => artifact.Path).ToList(),
                    bom,
                    positions,
                    step));
            foreach (var diagnostic in package.Diagnostics)
                stderr.WriteLine(diagnostic);
            if (package.Archive is null)
                return 1;

            var full = Path.GetFullPath(output);
            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(full, package.Archive);
            stdout.WriteLine(
                $"kicad fabrication: {gerbers.Artifacts.Count} gerber/drill file(s) -> {full}");
            return 0;
        }
        finally
        {
            try
            {
                work.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        var prefix = name + "=";
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.StartsWith(prefix, StringComparison.Ordinal))
                return argument[prefix.Length..];
            if (argument == name && index + 1 < args.Length)
                return args[index + 1];
        }

        return null;
    }

    private static int Usage(TextWriter stderr, string message)
    {
        stderr.WriteLine($"emd: {message}");
        return 2;
    }
}
