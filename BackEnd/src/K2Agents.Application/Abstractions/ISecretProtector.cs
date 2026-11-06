namespace K2Agents.Application.Abstractions;

/// <summary>
/// Criptografia de segredos guardados no banco (tokens de canais, como o do WhatsApp).
/// O texto puro nunca é persistido, retornado pela API nem registrado em logs.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>Lança <see cref="System.Security.Cryptography.CryptographicException"/> se o dado foi alterado ou a chave mudou.</summary>
    string Unprotect(string protectedValue);
}
