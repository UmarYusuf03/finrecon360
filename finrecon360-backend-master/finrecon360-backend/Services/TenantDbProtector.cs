using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace finrecon360_backend.Services
{
    public interface ITenantDbProtector
    {
        string Protect(string plainText);
        string Unprotect(string protectedText);
    }

    public class TenantDbProtector : ITenantDbProtector
    {
        private readonly IDataProtector _protector;
        private readonly IDataProtector _legacyProtector;

        public TenantDbProtector(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("finrecon360.tenant-db");
            _legacyProtector = DataProtectionProvider
                .Create("finrecon360-backend")
                .CreateProtector("finrecon360.tenant-db");
        }

        public string Protect(string plainText)
        {
            return _protector.Protect(plainText);
        }

        public string Unprotect(string protectedText)
        {
            if (string.IsNullOrWhiteSpace(protectedText))
            {
                return protectedText;
            }

            try
            {
                return _protector.Unprotect(protectedText);
            }
            catch (CryptographicException)
            {
                try
                {
                    return _legacyProtector.Unprotect(protectedText);
                }
                catch (CryptographicException)
                {
                    // Backward compatibility for any legacy plain-text connection strings.
                    if (protectedText.Contains("Server=", StringComparison.OrdinalIgnoreCase))
                    {
                        return protectedText;
                    }

                    throw;
                }
            }
        }
    }
}
