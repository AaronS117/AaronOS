using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace AaronOS.Modules.Trading.Brokerage;

/// <summary>
/// Keys for the paper broker and for the model. <see cref="AlpacaLive"/> exists as a field so the
/// value is explicit in storage rather than implied by which key happens to be present, but nothing
/// in this module sends it anywhere yet.
/// </summary>
public record TradingCredentials(
    string AlpacaKeyId,
    string AlpacaSecret,
    string AnthropicApiKey,
    bool AlpacaLive = false,
    /// <summary>
    /// Any endpoint speaking the OpenAI chat-completions format, which is how this runs for free:
    /// http://localhost:11434/v1 for Ollama, or a hosted free tier such as Groq or Gemini.
    /// </summary>
    string OpenAiBaseUrl = "",
    /// <summary>Empty for a local server, which accepts any key or none.</summary>
    string OpenAiApiKey = "");

/// <summary>
/// DPAPI-protected key storage under %LocalAppData%\AaronOS\, mirroring the Plaid store.
///
/// The keys never enter the SQLite database, are never logged, and never reach source control. That
/// last point is not incidental: this repository is public, and an Alpaca key with live trading
/// enabled would be an instruction to strangers.
/// </summary>
public class TradingCredentialStore
{
    private readonly string _filePath;

    public TradingCredentialStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AaronOS");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "trading-credentials.dat");
    }

    public bool HasCredentials => File.Exists(_filePath);

    public TradingCredentials? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var protectedBytes = File.ReadAllBytes(_filePath);
        var json = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<TradingCredentials>(json);
    }

    public void Save(TradingCredentials credentials)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var protectedBytes = ProtectedData.Protect(json, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, protectedBytes);
    }
}
