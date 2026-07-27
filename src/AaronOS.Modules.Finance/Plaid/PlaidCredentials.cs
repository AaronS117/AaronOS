namespace AaronOS.Modules.Finance.Plaid;

/// <summary>App-level Plaid API credentials. Persisted only via PlaidCredentialStore, which
/// DPAPI-encrypts this at rest — never construct this from a hardcoded literal.</summary>
public class PlaidCredentials
{
    public string ClientId { get; set; } = "";
    public string? SandboxSecret { get; set; }
    public string? ProductionSecret { get; set; }
    public PlaidEnvironment Environment { get; set; } = PlaidEnvironment.Sandbox;

    public string? ActiveSecret => Environment == PlaidEnvironment.Sandbox ? SandboxSecret : ProductionSecret;
}
