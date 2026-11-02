using System.Net.Sockets;
using Emd.Adapters;
using Emd.Adapters.MechMaker;

namespace Emd.Cli;

public static class RenodeBridgeCommand
{
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (args.Length == 0)
            return Usage(stderr, "renode bridge expects a subcommand.");
        return args[0].ToLowerInvariant() switch
        {
            "script" or "bridge-script" => Script(args, stdout, stderr),
            "run" or "bridge" => RunBridge(args, stdout, stderr),
            _ => Usage(stderr, $"Unknown renode bridge subcommand '{args[0]}'.")
        };
    }

    private static int Script(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var firmware = GetOption(args, "--firmware");
        var platform = GetOption(args, "--platform");
        var output = GetOption(args, "--out");
        if (string.IsNullOrWhiteSpace(firmware) || string.IsNullOrWhiteSpace(platform) ||
            string.IsNullOrWhiteSpace(output))
            return Usage(stderr, "renode bridge script requires --firmware, --platform, and --out.");

        var port = int.TryParse(GetOption(args, "--port") ?? "3456", out var value) ? value : 3456;
        var script = RenodeBridgeScript.Build(
            firmware, platform, GetOption(args, "--uart") ?? "uart0", port);
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(output, script);
        stdout.WriteLine($"renode bridge script: {output} (TCP {port})");
        return 0;
    }

    private static int RunBridge(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var machine = GetOption(args, "--machine");
        var mechmaker = GetOption(args, "--mechmaker");
        var output = GetOption(args, "--out");
        var uartFile = GetOption(args, "--uart-file");
        var endpoint = GetOption(args, "--endpoint");
        if (string.IsNullOrWhiteSpace(machine) || string.IsNullOrWhiteSpace(mechmaker) ||
            string.IsNullOrWhiteSpace(output))
            return Usage(stderr, "renode bridge requires --machine, --mechmaker, and --out.");
        if (string.IsNullOrWhiteSpace(uartFile) == string.IsNullOrWhiteSpace(endpoint))
            return Usage(stderr, "renode bridge requires exactly one of --uart-file or --endpoint.");

        TcpClient? tcp = null;
        TextReader reader;
        if (!string.IsNullOrWhiteSpace(uartFile))
        {
            reader = new StreamReader(uartFile);
        }
        else
        {
            var parts = endpoint!.Split(':', 2);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
                return Usage(stderr, "--endpoint must be host:port.");
            tcp = new TcpClient();
            tcp.Connect(parts[0], port);
            reader = new StreamReader(tcp.GetStream());
        }

        MechMakerMcpToolClient? client = null;
        try
        {
            machine = Path.GetFullPath(machine);
            mechmaker = Path.GetFullPath(mechmaker);
            var executable = Path.GetExtension(mechmaker).Equals(".dll", StringComparison.OrdinalIgnoreCase)
                ? "dotnet"
                : mechmaker;
            var arguments = executable == "dotnet" ? new[] { mechmaker } : [];
            client = MechMakerMcpToolClient.ConnectAsync(
                    executable, arguments,
                    Path.GetDirectoryName(Path.GetFullPath(mechmaker)))
                .GetAwaiter().GetResult();
            var bridge = new RenodeUartBridge();
            var result = bridge.RunAsync(reader, client, machine)
                .GetAwaiter().GetResult();
            foreach (var diagnostic in result.Diagnostics)
                stderr.WriteLine(diagnostic);
            if (result.TraceCsv is null)
                return 1;
            var directory = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(output, result.TraceCsv);
            stdout.WriteLine(
                $"renode bridge: {result.Commands} command(s) -> {output}");
            return 0;
        }
        finally
        {
            reader.Dispose();
            tcp?.Dispose();
            client?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        var prefix = name + "=";
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith(prefix, StringComparison.Ordinal))
                return args[index][prefix.Length..];
            if (args[index] == name && index + 1 < args.Length)
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
