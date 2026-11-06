using System.Security.Cryptography;
using System.Text;
using K2Agents.Application.Abstractions;

namespace K2Agents.Infrastructure.Security;

/// <summary>
/// AES-256-GCM (confidencialidade + integridade). Formato: "v1:" + Base64(nonce[12] | tag[16] | cifra).
/// A chave vem de Secrets:EncryptionKey (32 bytes em Base64, variável SECRETS_ENCRYPTION_KEY).
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmSecretProtector(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("A chave de criptografia deve ter 32 bytes (256 bits).", nameof(key));
        _key = key;
    }

    /// <summary>Lê a chave em Base64; null/vazio ou tamanho errado resulta em erro claro (sem expor o valor).</summary>
    public static byte[] ParseKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) throw new InvalidOperationException("Secrets:EncryptionKey não configurada. Defina SECRETS_ENCRYPTION_KEY no .env.");
        byte[] key;
        try { key = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException) { throw new InvalidOperationException("SECRETS_ENCRYPTION_KEY deve estar em Base64 (ex.: openssl rand -base64 32)."); }
        if (key.Length != 32) throw new InvalidOperationException($"SECRETS_ENCRYPTION_KEY deve ter 32 bytes; recebido: {key.Length}.");
        return key;
    }

    public string Protect(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + TagSize + data.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));
        return Prefix + Convert.ToBase64String(output);
    }

    public string Unprotect(string protectedValue)
    {
        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal)) throw new CryptographicException("Formato de segredo desconhecido.");
        var input = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        if (input.Length < NonceSize + TagSize) throw new CryptographicException("Segredo corrompido.");
        var plaintext = new byte[input.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize + TagSize), input.AsSpan(NonceSize, TagSize), plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}
