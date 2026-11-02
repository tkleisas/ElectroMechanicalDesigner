using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace Emd.Core.Manufacturing;

public static partial class RenodeLogParser
{
    public static IReadOnlyList<string> ExtractUart(string log)
    {
        var events = new List<string>();
        foreach (var line in log.Replace("\r\n", "\n").Split('\n'))
        {
            var match = UartPattern().Match(StripAnsi(line));
            if (!match.Success)
                continue;
            var message = match.Groups["message"].Value.Trim();
            if (message.Length > 0)
                events.Add(message);
        }
        return events;
    }

    public static JsonArray ToJson(IReadOnlyList<string> events) =>
        new(events.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    [GeneratedRegex(@"\]\s*uart\d*:\s*(?<message>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex UartPattern();

    [GeneratedRegex(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.CultureInvariant)]
    private static partial Regex AnsiPattern();

    private static string StripAnsi(string value) =>
        AnsiPattern().Replace(value, "");
}
