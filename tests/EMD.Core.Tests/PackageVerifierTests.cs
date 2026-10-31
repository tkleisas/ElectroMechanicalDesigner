using System.Text;
using System.Text.Json.Nodes;
using Emd.Core.Exporters;
using Emd.Core.Packaging;
using Emd.Core.Projects;

namespace Emd.Core.Tests;

public class PackageVerifierTests
{
    [Fact]
    public void Verify_accepts_a_fresh_package()
    {
        using var temp = new TempDirectory();
        WritePackage(temp.Path);

        var result = PackageVerifier.Default.VerifyDirectory(temp.Path);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == Emd.Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void Verify_reports_corrupted_member()
    {
        using var temp = new TempDirectory();
        WritePackage(temp.Path);
        File.AppendAllText(Path.Combine(temp.Path, "bom.csv"), "corrupt");

        var result = PackageVerifier.Default.VerifyDirectory(temp.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-PACKAGE-005");
    }

    [Fact]
    public void Verify_reports_missing_member()
    {
        using var temp = new TempDirectory();
        WritePackage(temp.Path);
        File.Delete(Path.Combine(temp.Path, "scene.xml"));

        var result = PackageVerifier.Default.VerifyDirectory(temp.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-PACKAGE-004");
    }

    [Fact]
    public void Verify_reports_missing_manifest()
    {
        using var temp = new TempDirectory();

        var result = PackageVerifier.Default.VerifyDirectory(temp.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-PACKAGE-003");
    }

    [Fact]
    public void Verify_warns_about_extra_files()
    {
        using var temp = new TempDirectory();
        WritePackage(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "extra.txt"), "extra");

        var result = PackageVerifier.Default.VerifyDirectory(temp.Path);

        Assert.True(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-PACKAGE-006");
    }

    [Fact]
    public void Verify_rejects_path_traversal()
    {
        using var temp = new TempDirectory();
        WritePackage(temp.Path);
        var manifestPath = Path.Combine(temp.Path, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["files"]![0]!["name"] = "../escape.txt";
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        var result = PackageVerifier.Default.VerifyDirectory(temp.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-PACKAGE-007");
    }

    [Fact]
    public void Verify_reports_invalid_manifest()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "manifest.json"), "{ not json");

        var result = PackageVerifier.Default.VerifyDirectory(temp.Path);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "EMD-PACKAGE-003");
    }

    private static void WritePackage(string directory)
    {
        var loaded = ProjectLoader.Default.LoadFile(TestPaths.ExampleProject);
        Assert.True(loaded.IsValid, string.Join(Environment.NewLine, loaded.Diagnostics));
        var package = PackageExporter.Default.Export(loaded.Project!);
        Assert.True(package.IsValid, string.Join(Environment.NewLine, package.Diagnostics));

        foreach (var file in package.Files)
            File.WriteAllBytes(Path.Combine(directory, file.Name), file.Content);
    }
}
