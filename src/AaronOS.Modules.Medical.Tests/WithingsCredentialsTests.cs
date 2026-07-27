using AaronOS.Modules.Medical.ViewModels;
using AaronOS.Modules.Medical.Withings;

namespace AaronOS.Modules.Medical.Tests;

public class WithingsCredentialsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static WithingsTokenBody Token(string access = "new-access", string? refresh = "new-refresh") =>
        new(access, refresh, ExpiresIn: 10800, UserId: "12345", Scope: "user.metrics");

    [Fact]
    public void ARotatedRefreshTokenReplacesTheOldOne()
    {
        // Withings invalidates the previous refresh token immediately. Dropping the new one breaks the
        // *next* sync, which is why this is worth a test rather than a careful reading.
        var credentials = new WithingsCredentials { RefreshToken = "old-refresh" };

        credentials.ApplyToken(Token(refresh: "rotated"), Now);

        Assert.Equal("rotated", credentials.RefreshToken);
    }

    [Fact]
    public void AResponseWithoutARefreshTokenKeepsTheExistingOne()
    {
        var credentials = new WithingsCredentials { RefreshToken = "still-good" };

        credentials.ApplyToken(Token(refresh: null), Now);

        Assert.Equal("still-good", credentials.RefreshToken);
    }

    [Fact]
    public void ExpiryIsCalculatedFromTheResponseLifetime()
    {
        var credentials = new WithingsCredentials();

        credentials.ApplyToken(Token(), Now);

        Assert.Equal(Now.AddSeconds(10800), credentials.AccessTokenExpiresAt);
        Assert.True(credentials.HasFreshAccessToken(Now));
    }

    [Fact]
    public void ATokenIsStaleBeforeItActuallyExpires()
    {
        // Treated as stale a minute early so it cannot expire between the check and the request.
        var credentials = new WithingsCredentials();
        credentials.ApplyToken(Token(), Now);

        Assert.False(credentials.HasFreshAccessToken(Now.AddSeconds(10800 - 30)));
    }

    [Fact]
    public void MissingTokensReadAsNotFreshAndNotAuthorized()
    {
        var credentials = new WithingsCredentials();

        Assert.False(credentials.HasFreshAccessToken(Now));
        Assert.False(credentials.IsAuthorized);
        Assert.False(credentials.HasAppCredentials);
    }

    [Fact]
    public void AppCredentialsNeedBothHalves()
    {
        Assert.False(new WithingsCredentials { ClientId = "id" }.HasAppCredentials);
        Assert.False(new WithingsCredentials { ClientSecret = "secret" }.HasAppCredentials);
        Assert.True(new WithingsCredentials { ClientId = "id", ClientSecret = "secret" }.HasAppCredentials);
    }

    [Theory]
    [InlineData("abc123", "abc123")]
    [InlineData("  abc123  ", "abc123")]
    [InlineData("https://wbsapi.withings.net/v2/oauth2?code=abc123&state=xyz", "abc123")]
    [InlineData("https://wbsapi.withings.net/v2/oauth2?state=xyz&code=abc123", "abc123")]
    [InlineData("https://wbsapi.withings.net/v2/oauth2?code=abc123", "abc123")]
    [InlineData("https://wbsapi.withings.net/v2/oauth2?code=abc123#frag", "abc123")]
    public void TheCodeIsFoundWhetherPastedBareOrAsTheWholeRedirectUrl(string pasted, string expected)
    {
        // Pasting the whole address bar is what people actually do.
        Assert.Equal(expected, SleepViewModel.ExtractCode(pasted));
    }
}
