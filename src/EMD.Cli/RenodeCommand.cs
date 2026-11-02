using Emd.Adapters;
using Emd.Core.Model;
using Emd.Core.Projects;

namespace Emd.Cli;

public static class RenodeCommand
{
    public static int Run(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        RenodeAdapter adapter)
    {
        if (args.Length == 0 ||
            !string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
            return Usage(stderr, "renode expects the 'run' subcommand.");

        var projectPath = GetOption(args, "--project");
        if (string.IsNullOrWhiteSpace(projectPath))
            return Usage(stderr, "renode run requires --project <project.json>.");
        var loaded = ProjectLoader.Default.LoadFile(projectPath);
        foreach (var diagnostic in loaded.Diagnostics)
            stderr.WriteLine(diagnostic);
        if (loaded.Project is null)
            return 1;

        var firmware = SelectFirmware(loaded.Project, GetOption(args, "--firmware-project"));
        if (firmware is null)
        {
            stderr.WriteLine("emd: project has no firmware project.");
            return 1;
        }

        var firmwarePath = GetOption(args, "--firmware") ?? ArtifactPath(loaded.Project, firmware.BinaryArtifactId)
            ?? ArtifactPath(loaded.Project, firmware.SourceArtifactId);
        var platform = GetOption(args, "--platform") ?? firmware.RenodePlatform;
        if (string.IsNullOrWhiteSpace(firmwarePath) || !File.Exists(firmwarePath))
        {
            stderr.WriteLine("emd: firmware ELF was not found; pass --firmware or add a binary artifact.");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(platform) || !File.Exists(platform))
        {
            stderr.WriteLine("emd: Renode platform was not found; pass --platform or set renode_platform.");
            return 1;
        }

        var output = GetOption(args, "--out") ?? "out/renode.log";
        if (!double.TryParse(GetOption(args, "--duration") ?? "2",
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var duration))
        {
            stderr.WriteLine("emd: --duration must be a number.");
            return 1;
        }

        var result = adapter.RunAsync(
                firmwarePath, platform, output, duration, GetOption(args, "--uart"))
            .GetAwaiter().GetResult();
        foreach (var diagnostic in result.Diagnostics)
            stderr.WriteLine(diagnostic);
        if (!result.Success)
            return 1;

        var eventsPath = GetOption(args, "--events");
        if (!string.IsNullOrWhiteSpace(eventsPath))
        {
            var events = Emd.Core.Manufacturing.RenodeLogParser.ExtractUart(
                File.ReadAllText(output));
            var directory = Path.GetDirectoryName(Path.GetFullPath(eventsPath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(
                eventsPath,
                Emd.Core.Manufacturing.RenodeLogParser.ToJson(events).ToJsonString());
            stdout.WriteLine($"renode UART events: {events.Count} -> {eventsPath}");
        }

        stdout.WriteLine($"renode: {firmware.Key} -> {output}");
        return 0;
    }

    private static FirmwareProject? SelectFirmware(EmdProject project, string? key) =>
        project.Firmware?.Projects.FirstOrDefault(firmware =>
            string.IsNullOrWhiteSpace(key) || firmware.Key == key) ??
        project.Firmware?.Projects.FirstOrDefault();

    private static string? ArtifactPath(EmdProject project, Guid? id) =>
        id is null ? null : project.Artifacts.FirstOrDefault(artifact => artifact.Id == id)?.Path;

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
