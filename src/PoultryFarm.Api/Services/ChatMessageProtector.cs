using System.Security.Cryptography;
using System.Text;

namespace PoultryFarm.Api.Services;

public interface IChatMessageProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}

public sealed class ChatMessageProtector(IConfiguration configuration) : IChatMessageProtector
{
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key = ResolveKey(configuration);

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return plaintext;
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSize + TagSize, ciphertext.Length);

        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText) ||
            !protectedText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return protectedText;
        }

        try
        {
            var payload = Convert.FromBase64String(protectedText[Prefix.Length..]);
            if (payload.Length <= NonceSize + TagSize)
            {
                return string.Empty;
            }

            var nonce = payload[..NonceSize];
            var tag = payload[NonceSize..(NonceSize + TagSize)];
            var ciphertext = payload[(NonceSize + TagSize)..];
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException)
        {
            return "[Encrypted message could not be opened]";
        }
        catch (FormatException)
        {
            return "[Encrypted message could not be opened]";
        }
    }

    private static byte[] ResolveKey(IConfiguration configuration)
    {
        var configuredKey = configuration["ChatEncryption:KeyBase64"];
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            var key = Convert.FromBase64String(configuredKey);
            if (key.Length is 16 or 24 or 32)
            {
                return key;
            }

            throw new InvalidOperationException("ChatEncryption:KeyBase64 must decode to a 16, 24, or 32 byte AES key.");
        }

        var fallbackMaterial = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("ChatEncryption:KeyBase64 or Jwt:SigningKey is required.");
        return SHA256.HashData(Encoding.UTF8.GetBytes(fallbackMaterial));
    }
}
