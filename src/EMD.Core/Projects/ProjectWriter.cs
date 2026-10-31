using System.Text.Json;
using Emd.Core.Model;

namespace Emd.Core.Projects;

public static class ProjectWriter
{
    public static string Serialize(EmdProject project) =>
        JsonSerializer.Serialize(project, CoreJson.Options);

    public static void Save(EmdProject project, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, Serialize(project));
    }
}
