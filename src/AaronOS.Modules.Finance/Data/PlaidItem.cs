namespace AaronOS.Modules.Finance.Data;

/// <summary>One row per linked bank connection.</summary>
public class PlaidItem
{
    public int Id { get; set; }
    public string ItemId { get; set; } = "";
    public string InstitutionId { get; set; } = "";
    public string InstitutionName { get; set; } = "";

    /// <summary>DPAPI-protected (current-user scope) access token. Never plaintext at rest.</summary>
    public byte[] AccessTokenEncrypted { get; set; } = [];

    /// <summary>Null until the first transactions/sync call.</summary>
    public string? Cursor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
