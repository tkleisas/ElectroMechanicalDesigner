using Emd.Adapters;
using Emd.Core.Diagnostics;

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
            return Usage(stderr, "kicad expects erc, drc, netlist, bom, gerbers, or step.");

        var operation = args[0].ToLowerInvariant();
        var schematic = GetOption(args, "--schematic");
        var pcb = GetOption(args, "--pcb");
        var output = GetOption(args, "--out");

        if (string.IsNullOrWhiteSpace(output))
            return Usage(stderr, $"kicad {operation} requires --out <path>.");

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
