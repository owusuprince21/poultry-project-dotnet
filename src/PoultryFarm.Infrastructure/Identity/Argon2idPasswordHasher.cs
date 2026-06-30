using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace PoultryFarm.Infrastructure.Identity;

public sealed class Argon2idPasswordHasher : IPasswordHasher<ApplicationUser>
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemoryKiB = 65536;
    private const int Iterations = 3;
    private const int Parallelism = 2;
    private readonly PasswordHasher<ApplicationUser> _identityHasher = new();

    public string HashPassword(ApplicationUser user, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Hash(password, salt, MemoryKiB, Iterations, Parallelism);
        return string.Join(
            '$',
            string.Empty,
            "argon2id",
            "v=19",
            $"m={MemoryKiB},t={Iterations},p={Parallelism}",
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        if (hashedPassword.StartsWith("$argon2id$", StringComparison.Ordinal))
        {
            return VerifyArgon2id(hashedPassword, providedPassword);
        }

        var identityResult = _identityHasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
        return identityResult == PasswordVerificationResult.Failed
            ? PasswordVerificationResult.Failed
            : PasswordVerificationResult.SuccessRehashNeeded;
    }

    private static PasswordVerificationResult VerifyArgon2id(string hashedPassword, string providedPassword)
    {
        var parts = hashedPassword.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 ||
            parts[0] != "argon2id" ||
            parts[1] != "v=19" ||
            !TryParseParameters(parts[2], out var memoryKiB, out var iterations, out var parallelism))
        {
            return PasswordVerificationResult.Failed;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Hash(providedPassword, salt, memoryKiB, iterations, parallelism, expected.Length);

            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                return PasswordVerificationResult.Failed;
            }

            return memoryKiB < MemoryKiB || iterations < Iterations || parallelism < Parallelism
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Success;
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
    }

    private static byte[] Hash(
        string password,
        byte[] salt,
        int memoryKiB,
        int iterations,
        int parallelism,
        int hashSize = HashSize)
    {
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };

        return argon2.GetBytes(hashSize);
    }

    private static bool TryParseParameters(string value, out int memoryKiB, out int iterations, out int parallelism)
    {
        memoryKiB = 0;
        iterations = 0;
        parallelism = 0;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length != 2 || !int.TryParse(keyValue[1], out var parsed))
            {
                return false;
            }

            switch (keyValue[0])
            {
                case "m":
                    memoryKiB = parsed;
                    break;
                case "t":
                    iterations = parsed;
                    break;
                case "p":
                    parallelism = parsed;
                    break;
            }
        }

        return memoryKiB > 0 && iterations > 0 && parallelism > 0;
    }
}
