using AaronOS.Core;
using AaronOS.Modules.Trading.Brokerage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AaronOS.Modules.Trading.ViewModels;

/// <summary>
/// Entry for the paper-broker and model keys.
///
/// Saved keys are never read back into the boxes. Showing a secret so it can be re-saved unchanged
/// puts it on screen for no benefit; the status line says whether keys exist, and typing new ones
/// replaces them.
/// </summary>
public partial class TradingKeysViewModel(TradingCredentialStore credentialStore) : ViewModelBase
{
    [ObservableProperty]
    private string _alpacaKeyId = "";

    [ObservableProperty]
    private string _alpacaSecret = "";

    [ObservableProperty]
    private string _anthropicApiKey = "";

    /// <summary>Not a secret, so unlike the keys this one is shown back and edited in place.</summary>
    [ObservableProperty]
    private string _openAiBaseUrl = "";

    [ObservableProperty]
    private string _openAiApiKey = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasSavedKeys;

    [RelayCommand]
    private void Load()
    {
        var existing = credentialStore.Load();
        HasSavedKeys = credentialStore.HasCredentials;

        // The endpoint is configuration rather than a credential, so it is safe to display and
        // useful to see — it is the field that says which model is actually being called.
        OpenAiBaseUrl = existing?.OpenAiBaseUrl ?? "";

        StatusMessage = HasSavedKeys
            ? "Keys are saved. Enter new values only to replace them."
            : "No keys saved yet. Paper trading needs an Alpaca paper key, and the agent needs either "
              + "an Anthropic key or an OpenAI-compatible endpoint.";
    }

    [RelayCommand]
    private void Save()
    {
        var existing = credentialStore.Load();

        // A blank box means "leave this one alone", so one key can be rotated without re-entering
        // the others.
        var next = new TradingCredentials(
            AlpacaKeyId: Fallback(AlpacaKeyId, existing?.AlpacaKeyId),
            AlpacaSecret: Fallback(AlpacaSecret, existing?.AlpacaSecret),
            AnthropicApiKey: Fallback(AnthropicApiKey, existing?.AnthropicApiKey),
            AlpacaLive: false,

            // The endpoint is edited in place, so an emptied box means "remove it" rather than
            // "leave it alone" the way a blank secret does.
            OpenAiBaseUrl: OpenAiBaseUrl.Trim(),
            OpenAiApiKey: Fallback(OpenAiApiKey, existing?.OpenAiApiKey));

        if (next.AlpacaKeyId.Length == 0 && next.AnthropicApiKey.Length == 0 && next.OpenAiBaseUrl.Length == 0)
        {
            StatusMessage = "Nothing to save yet.";
            return;
        }

        credentialStore.Save(next);

        AlpacaKeyId = "";
        AlpacaSecret = "";
        AnthropicApiKey = "";
        OpenAiApiKey = "";
        HasSavedKeys = true;
        StatusMessage = $"Saved {DateTime.Now:h:mm tt}. Stored encrypted for this Windows account only.";
    }

    private static string Fallback(string entered, string? existing) =>
        string.IsNullOrWhiteSpace(entered) ? existing ?? "" : entered.Trim();
}
