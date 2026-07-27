using System.Security.Cryptography;
using System.Text;

namespace AaronOS.Modules.Finance.Plaid;

/// <summary>DPAPI (current-user scope) protection for a single access token value, stored as
/// PlaidItem.AccessTokenEncrypted — never persisted or logged in plaintext.</summary>
public static class AccessTokenProtector
{
    public static byte[] Protect(string accessToken) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(accessToken), optionalEntropy: null, DataProtectionScope.CurrentUser);

    public static string Unprotect(byte[] encrypted) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser));
}
