using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Emd.Core.Artifacts;

public sealed record ArtifactPutResult(
    string Sha256,
    string RelativePath,
    string Reference,
    long Bytes);

public sealed class ArtifactStore
{
    private const string ReferencePrefix = "artifact://sha256/";

    private static readonly Regex Sha256Pattern = new("^[a-f0-9]{64}$", RegexOptions.Compiled);

    public ArtifactStore(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }

    public ArtifactPutResult Put(byte[] content)
    {
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var target = GetPath(hash);

        if (!File.Exists(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, content);
                File.Move(temp, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        var relativePath = Path.GetRelativePath(RootPath, target).Replace('\\', '/');
        return new ArtifactPutResult(hash, relativePath, ReferenceFor(hash), content.LongLength);
    }

    public ArtifactPutResult Put(Stream content)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return Put(buffer.ToArray());
    }

    public ArtifactPutResult PutFile(string path) => Put(File.ReadAllBytes(path));

    public bool Contains(string sha256) =>
        Sha256Pattern.IsMatch(sha256) && File.Exists(GetPath(sha256));

    public string GetPath(string sha256)
    {
        if (!Sha256Pattern.IsMatch(sha256))
            throw new ArgumentException("Expected a lowercase SHA-256 hex digest.", nameof(sha256));

        return Path.Combine(RootPath, "sha256", sha256[..2], sha256);
    }

    public byte[] Read(string sha256) => File.ReadAllBytes(GetPath(sha256));

    public bool Verify(string sha256)
    {
        var path = GetPath(sha256);
        if (!File.Exists(path))
            return false;

        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        return string.Equals(actual, sha256, StringComparison.Ordinal);
    }

    public static string ReferenceFor(string sha256) => $"{ReferencePrefix}{sha256}";

    public bool TryResolve(string reference, out string path)
    {
        path = "";
        if (!reference.StartsWith(ReferencePrefix, StringComparison.Ordinal))
            return false;

        var hash = reference[ReferencePrefix.Length..];
        if (!Sha256Pattern.IsMatch(hash))
            return false;

        path = GetPath(hash);
        return true;
    }
}
