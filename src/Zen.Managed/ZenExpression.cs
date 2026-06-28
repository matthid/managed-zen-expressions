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

    /// <summary>Compile (lex + parse) a Zen expression into a reusable AST.</summary>
    public static ZenExpression Compile(string source)
        => new(Parser.Parse(source));

    /// <summary>Evaluate against an already-deserialised context value.</summary>
    public ZenValue Evaluate(ZenValue context) => ThreadEvaluator.Evaluate(_root, context);

    /// <summary>Evaluate against a JSON context string (parse + eval).</summary>
    public ZenValue Evaluate(string contextJson) => Evaluate(ZenJson.Parse(contextJson));
}
