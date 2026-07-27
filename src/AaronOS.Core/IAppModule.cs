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
    void RegisterServices(IServiceCollection services);
}
