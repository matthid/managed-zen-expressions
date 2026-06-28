using Zen.Managed.Ast;

namespace Zen.Managed;

/// <summary>
/// Public entry point for the managed Zen expression engine. A compiled
/// expression holds its AST and is safe to evaluate concurrently from multiple
/// threads (each evaluation carries its own state).
/// </summary>
public sealed class ZenExpression
{
    private readonly Node _root;

    [ThreadStatic] private static Evaluator? t_evaluator;
    private static Evaluator ThreadEvaluator => t_evaluator ??= new Evaluator();

    private ZenExpression(Node root) { _root = root; }

    /// <summary>Compile (lex + parse) a Zen expression into a reusable AST.
    /// Applies default parse guards (source length + recursion depth).</summary>
    public static ZenExpression Compile(string source)
        => Compile(source, ZenLimits.Default);

    /// <summary>Compile with explicit parse guards.</summary>
    public static ZenExpression Compile(string source, ZenLimits limits)
    {
        if (source.Length > limits.MaxSourceLength)
            throw new ZenLimitException($"source length {source.Length} exceeds MaxSourceLength {limits.MaxSourceLength}");
        return new(Parser.Parse(source, limits.MaxParseDepth));
    }

    /// <summary>Evaluate against an already-deserialised context value. Unlimited (fast path).</summary>
    public ZenValue Evaluate(ZenValue context) => ThreadEvaluator.Evaluate(_root, context);

    /// <summary>Evaluate with a deterministic resource budget (steps / allocations / bytes).</summary>
    public ZenValue Evaluate(ZenValue context, ZenLimits limits) => ThreadEvaluator.Evaluate(_root, context, limits);

    /// <summary>Evaluate against a JSON context string (parse + eval), unlimited.</summary>
    public ZenValue Evaluate(string contextJson) => Evaluate(ZenJson.Parse(contextJson));

    /// <summary>Evaluate against a JSON context string with a resource budget.</summary>
    public ZenValue Evaluate(string contextJson, ZenLimits limits) => Evaluate(ZenJson.Parse(contextJson), limits);
}
