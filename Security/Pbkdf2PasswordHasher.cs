using System.Security.Cryptography;
using System.Text;

namespace CONATRADEC_API.Security
{
    public class Pbkdf2PasswordHasher
    {
        // Guarda: PBKDF2$<iter>$<saltB64>$<hashB64>
        public static string HashToString(string password, int iterations = 100_000, int saltSize = 16, int keySize = 32)
        {
            var salt = RandomNumberGenerator.GetBytes(saltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                keySize);

            return $"PBKDF2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        public static bool VerifyFromString(string password, string stored)
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != "PBKDF2") return false;

            var iterations = int.Parse(parts[1]);
            var salt = Convert.FromBase64String(parts[2]);
            var hash = Convert.FromBase64String(parts[3]);

            var test = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                hash.Length);

            return CryptographicOperations.FixedTimeEquals(test, hash);
        }
    }
}
