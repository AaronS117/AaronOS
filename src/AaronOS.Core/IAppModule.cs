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

    /// <summary>
    /// Optional: work a module needs done once the database is ready and before the window appears.
    ///
    /// This exists so a module can run unattended without any of its pages being opened. Background
    /// work started from a page only runs once someone navigates there, which is fine for refreshing
    /// a view and useless for anything that should simply be running — a trading schedule, an expiry
    /// check, a sync. The shell awaits each module in turn, so a module that needs to be slow should
    /// start its own timer and return rather than blocking startup.
    ///
    /// A default no-op keeps this non-breaking for modules that have nothing to do here.
    /// </summary>
    Task OnStartupAsync(IServiceProvider services) => Task.CompletedTask;
}
