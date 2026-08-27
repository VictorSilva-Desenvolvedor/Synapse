using System.Security.Cryptography;
using System.Text;

namespace Synapse.Sync.Crypto;

/// <summary>
/// Provedor de criptografia Zero-Knowledge para notas do cofre usando AES-256-GCM (V2.2).
/// Garante que nenhum dado seja legível na nuvem sem a posse da chave mestra / passphrase.
/// </summary>
public static class VaultCrypto
{
    private static readonly byte[] HeaderMagic = "SYNAPSE_ENC_V1"u8.ToArray();
    public const int KeySizeBytes = 32; // 256 bits
    public const int NonceSizeBytes = 12; // 96 bits (padrão NIST para AES-GCM)
    public const int TagSizeBytes = 16; // 128 bits
    public const int SaltSizeBytes = 16; // 128 bits
    public const int Pbkdf2Iterations = 100_000;

    public static byte[] DeriveKey(string passphrase, byte[] salt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        ArgumentNullException.ThrowIfNull(salt);

        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeySizeBytes);
    }

    public static byte[] GenerateSalt()
    {
        return RandomNumberGenerator.GetBytes(SaltSizeBytes);
    }

    public static byte[] Encrypt(byte[] plaintext, byte[] key, byte[]? salt = null)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException($"A chave precisa ter exatamente {KeySizeBytes} bytes (256 bits).", nameof(key));
        }

        var usedSalt = salt ?? GenerateSalt();
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var tag = new byte[TagSizeBytes];
        var ciphertext = new byte[plaintext.Length];

        using (var aesGcm = new AesGcm(key, TagSizeBytes))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, HeaderMagic);
        }

        // Layout: [Magic 14B] + [Salt 16B] + [Nonce 12B] + [Tag 16B] + [Ciphertext NB]
        using var ms = new MemoryStream();
        ms.Write(HeaderMagic);
        ms.Write(usedSalt);
        ms.Write(nonce);
        ms.Write(tag);
        ms.Write(ciphertext);

        return ms.ToArray();
    }

    public static byte[] Decrypt(byte[] payload, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeySizeBytes)
        {
            throw new ArgumentException($"A chave precisa ter exatamente {KeySizeBytes} bytes (256 bits).", nameof(key));
        }

        var minLength = HeaderMagic.Length + SaltSizeBytes + NonceSizeBytes + TagSizeBytes;
        if (payload.Length < minLength)
        {
            throw new CryptographicException("Payload criptografado inválido ou truncado.");
        }

        // Valida cabeçalho
        if (!payload.AsSpan(0, HeaderMagic.Length).SequenceEqual(HeaderMagic))
        {
            throw new CryptographicException("Cabeçalho de criptografia Synapse não reconhecido.");
        }

        var offset = HeaderMagic.Length;

        var salt = payload.AsSpan(offset, SaltSizeBytes);
        offset += SaltSizeBytes;

        var nonce = payload.AsSpan(offset, NonceSizeBytes);
        offset += NonceSizeBytes;

        var tag = payload.AsSpan(offset, TagSizeBytes);
        offset += TagSizeBytes;

        var ciphertext = payload.AsSpan(offset);
        var plaintext = new byte[ciphertext.Length];

        using (var aesGcm = new AesGcm(key, TagSizeBytes))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, HeaderMagic);
        }

        return plaintext;
    }

    public static bool IsEncrypted(byte[] data)
    {
        if (data == null || data.Length < HeaderMagic.Length) return false;
        return data.AsSpan(0, HeaderMagic.Length).SequenceEqual(HeaderMagic);
    }
}
