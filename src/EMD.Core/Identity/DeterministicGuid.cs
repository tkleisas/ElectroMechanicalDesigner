using System.Security.Cryptography;
using System.Text;

namespace Emd.Core.Identity;

public static class EmdNamespaces
{
    public static readonly Guid MechMaker = new("7a1d4e2c-9b3f-4c8a-8d6e-5f0a1b2c3d4e");

    public static readonly Guid KiCad = new("0f5b2a7c-4d1e-4a8b-9c6d-3e2f1a0b9c8d");

    public static readonly Guid FreeCad = new("2c9e4b1a-7f3d-4e6a-b8c5-1d0f9e8a7b6c");
}

/// <summary>RFC 4122 version-5 UUIDs: stable across imports for the same namespace and name.</summary>
public static class DeterministicGuid
{
    public static Guid Create(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var data = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, data, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, data, namespaceBytes.Length, nameBytes.Length);

        var hash = SHA1.HashData(data);
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, guidBytes.Length);

        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        SwapByteOrder(guidBytes);
        return new Guid(guidBytes);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        Swap(guid, 0, 3);
        Swap(guid, 1, 2);
        Swap(guid, 4, 5);
        Swap(guid, 6, 7);
    }

    private static void Swap(byte[] bytes, int left, int right) =>
        (bytes[left], bytes[right]) = (bytes[right], bytes[left]);
}
