namespace AaronOS.Modules.Trading.Agent;

/// <summary>
/// Picks the provider named in the config.
///
/// An unrecognised or empty name falls back to the first registered provider rather than throwing.
/// The name is stored as free text so a provider can be added without a schema change, and the cost
/// of that is a value that might not match anything — which should degrade to a working default, not
/// stop the module loading.
/// </summary>
public class AgentProviderRegistry(IEnumerable<IAgentProvider> providers)
{
    private readonly List<IAgentProvider> _providers = providers.ToList();

    public IReadOnlyList<string> Names => _providers.Select(p => p.Name).ToList();

    public IAgentProvider Resolve(string? name) =>
        _providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? _providers[0];

    /// <summary>True when at least one provider has what it needs to run.</summary>
    public bool AnyConfigured => _providers.Any(p => p.IsConfigured);
}
