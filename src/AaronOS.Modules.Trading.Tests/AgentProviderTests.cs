using AaronOS.Modules.Trading.Agent;

namespace AaronOS.Modules.Trading.Tests;

/// <summary>
/// Provider selection and argument reading. Both exist to make weak or misconfigured models a
/// recoverable nuisance rather than a crash, so the tests are mostly about the bad inputs.
/// </summary>
public class AgentProviderTests
{
    private sealed class FakeProvider(string name, bool configured) : IAgentProvider
    {
        public string Name => name;
        public bool IsConfigured => configured;

        public IAgentConversation Start(
            string model, string systemPrompt, string firstUserMessage, IReadOnlyList<AgentTool> tools) =>
            throw new NotSupportedException();
    }

    private static AgentProviderRegistry Registry() =>
        new([new FakeProvider("anthropic", true), new FakeProvider("openai-compatible", false)]);

    [Fact]
    public void ANamedProviderIsResolved()
    {
        Assert.Equal("openai-compatible", Registry().Resolve("openai-compatible").Name);
    }

    [Fact]
    public void ProviderNamesIgnoreCase()
    {
        Assert.Equal("anthropic", Registry().Resolve("Anthropic").Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some-provider-that-was-removed")]
    public void AnUnknownProviderFallsBackToTheFirstRatherThanThrowing(string? name)
    {
        // The name is free text in the database so a module can add a provider without a schema
        // change. The cost is a value that may not match, which must not stop the page loading.
        Assert.Equal("anthropic", Registry().Resolve(name).Name);
    }

    [Fact]
    public void AnyConfiguredIsTrueWhenAtLeastOneProviderIsReady()
    {
        Assert.True(Registry().AnyConfigured);
        Assert.False(new AgentProviderRegistry([new FakeProvider("x", false)]).AnyConfigured);
    }

    [Fact]
    public void ExactlyTwoToolsAreOffered()
    {
        // Local tool-calling models degrade quickly past about three tools, so the small surface is
        // a deliberate constraint rather than an accident of scope.
        Assert.Equal(2, AgentTools.All.Count);
        Assert.All(AgentTools.All, tool =>
        {
            Assert.NotEmpty(tool.Name);
            Assert.NotEmpty(tool.Description);
            Assert.NotNull(tool.ParametersSchema["properties"]);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"symbol\": \"AAPL\"")]
    [InlineData("not json at all")]
    public void MalformedArgumentsParseToNullInsteadOfThrowing(string? raw)
    {
        // A model writing the arguments string itself will sometimes write it wrong. Refusing the
        // call costs a tool result; throwing would cost the whole cycle.
        Assert.Null(ToolArguments.Parse(raw));
    }

    [Fact]
    public void WellFormedArgumentsParse()
    {
        var parsed = ToolArguments.Parse("""{"symbol":"MSFT","quantity":4}""");

        Assert.NotNull(parsed);
        Assert.Equal("MSFT", ToolArguments.StringOf(parsed, "symbol"));
        Assert.Equal(4, ToolArguments.IntOf(parsed, "quantity"));
    }

    [Fact]
    public void AMissingPropertyReadsAsAbsent()
    {
        var parsed = ToolArguments.Parse("""{"symbol":"MSFT"}""");

        Assert.Null(ToolArguments.StringOf(parsed, "rationale"));
        Assert.Null(ToolArguments.IntOf(parsed, "quantity"));
    }

    [Fact]
    public void AQuantitySentAsAStringIsAccepted()
    {
        // Models asked for an integer regularly send "10". Refusing that would be pedantry, and the
        // value still has to clear every guardrail afterwards.
        var parsed = ToolArguments.Parse("""{"quantity":"10"}""");

        Assert.Equal(10, ToolArguments.IntOf(parsed, "quantity"));
    }

    [Fact]
    public void AQuantityThatIsNotANumberAtAllReadsAsAbsent()
    {
        var parsed = ToolArguments.Parse("""{"quantity":"lots"}""");

        Assert.Null(ToolArguments.IntOf(parsed, "quantity"));
    }

    [Fact]
    public void ANumericSymbolReadsAsAbsentRatherThanBeingCoerced()
    {
        // Guards against a stringified number turning into a ticker like "12".
        var parsed = ToolArguments.Parse("""{"symbol":12}""");

        Assert.Null(ToolArguments.StringOf(parsed, "symbol"));
    }
}
