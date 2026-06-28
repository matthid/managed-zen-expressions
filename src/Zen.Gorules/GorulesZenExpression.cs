using System.Text.Json;
using Zen.Managed;

namespace Zen.Gorules;

/// <summary>
/// Adapter over the official <c>GoRules.Zen</c> package (the native Rust ZEN
/// engine delivered to .NET via UniFFI). This is the real-world "unmanaged
/// engine via interop" path, and the third engine in the comparison.
///
/// The official API is <c>ZenExpression.Evaluate&lt;T&gt;(expression, context)</c>;
/// it serialises the context to JSON, evaluates natively, and deserialises the
/// result. We adapt it to the same (expression, JSON-context) -> ZenValue shape
/// used by the managed and manual-native engines.
/// </summary>
public sealed class GorulesZenExpression
{
    private readonly string _expression;

    private GorulesZenExpression(string expression) { _expression = expression; }

    public static GorulesZenExpression Compile(string expression) => new(expression);

    /// <summary>Evaluate against a JSON context string. The context is parsed to a
    /// JsonElement once per call (matching the other engines' JSON-path cost).</summary>
    public ZenValue Evaluate(string contextJson)
    {
        using var doc = JsonDocument.Parse(contextJson);
        return Evaluate(doc.RootElement);
    }

    /// <summary>Evaluate against a pre-parsed JsonElement context (pure-eval path).</summary>
    public ZenValue Evaluate(JsonElement context)
    {
        JsonElement result = global::GoRules.Zen.ZenExpression.Evaluate<JsonElement>(_expression, context)
            .GetAwaiter().GetResult();
        return ZenJson.FromElement(result);
    }

    /// <summary>Raw evaluation returning the result JSON string (no ZenValue conversion;
    /// used by benchmarks to isolate the official engine's own end-to-end cost).</summary>
    public string EvaluateRaw(string contextJson)
    {
        using var doc = JsonDocument.Parse(contextJson);
        var result = global::GoRules.Zen.ZenExpression.Evaluate<JsonElement>(_expression, doc.RootElement)
            .GetAwaiter().GetResult();
        return result.GetRawText();
    }
}
