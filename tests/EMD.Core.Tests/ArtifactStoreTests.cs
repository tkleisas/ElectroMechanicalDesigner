using System.Security.Cryptography;
using System.Text;
using Emd.Core.Artifacts;

namespace Emd.Core.Tests;

public class ArtifactStoreTests
{
    private static readonly byte[] HelloBytes = Encoding.UTF8.GetBytes("hello artifact");

    [Fact]
    public void Put_stores_content_by_sha256()
    {
        using var temp = new TempDirectory();
        var store = new ArtifactStore(temp.Path);

        var result = store.Put(HelloBytes);

        var expectedHash = Convert.ToHexString(SHA256.HashData(HelloBytes)).ToLowerInvariant();
        Assert.Equal(expectedHash, result.Sha256);
        Assert.Equal($"artifact://sha256/{expectedHash}", result.Reference);
        Assert.Equal(HelloBytes.Length, result.Bytes);
        Assert.True(store.Contains(result.Sha256));
        Assert.Equal(HelloBytes, store.Read(result.Sha256));
        Assert.True(store.Verify(result.Sha256));
    }

    [Fact]
    public void Put_is_idempotent()
    {
        using var temp = new TempDirectory();
        var store = new ArtifactStore(temp.Path);

        var first = store.Put(HelloBytes);
        var second = store.Put(HelloBytes);

        Assert.Equal(first, second);
        var files = Directory.GetFiles(Path.Combine(temp.Path, "sha256"), "*", SearchOption.AllDirectories);
        Assert.Single(files);
    }

    [Fact]
    public void PutFile_hashes_the_file_contents()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "source.bin");
        File.WriteAllBytes(source, HelloBytes);
        var store = new ArtifactStore(Path.Combine(temp.Path, "store"));

        var result = store.PutFile(source);

        Assert.Equal(HelloBytes, store.Read(result.Sha256));
    }

    [Fact]
    public void Verify_detects_corrupted_content()
    {
        using var temp = new TempDirectory();
        var store = new ArtifactStore(temp.Path);
        var result = store.Put(HelloBytes);

        File.WriteAllText(store.GetPath(result.Sha256), "corrupted");

        Assert.False(store.Verify(result.Sha256));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("not-a-hash")]
    [InlineData("ABCDEF")]
    public void GetPath_rejects_invalid_hashes(string hash)
    {
        using var temp = new TempDirectory();
        var store = new ArtifactStore(temp.Path);

        Assert.Throws<ArgumentException>(() => store.GetPath(hash));
    }

    [Fact]
    public void GetPath_stays_inside_the_store_root()
    {
        using var temp = new TempDirectory();
        var store = new ArtifactStore(temp.Path);
        var result = store.Put(HelloBytes);

        var path = store.GetPath(result.Sha256);

        Assert.StartsWith(Path.GetFullPath(temp.Path), Path.GetFullPath(path), StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveReference_accepts_artifact_sha256_uris()
    {
        using var temp = new TempDirectory();
        var store = new ArtifactStore(temp.Path);
        var result = store.Put(HelloBytes);

        Assert.True(store.TryResolve(result.Reference, out var path));
        Assert.Equal(store.GetPath(result.Sha256), path);
        Assert.False(store.TryResolve("file:///etc/passwd", out _));
    }
}
