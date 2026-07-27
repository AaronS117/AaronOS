using System.Security.Cryptography;
using System.Text;

namespace AaronOS.Modules.Nutrition.Usda;

/// <summary>DPAPI (current-user scope) protection for the USDA FoodData Central API key, mirroring
/// AaronOS.Modules.Finance.Plaid.AccessTokenProtector — duplicated rather than shared, since
/// modules can't reference each other's internals and one small DPAPI helper isn't worth
/// promoting to Core for two callers yet.</summary>
public static class ApiKeyProtector
{
    public static byte[] Protect(string apiKey) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), optionalEntropy: null, DataProtectionScope.CurrentUser);

    public static string Unprotect(byte[] encrypted) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser));
}
