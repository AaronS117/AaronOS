using System.ComponentModel;
using System.IO;
using System.Text.Json;
using AaronOS.Core;
using AaronOS.Modules.Finance.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using System.Windows.Controls;

namespace AaronOS.Modules.Finance.Views;

/// <summary>
/// The Finance module's contribution to the app's Settings page (see IAppModule.SettingsContentType):
/// linking a bank is configuration you do once, not a day-to-day workflow, so it belongs in Settings
/// rather than in the module's own sub-navigation.
/// </summary>
public sealed partial class LinkAccountSection : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string VirtualHost = "aaronos.plaidlink.local";

    public LinkAccountViewModel ViewModel { get; }

    public LinkAccountSection()
    {
        ViewModel = AppServices.Provider.GetRequiredService<LinkAccountViewModel>();
        DataContext = ViewModel;
        InitializeComponent();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private async void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.LinkToken) && ViewModel.LinkToken is not null)
        {
            await LoadPlaidLinkAsync(ViewModel.LinkToken);
        }
    }

    private async Task LoadPlaidLinkAsync(string linkToken)
    {
        await WebView.EnsureCoreWebView2Async();
        WebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
        WebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        WebView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
        WebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

        // Plaid Link's own iframe uses postMessage internally and expects a real origin —
        // NavigateToString gives the page a null/opaque origin, which breaks that. Serving the
        // page from a mapped virtual host (a real https:// origin backed by a local folder) is
        // WebView2's documented fix for exactly this class of problem.
        var folder = Path.Combine(Path.GetTempPath(), "AaronOS.PlaidLink");
        Directory.CreateDirectory(folder);
        var htmlPath = Path.Combine(folder, "link.html");
        await File.WriteAllTextAsync(htmlPath, BuildPlaidLinkHtml(linkToken));

        WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHost, folder, CoreWebView2HostResourceAccessKind.Allow);
        WebView.CoreWebView2.Navigate($"https://{VirtualHost}/link.html");
    }

    private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var json = e.TryGetWebMessageAsString();
        if (json is null)
        {
            return;
        }

        var envelope = JsonSerializer.Deserialize<DiagnosticEnvelope>(json, JsonOptions);
        if (envelope?.Kind == "error")
        {
            ViewModel.StatusMessage = $"Link page error: {envelope.Message}";
            return;
        }

        var payload = JsonSerializer.Deserialize<LinkSuccessPayload>(json, JsonOptions);
        if (payload is not null)
        {
            await ViewModel.CompleteLinkAsync(payload.PublicToken, payload.InstitutionId, payload.InstitutionName);
        }
    }

    // Without a registered redirect_uri, Plaid's Link SDK opens OAuth institutions' real login
    // pages via window.open() rather than a full-page redirect (confirmed against Plaid's OAuth
    // docs). WebView2 doesn't create a window for that automatically — the host must handle
    // NewWindowRequested itself, or the popup silently fails to appear. This must be a genuinely
    // separate window (not the same WebView), since the popup talks back to this page via
    // window.opener once OAuth completes, and that window's CoreWebView2 must be initialised
    // after Show() or EnsureCoreWebView2Async never returns.
    private async void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var popup = new OAuthPopupWindow { Owner = System.Windows.Window.GetWindow(this) };
            await popup.InitializeAsync();
            e.NewWindow = popup.WebView.CoreWebView2;
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Could not open the bank login window: {ex.Message}";
        }
        finally
        {
            deferral.Complete();
        }
    }

    private record LinkSuccessPayload(string PublicToken, string InstitutionId, string InstitutionName);
    private record DiagnosticEnvelope(string? Kind, string? Message);

    // ponytail: Plaid Link has no native WPF host — this is the documented pattern (a WebView2
    // loaded with Plaid's own link-initialize.js), not a workaround we chose over something simpler.
    // The window.onerror / try-catch reporting stays: a failure inside Plaid's own script is
    // otherwise completely silent from C#'s side, which cost a long time to diagnose once already.
    private static string BuildPlaidLinkHtml(string linkToken) => $$"""
        <!DOCTYPE html>
        <html>
        <head><script src="https://cdn.plaid.com/link/v2/stable/link-initialize.js"></script></head>
        <body>
        <script>
          window.onerror = function(message, source, lineno, colno, error) {
            window.chrome.webview.postMessage(JSON.stringify({ kind: "error", message: String(message) + " @ " + source + ":" + lineno }));
          };
          try {
            var handler = Plaid.create({
              token: "{{linkToken}}",
              onSuccess: function(public_token, metadata) {
                window.chrome.webview.postMessage(JSON.stringify({
                  publicToken: public_token,
                  institutionId: metadata.institution.institution_id,
                  institutionName: metadata.institution.name
                }));
              },
              onExit: function(err, metadata) {}
            });
            handler.open();
          } catch (e) {
            window.chrome.webview.postMessage(JSON.stringify({ kind: "error", message: String(e) }));
          }
        </script>
        </body>
        </html>
        """;
}
