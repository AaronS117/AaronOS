using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace AaronOS.Modules.Finance.Plaid;

/// <summary>
/// Reads/writes app-level Plaid credentials as a DPAPI-protected (current-user scope) file under
/// %LocalAppData%\AaronOS\. The plaintext client_id/secret never touch the SQLite database, never
/// get logged, and never live in source control.
/// </summary>
public class PlaidCredentialStore
{
    private readonly string _filePath;

    public PlaidCredentialStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AaronOS");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "plaid-credentials.dat");
    }

    public bool HasCredentials => File.Exists(_filePath);

    public PlaidCredentials? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var protectedBytes = File.ReadAllBytes(_filePath);
        var json = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<PlaidCredentials>(json);
    }

    public void Save(PlaidCredentials credentials)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var protectedBytes = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, protectedBytes);
    }
}
