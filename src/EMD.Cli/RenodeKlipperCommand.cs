using System.Net.Sockets;
using Emd.Adapters;
using Emd.Adapters.MechMaker;

namespace Emd.Cli;

public static class RenodeKlipperCommand
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var machine = GetOption(args, "--machine");
        var mechmaker = GetOption(args, "--mechmaker");
        var output = GetOption(args, "--out");
        var uartFile = GetOption(args, "--uart-file");
        var endpoint = GetOption(args, "--endpoint");
        if (string.IsNullOrWhiteSpace(machine) || string.IsNullOrWhiteSpace(mechmaker) ||
            string.IsNullOrWhiteSpace(output))
            return Usage(stderr, "renode klipper requires --machine, --mechmaker, and --out.");
        if (string.IsNullOrWhiteSpace(uartFile) == string.IsNullOrWhiteSpace(endpoint))
            return Usage(stderr, "renode klipper requires exactly one of --uart-file or --endpoint.");

        if (!double.TryParse(GetOption(args, "--run-step") ?? "0.01",
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var runStep))
            return Usage(stderr, "--run-step must be a number.");

        TcpClient? tcp = null;
        Stream input;
        Stream transportOutput;
        if (!string.IsNullOrWhiteSpace(uartFile))
        {
            input = File.OpenRead(uartFile);
            transportOutput = Stream.Null;
        }
        else
        {
            var parts = endpoint!.Split(':', 2);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
                return Usage(stderr, "--endpoint must be host:port.");
            tcp = new TcpClient();
            tcp.Connect(parts[0], port);
            input = tcp.GetStream();
            transportOutput = input;
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
                    Path.GetDirectoryName(mechmaker))
                .GetAwaiter().GetResult();
            var result = new KlipperUartBridge()
                .RunAsync(input, transportOutput, client, machine, runStep)
                .GetAwaiter().GetResult();
            foreach (var diagnostic in result.Diagnostics)
                stderr.WriteLine(diagnostic);
            if (result.TraceCsv is null)
                return 1;
            var directory = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(output, result.TraceCsv);
            stdout.WriteLine($"renode klipper: {result.Commands} command(s) -> {output}");
            return 0;
        }
        finally
        {
            input.Dispose();
            if (transportOutput != input)
                transportOutput.Dispose();
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
