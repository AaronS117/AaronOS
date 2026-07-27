using AaronOS.Modules.Finance.Plaid;

namespace AaronOS.Modules.Finance.Tests;

public class AccessTokenProtectorTests
{
    [Fact]
    public void RoundTrips_ThroughDpapi()
    {
        const string token = "access-sandbox-abc123";

        var encrypted = AccessTokenProtector.Protect(token);
        var decrypted = AccessTokenProtector.Unprotect(encrypted);

        Assert.Equal(token, decrypted);
        Assert.NotEqual(token, System.Text.Encoding.UTF8.GetString(encrypted));
    }
}
