using AaronOS.Modules.Nutrition.Usda;

namespace AaronOS.Modules.Nutrition.Tests;

public class ApiKeyProtectorTests
{
    [Fact]
    public void RoundTrips_ThroughDpapi()
    {
        const string apiKey = "DEMO_KEY-abc123";

        var encrypted = ApiKeyProtector.Protect(apiKey);
        var decrypted = ApiKeyProtector.Unprotect(encrypted);

        Assert.Equal(apiKey, decrypted);
        Assert.NotEqual(apiKey, System.Text.Encoding.UTF8.GetString(encrypted));
    }
}
