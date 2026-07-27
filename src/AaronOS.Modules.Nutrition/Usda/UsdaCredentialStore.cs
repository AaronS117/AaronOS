using System.IO;

namespace AaronOS.Modules.Nutrition.Usda;

/// <summary>
/// Reads/writes the USDA FoodData Central API key as a DPAPI-protected (current-user scope) file
/// under %LocalAppData%\AaronOS\, mirroring AaronOS.Modules.Finance.Plaid.PlaidCredentialStore.
/// The plaintext key never touches the SQLite database, never gets logged, and never lives in
/// source control.
/// </summary>
public class UsdaCredentialStore
{
    private readonly string _filePath;

    public UsdaCredentialStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AaronOS");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "usda-credentials.dat");
    }

    public bool HasApiKey => File.Exists(_filePath);

    public string? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var encrypted = File.ReadAllBytes(_filePath);
        return ApiKeyProtector.Unprotect(encrypted);
    }

    public void Save(string apiKey)
    {
        File.WriteAllBytes(_filePath, ApiKeyProtector.Protect(apiKey));
    }
}
