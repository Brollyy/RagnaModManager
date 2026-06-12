using System.Security.Cryptography;

namespace RagnaModManager.Core.Checksums;

public static class Sha256
{
    public static string FileChecksum(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
