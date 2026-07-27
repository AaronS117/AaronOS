using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

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
    IconElement Icon { get; }
    Type HomePageType { get; }
    void RegisterServices(IServiceCollection services);
}
