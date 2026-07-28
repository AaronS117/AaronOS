using System.Text.Json;
using System.Text.Json.Nodes;

namespace AaronOS.Modules.Trading.Agent;

/// <summary>
/// Reads the arguments a model supplied for a tool call.
///
/// In the OpenAI-compatible format these arrive as a JSON string the model wrote itself, so
/// malformed JSON is a normal outcome rather than a fault — smaller local models produce it
/// regularly. Every accessor here answers "absent" instead of throwing, which lets a bad call be
/// refused and explained back to the model rather than ending the cycle over a typo.
/// </summary>
public static class ToolArguments
{
    public static JsonNode? Parse(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(arguments);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? StringOf(JsonNode? arguments, string property)
    {
        try
        {
            return (string?)arguments?[property];
        }
        catch (InvalidOperationException)
        {
            // The property exists but is not a string — treated the same as absent.
            return null;
        }
    }

    /// <summary>
    /// A whole-number argument, accepting the string form too. Models asked for an integer will
    /// sometimes send "10", and refusing that would be pedantry rather than safety — the quantity
    /// still has to clear every guardrail afterwards.
    /// </summary>
    public static int? IntOf(JsonNode? arguments, string property)
    {
        var value = arguments?[property];
        if (value is null)
        {
            return null;
        }

        try
        {
            return (int)value;
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or OverflowException)
        {
            var text = StringOf(arguments, property);
            return int.TryParse(text, out var parsed) ? parsed : null;
        }
    }
}
