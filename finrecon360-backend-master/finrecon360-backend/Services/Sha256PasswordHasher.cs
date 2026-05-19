using System.Security.Cryptography;
using System.Text;

namespace finrecon360_backend.Services
{
    /// <summary>
    /// WHY: Implements password hashing for secure credential storage.
    /// SHA256 with base64 encoding provides one-way hashing so plaintext passwords are never stored.
    /// This service is injected wherever users set or reset passwords (onboarding, password-reset flow, admin user creation).
    /// Centralizing password hashing here ensures consistent treatment across all auth flows.
    /// </summary>
    public class Sha256PasswordHasher : IPasswordHasher
    {
        public string Hash(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        public bool Verify(string password, string passwordHash)
        {
            return Hash(password) == passwordHash;
        }
    }
}
