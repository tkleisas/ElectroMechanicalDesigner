namespace Emd.Core.Tests;

internal static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string ExampleProject => Path.Combine(RepoRoot, "examples", "project.json");

    public static string Schema => Path.Combine(RepoRoot, "schema", "emd-project.schema.json");

    public static string Fixture(string name) => Path.Combine(RepoRoot, "tests", "EMD.Core.Tests", "Fixtures", name);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "schema", "emd-project.schema.json")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
