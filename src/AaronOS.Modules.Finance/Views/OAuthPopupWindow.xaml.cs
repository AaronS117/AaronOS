using System.Windows;

namespace AaronOS.Modules.Finance.Views;

/// <summary>
/// A genuinely separate window for Plaid's OAuth pop-up (used when no redirect_uri is
/// configured — Plaid opens the bank's real login via window.open() rather than a full-page
/// redirect). Must stay a distinct window/CoreWebView2 instance, not reuse the Link page's own
/// WebView, because the popup communicates back to the original Link page via window.opener
/// once OAuth completes; replacing the original page's content would destroy that relationship.
/// </summary>
public sealed partial class OAuthPopupWindow : Window
{
    public OAuthPopupWindow()
    {
        InitializeComponent();
    }

    public async Task InitializeAsync()
    {
        // Show() first — EnsureCoreWebView2Async() needs a real window handle to attach to,
        // and calling it before the window is shown left it hanging indefinitely (confirmed by
        // tracing: it never returned, even though the main page's WebView had initialized fine
        // before ever being shown — the difference is this window doesn't exist as a native HWND
        // until Show()/first layout happens).
        Show();
        await WebView.EnsureCoreWebView2Async();
        WebView.CoreWebView2.WindowCloseRequested += (_, _) => Close();
    }
}
