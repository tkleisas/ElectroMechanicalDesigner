using System.Globalization;

namespace Emd.Core.Manufacturing;

public sealed record FirmwareCommand(string Kind, string? Target, double Value);

public static class FirmwareCommandParser
{
    public static bool TryParse(string line, out FirmwareCommand? command, out string? error)
    {
        command = null;
        error = null;
        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#') || text.StartsWith(';'))
            return false;

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var kind = parts[0].ToUpperInvariant();
        switch (kind)
        {
            case "MOTOR":
            case "M":
                if (parts.Length != 3 || !TryNumber(parts[2], out var rev))
                {
                    error = "expected MOTOR <instance-id> <rev/s>";
                    return false;
                }
                command = new FirmwareCommand("motor", parts[1], rev);
                return true;
            case "STOP":
                if (parts.Length != 2)
                {
                    error = "expected STOP <instance-id>";
                    return false;
                }
                command = new FirmwareCommand("stop", parts[1], 0);
                return true;
            case "RUN":
                if (parts.Length != 2 || !TryNumber(parts[1], out var seconds) || seconds < 0)
                {
                    error = "expected RUN <seconds>";
                    return false;
                }
                command = new FirmwareCommand("run", null, seconds);
                return true;
            case "ENABLE":
                if (parts.Length != 3 || !int.TryParse(parts[2], out var enabled) ||
                    enabled is not (0 or 1))
                {
                    error = "expected ENABLE <instance-id> <0|1>";
                    return false;
                }
                command = new FirmwareCommand("enable", parts[1], enabled);
                return true;
            default:
                error = $"unknown firmware command '{parts[0]}'";
                return false;
        }
    }

    private static bool TryNumber(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
}
