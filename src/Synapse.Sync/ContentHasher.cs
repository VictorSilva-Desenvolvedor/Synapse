using System.Security.Cryptography;
using System.Text;

namespace Synapse.Sync;

/// <summary>SHA-256 do conteúdo (RF-SYNC.3) — compara conteúdo, não mtime, para evitar falso positivo por toque de arquivo sem mudança real.</summary>
internal static class ContentHasher
{
    public static string Sha256(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
