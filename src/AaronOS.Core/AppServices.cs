namespace AaronOS.Core;

/// <summary>
/// Set once by AaronOS.App's composition root at startup. Pages use this to resolve their
/// ViewModel because Frame.Navigate requires a parameterless Page constructor, so DI can't be
/// injected the normal way — and a module's Pages can't reference AaronOS.App directly (that
/// would be a circular project reference).
/// </summary>
public static class AppServices
{
    public static IServiceProvider Provider { get; set; } = null!;
}
