using Zen.Managed;

namespace Zen.ZenEngine;

/// <summary>
/// Adapter over the official <c>GoRules.ZenEngine</c> package (NuGet 0.7.2) — a
/// second .NET binding of the same native Rust ZEN core as <c>GoRules.Zen</c>,
/// but with a materially different API. Where <c>GoRules.Zen.ZenExpression</c>
/// exposes only an async one-shot <c>Evaluate&lt;T&gt;(expr, context)</c> that
/// re-parses the expression and re-serializes the context every call, this binding
/// exposes:
/// <list type="bullet">
/// <item><c>ZenExpression.Compile(expr)</c> — a <b>reusable, precompiled</b>
///   expression (amortizes parse cost across evaluations).</item>
/// <item><c>Evaluate(JsonBuffer)</c> — <b>synchronous</b> (no async <c>Task</c> /
///   thread-pool dispatch floor), taking a <c>JsonBuffer</c> of raw JSON bytes as
///   context (no per-call object serialization).</item>
/// </list>
/// This is the "official engine with a better binding" path — it tests whether the
/// 20–34× gap the study attributes to <c>GoRules.Zen</c> is the engine or the
/// binding. We adapt it to the same (expression, JSON-context) -> ZenValue shape
/// used by the other engines.
/// </summary>
public sealed class ZenEngineExpression : IDisposable
{
    private readonly global::GoRules.ZenEngine.ZenExpression _expr;
    private global::GoRules.ZenEngine.JsonBuffer? _ctx;

    private ZenEngineExpression(string expression)
    {
        // Compile once, reuse across Evaluate calls (the precompiled fast path).
        _expr = global::GoRules.ZenEngine.ZenExpression.Compile(expression);
    }

    public static ZenEngineExpression Compile(string expression) => new(expression);

    /// <summary>Cache a context (parsed to a JsonBuffer once) for the pre-parsed
    /// "pure" path, so per-call cost is just the native eval + result parse.</summary>
    public ZenEngineExpression UseContext(string contextJson)
    {
        _ctx = new global::GoRules.ZenEngine.JsonBuffer(contextJson);
        return this;
    }

    /// <summary>Evaluate against the cached JsonBuffer context (pure-eval path:
    /// compiled expression + reused context buffer — the binding's fast path).</summary>
    public ZenValue Evaluate()
    {
        var result = _expr.Evaluate(_ctx!);
        return ZenJson.Parse(result.ToString());
    }

    /// <summary>Evaluate against a JSON context string, building a fresh JsonBuffer
    /// per call (json-eval path; isolates the per-call buffer-construction cost).</summary>
    public ZenValue Evaluate(string contextJson)
    {
        var result = _expr.Evaluate(new global::GoRules.ZenEngine.JsonBuffer(contextJson));
        return ZenJson.Parse(result.ToString());
    }

    /// <summary>Raw evaluation returning the result JSON string (no ZenValue
    /// conversion; isolates the engine's own end-to-end cost). Mirrors
    /// <see cref="Zen.Gorules.GorulesZenExpression.EvaluateRaw"/>.</summary>
    public string EvaluateRaw(string contextJson)
    {
        var result = _expr.Evaluate(new global::GoRules.ZenEngine.JsonBuffer(contextJson));
        return result.ToString();
    }

    public void Dispose() => _expr.Dispose();
}
