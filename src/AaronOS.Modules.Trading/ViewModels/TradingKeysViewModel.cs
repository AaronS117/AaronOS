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

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasSavedKeys;

    [RelayCommand]
    private void Load()
    {
        HasSavedKeys = credentialStore.HasCredentials;
        StatusMessage = HasSavedKeys
            ? "Keys are saved. Enter new values only to replace them."
            : "No keys saved yet. Paper trading needs an Alpaca paper key; the agent needs an Anthropic key.";
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
            AlpacaLive: false);

        if (next.AlpacaKeyId.Length == 0 && next.AnthropicApiKey.Length == 0)
        {
            StatusMessage = "Nothing to save yet.";
            return;
        }

        credentialStore.Save(next);

        AlpacaKeyId = "";
        AlpacaSecret = "";
        AnthropicApiKey = "";
        HasSavedKeys = true;
        StatusMessage = $"Saved {DateTime.Now:h:mm tt}. Stored encrypted for this Windows account only.";
    }

    private static string Fallback(string entered, string? existing) =>
        string.IsNullOrWhiteSpace(entered) ? existing ?? "" : entered.Trim();
}
