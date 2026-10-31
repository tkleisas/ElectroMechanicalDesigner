using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Emd.Core.Diagnostics;

namespace Emd.Core.Packaging;

public sealed record PackageVerifyResult(IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

/// <summary>Verifies a package directory against its hashed manifest.</summary>
public sealed class PackageVerifier
{
    public static PackageVerifier Default { get; } = new();

    public PackageVerifyResult VerifyDirectory(string directory)
    {
        var diagnostics = new List<Diagnostic>();
        var root = Path.GetFullPath(directory);
        var manifestPath = Path.Combine(root, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            diagnostics.Add(new Diagnostic(
                "EMD-PACKAGE-003",
                DiagnosticSeverity.Error,
                $"Package manifest '{manifestPath}' was not found."));
            return new PackageVerifyResult(diagnostics);
        }

        JsonObject? manifest;
        try
        {
            manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-PACKAGE-003",
                DiagnosticSeverity.Error,
                $"Package manifest is invalid: {exception.Message}"));
            return new PackageVerifyResult(diagnostics);
        }

        if (manifest is null ||
            (string?)manifest["schema_version"] != "0.1" ||
            manifest["files"] is not JsonArray files)
        {
            diagnostics.Add(new Diagnostic(
                "EMD-PACKAGE-003",
                DiagnosticSeverity.Error,
                "Package manifest is missing schema_version/files."));
            return new PackageVerifyResult(diagnostics);
        }

        var listed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entryNode in files)
        {
            if (entryNode is not JsonObject entry)
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-PACKAGE-003",
                    DiagnosticSeverity.Error,
                    "Package manifest contains an invalid file entry."));
                continue;
            }

            var name = (string?)entry["name"];
            var expectedHash = (string?)entry["sha256"];
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(expectedHash))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-PACKAGE-003",
                    DiagnosticSeverity.Error,
                    "Package manifest file entry is missing name/sha256."));
                continue;
            }

            listed.Add(name);
            var fullPath = ResolveSafePath(root, name, diagnostics);
            if (fullPath is null)
                continue;

            if (!File.Exists(fullPath))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-PACKAGE-004",
                    DiagnosticSeverity.Error,
                    $"Package member '{name}' is missing.",
                    Entity: name));
                continue;
            }

            var content = File.ReadAllBytes(fullPath);
            var actualHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            long? expectedBytes = null;
            if (entry["bytes"] is { } bytesNode)
            {
                try
                {
                    expectedBytes = bytesNode.GetValue<long>();
                }
                catch (FormatException)
                {
                    // A malformed byte count is handled by the hash/size mismatch below.
                }
            }

            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase) ||
                (expectedBytes is not null && content.LongLength != expectedBytes))
            {
                diagnostics.Add(new Diagnostic(
                    "EMD-PACKAGE-005",
                    DiagnosticSeverity.Error,
                    $"Package member '{name}' does not match the manifest hash/size.",
                    Entity: name));
            }
        }

        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative == "manifest.json" || listed.Contains(relative))
                continue;

            diagnostics.Add(new Diagnostic(
                "EMD-PACKAGE-006",
                DiagnosticSeverity.Warning,
                $"Package contains unlisted file '{relative}'.",
                Entity: relative));
        }

        return new PackageVerifyResult(diagnostics);
    }

    private static string? ResolveSafePath(
        string root,
        string name,
        List<Diagnostic> diagnostics)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, name));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (fullPath.StartsWith(prefix, StringComparison.Ordinal))
            return fullPath;

        diagnostics.Add(new Diagnostic(
            "EMD-PACKAGE-007",
            DiagnosticSeverity.Error,
            $"Package member '{name}' escapes the package directory.",
            Entity: name));
        return null;
    }
}
