using Emd.Adapters;

namespace Emd.Cli;

public static class SpiceCommand
{
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        NgSpiceAdapter adapter)
    {
        if (args.Length > 0 &&
            string.Equals(args[0], "fmu", StringComparison.OrdinalIgnoreCase))
        {
            return SpiceFmuCommand.Run(
                args.Skip(1).ToArray(), stdout, stderr, ProcessToolHost.Default);
        }

        var netlist = GetOption(args, "--netlist");
        var output = GetOption(args, "--out");

        if (string.IsNullOrWhiteSpace(netlist))
            return Usage(stderr, "spice requires --netlist <file.cir>.");
        if (string.IsNullOrWhiteSpace(output))
            return Usage(stderr, "spice requires --out <log>.");

        var result = adapter.RunAsync(netlist, output).GetAwaiter().GetResult();

        foreach (var diagnostic in result.Diagnostics)
            stderr.WriteLine(diagnostic);

        foreach (var artifact in result.Artifacts)
            stdout.WriteLine($"artifact: {artifact.Format} {artifact.Path}");

        stdout.WriteLine($"spice: {result.Artifacts.Count} artifact(s)");
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
