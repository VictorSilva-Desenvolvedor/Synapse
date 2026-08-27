using System.Security.Cryptography;
using System.Text;
using Shouldly;
using Synapse.Sync.Crypto;

namespace Synapse.Tests.Crypto;

public class VaultCryptoTests
{
    [Fact]
    public void EncryptAndDecrypt_RoundTrip_ShouldRestoreOriginalContent()
    {
        var originalText = "# Nota Secreta do Obsidian\nConteúdo confidencial protegido com Zero-Knowledge AES-GCM.";
        var plaintext = Encoding.UTF8.GetBytes(originalText);
        var key = RandomNumberGenerator.GetBytes(VaultCrypto.KeySizeBytes);

        var encrypted = VaultCrypto.Encrypt(plaintext, key);

        VaultCrypto.IsEncrypted(encrypted).ShouldBeTrue();
        encrypted.ShouldNotBe(plaintext);

        var decrypted = VaultCrypto.Decrypt(encrypted, key);
        var restoredText = Encoding.UTF8.GetString(decrypted);

        restoredText.ShouldBe(originalText);
    }

    [Fact]
    public void Encrypt_MultipleCalls_ShouldGenerateUniqueNoncesAndDistinctCiphertexts()
    {
        var plaintext = Encoding.UTF8.GetBytes("Mesmo texto idêntico");
        var key = RandomNumberGenerator.GetBytes(VaultCrypto.KeySizeBytes);

        var encrypted1 = VaultCrypto.Encrypt(plaintext, key);
        var encrypted2 = VaultCrypto.Encrypt(plaintext, key);

        // Ciphertexts e nonces devem ser distintos
        encrypted1.ShouldNotBe(encrypted2);

        // Ambos devem descriptografar com sucesso para o mesmo texto
        Encoding.UTF8.GetString(VaultCrypto.Decrypt(encrypted1, key)).ShouldBe("Mesmo texto idêntico");
        Encoding.UTF8.GetString(VaultCrypto.Decrypt(encrypted2, key)).ShouldBe("Mesmo texto idêntico");
    }

    [Fact]
    public void Decrypt_WithWrongKey_ShouldThrowCryptographicException()
    {
        var plaintext = Encoding.UTF8.GetBytes("Segredo importante");
        var correctKey = RandomNumberGenerator.GetBytes(VaultCrypto.KeySizeBytes);
        var wrongKey = RandomNumberGenerator.GetBytes(VaultCrypto.KeySizeBytes);

        var encrypted = VaultCrypto.Encrypt(plaintext, correctKey);

        Should.Throw<CryptographicException>(() => VaultCrypto.Decrypt(encrypted, wrongKey));
    }

    [Fact]
    public void Decrypt_WithTamperedPayload_ShouldThrowCryptographicException()
    {
        var plaintext = Encoding.UTF8.GetBytes("Conteúdo intocado");
        var key = RandomNumberGenerator.GetBytes(VaultCrypto.KeySizeBytes);

        var encrypted = VaultCrypto.Encrypt(plaintext, key);

        // Corrompe o último byte do ciphertext
        encrypted[^1] ^= 0xFF;

        Should.Throw<CryptographicException>(() => VaultCrypto.Decrypt(encrypted, key));
    }

    [Fact]
    public void DeriveKey_WithPassphraseAndSalt_ShouldProduceDeterministicKey()
    {
        var passphrase = "MinhaSenhaSuperSecreta123!";
        var salt = VaultCrypto.GenerateSalt();

        var key1 = VaultCrypto.DeriveKey(passphrase, salt);
        var key2 = VaultCrypto.DeriveKey(passphrase, salt);

        key1.Length.ShouldBe(32);
        key1.ShouldBe(key2);
    }
}
