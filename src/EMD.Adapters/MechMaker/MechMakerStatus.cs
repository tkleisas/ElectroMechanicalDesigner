using System.Globalization;
using System.Text.RegularExpressions;

namespace Emd.Adapters.MechMaker;

public sealed record MechMakerMotorStatus(
    string Id,
    double CommandedRev,
    double ActualRev,
    double VelocityRevPerSec,
    int MissedSteps,
    bool Disabled,
    bool Stalled);

public sealed record MechMakerEndstopStatus(string Joint, bool Pressed);

public sealed record MechMakerStatus(
    double Time,
    IReadOnlyList<MechMakerMotorStatus> Motors,
    IReadOnlyList<MechMakerEndstopStatus> Endstops);

public static class MechMakerStatusParser
{
    private static readonly Regex TimeRegex = new(
        @"^t = (?<time>-?[0-9.,]+) s$", RegexOptions.Compiled);

    private static readonly Regex MotorRegex = new(
        @"^(?<id>[^:]+?)(?<disabled> \(disabled\))?: commanded (?<commanded>-?[0-9.,]+) rev / actual (?<actual>-?[0-9.,]+) rev, velocity (?<velocity>-?[0-9.,]+) rev/s.*?missed steps (?<missed>\d+)(?<stalled>, STALLED)?",
        RegexOptions.Compiled);

    private static readonly Regex EndstopRegex = new(
        @"^endstop (?<joint>[^:]+): (?<state>PRESSED|open)$", RegexOptions.Compiled);

    public static bool TryParse(string text, out MechMakerStatus? status, out string? error)
    {
        status = null;
        error = null;
        double? time = null;
        var motors = new List<MechMakerMotorStatus>();
        var endstops = new List<MechMakerEndstopStatus>();

        foreach (var line in text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0))
        {
            var timeMatch = TimeRegex.Match(line);
            if (timeMatch.Success)
            {
                time = ParseNumber(timeMatch.Groups["time"].Value);
                continue;
            }

            var motorMatch = MotorRegex.Match(line);
            if (motorMatch.Success)
            {
                motors.Add(new MechMakerMotorStatus(
                    motorMatch.Groups["id"].Value,
                    ParseNumber(motorMatch.Groups["commanded"].Value),
                    ParseNumber(motorMatch.Groups["actual"].Value),
                    ParseNumber(motorMatch.Groups["velocity"].Value),
                    int.Parse(motorMatch.Groups["missed"].Value, CultureInfo.InvariantCulture),
                    motorMatch.Groups["disabled"].Success,
                    motorMatch.Groups["stalled"].Success));
                continue;
            }

            var endstopMatch = EndstopRegex.Match(line);
            if (endstopMatch.Success)
            {
                endstops.Add(new MechMakerEndstopStatus(
                    endstopMatch.Groups["joint"].Value,
                    endstopMatch.Groups["state"].Value == "PRESSED"));
            }
        }

        if (time is null)
        {
            error = "MechMaker status has no time line.";
            return false;
        }

        status = new MechMakerStatus(time.Value, motors, endstops);
        return true;
    }

    private static double ParseNumber(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant))
            return invariant;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var current))
            return current;
        return double.Parse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
