using System.Text.RegularExpressions;

namespace Emd.Core.Importers;

internal static class Slug
{
    private static readonly Regex InvalidChars = new("[^A-Za-z0-9_.-]+", RegexOptions.Compiled);

    public static string Create(string value, string fallback = "project")
    {
        var key = InvalidChars.Replace(value.Trim(), "_").Trim('_');
        if (key.Length == 0)
            key = fallback;

        if (!(char.IsLetter(key[0]) || key[0] == '_'))
            key = "_" + key;

        return key.ToLowerInvariant();
    }

    public static string Key(string value, string fallback = "key")
    {
        var key = InvalidChars.Replace(value.Trim(), "_").Trim('_');
        if (key.Length == 0)
            key = fallback;

        if (!(char.IsLetter(key[0]) || key[0] == '_'))
            key = "_" + key;

        return key;
    }
}
