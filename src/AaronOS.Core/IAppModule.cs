using Microsoft.Extensions.DependencyInjection;

namespace AaronOS.Core;

/// <summary>
/// Contract every feature module implements so the app shell can list it in navigation,
/// wire up its services, and let it own its slice of the shared database schema.
/// See docs/MODULE_GUIDELINES.md for how to add a new module.
/// </summary>
public interface IAppModule
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>The name of a Wpf.Ui.Controls.SymbolRegular enum member (e.g. "Person24"),
    /// kept as a plain string so this contract has no compile-time dependency on the UI
    /// framework's icon type. The shell parses it via Enum.Parse&lt;SymbolRegular&gt;.</summary>
    string IconGlyph { get; }

    Type HomePageType { get; }

    /// <summary>
    /// Optional: a <c>UserControl</c> type this module contributes to the app's Settings page, for
    /// configuration that belongs to the module rather than to a day-to-day workflow page (linking a
    /// bank account, for instance). Return null — the default — when a module has no settings.
    ///
    /// A default implementation keeps this non-breaking for existing modules, and it exists as a
    /// contract member rather than the Settings page referencing a module's page directly, because
    /// the shell must not reach into a module's internal pages (see docs/MODULE_GUIDELINES.md).
    /// A UserControl rather than a Page because the Settings page composes several of these inline.
    /// </summary>
    Type? SettingsContentType => null;

    void RegisterServices(IServiceCollection services);
}
