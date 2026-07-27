using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace AaronOS.Modules.Medical.Withings;

/// <summary>
/// Reads/writes Withings app credentials and OAuth tokens as a DPAPI-protected (current-user scope)
/// file under %LocalAppData%\AaronOS\, mirroring
/// AaronOS.Modules.Finance.Plaid.PlaidCredentialStore. The client secret and refresh token never
/// touch the SQLite database, never get logged, and never live in source control.
///
/// Third copy of this DPAPI-file pattern now (Plaid, USDA, here). Worth promoting to Core the next
/// time one is needed; not worth churning two working modules today.
/// </summary>
public class WithingsCredentialStore
{
    private readonly string _filePath;

    public WithingsCredentialStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AaronOS");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "withings-credentials.dat");
    }

    public bool HasCredentials => File.Exists(_filePath);

    public WithingsCredentials? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var protectedBytes = File.ReadAllBytes(_filePath);
        var json = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<WithingsCredentials>(json);
    }

    public void Save(WithingsCredentials credentials)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var protectedBytes = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, protectedBytes);
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }
}
