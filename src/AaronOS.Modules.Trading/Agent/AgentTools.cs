using System.Text.Json.Nodes;

namespace AaronOS.Modules.Trading.Agent;

/// <summary>
/// The tools the agent is given, described once in JSON Schema and translated by each provider.
///
/// There are deliberately only two. Research on local tool-calling models is consistent that they
/// degrade quickly past about three tools, and a small surface also means fewer ways for a weak
/// model to produce something the guardrails then have to refuse.
/// </summary>
public static class AgentTools
{
    public static IReadOnlyList<AgentTool> All =>
    [
        new(
            "place_order",
            "Place a market order for a whole number of shares. Buying is limited by the position "
            + "and exposure caps; selling may only reduce a position you already hold.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["symbol"] = new JsonObject { ["type"] = "string", ["description"] = "Ticker, e.g. MSFT." },
                    ["side"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray { "buy", "sell" },
                    },
                    ["quantity"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 },
                    ["rationale"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "One sentence on why, stored with the order.",
                    },
                },
                ["required"] = new JsonArray { "symbol", "side", "quantity", "rationale" },
            }),
        new(
            "close_position",
            "Sell the entire holding in one symbol.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["symbol"] = new JsonObject { ["type"] = "string" },
                    ["rationale"] = new JsonObject { ["type"] = "string" },
                },
                ["required"] = new JsonArray { "symbol", "rationale" },
            }),
    ];
}
