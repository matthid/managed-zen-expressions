namespace Zen.Managed;

/// <summary>
/// A deterministic resource budget for a single evaluation. Limits are expressed
/// as <b>counts</b> (node visits, allocations, bytes), never wall-clock time, so
/// behaviour is identical regardless of CPU load — a busy machine will not abort
/// an expression that a quiet one accepted.
///
/// <list type="bullet">
/// <item><see cref="MaxSteps"/> bounds total work (one charge per AST node visit).</item>
/// <item><see cref="MaxAllocations"/>/<see cref="MaxBytes"/> bound memory by counting the
///   high-level structural allocations the evaluator makes (arrays, objects, dynamic strings)
///   with a size estimate each. (Scalars — numbers/bools/null — are struct values and never charged.)</item>
/// <item><see cref="MaxSourceLength"/>/<see cref="MaxParseDepth"/> bound parsing so deeply
///   nested or oversized source cannot stack-overflow before evaluation.</item>
/// </list>
///
/// Eval-time limits are <b>opt-in</b>: <c>Evaluate(context)</c> enforces nothing (the
/// fast path used by benchmarks); pass a <see cref="ZenLimits"/> to <c>Evaluate</c> to
/// sandbox untrusted expressions. Parse-time guards are applied by default (they are cheap).
/// </summary>
public sealed class ZenLimits
{
    /// <summary>Maximum AST node visits during one evaluation.</summary>
    public long MaxSteps { get; set; } = 1_000_000;

    /// <summary>Maximum number of structural allocations (arrays/objects/strings).</summary>
    public long MaxAllocations { get; set; } = 1_000_000;

    /// <summary>Estimated bytes the evaluation may allocate before aborting.</summary>
    public long MaxBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>Maximum source text length accepted by <see cref="ZenExpression.Compile"/>.</summary>
    public int MaxSourceLength { get; set; } = 1_000_000;

    /// <summary>Maximum parser recursion depth (guards against deeply nested source).</summary>
    public int MaxParseDepth { get; set; } = 1_000;

    /// <summary>Sensible defaults for sandboxing untrusted expressions.</summary>
    public static ZenLimits Default { get; } = new ZenLimits();

    /// <summary>Stricter budget for hostile input.</summary>
    public static ZenLimits Strict { get; } = new ZenLimits
    {
        MaxSteps = 10_000,
        MaxAllocations = 10_000,
        MaxBytes = 4L * 1024 * 1024,
        MaxSourceLength = 64_000,
        MaxParseDepth = 200,
    };
}

/// <summary>Thrown when an evaluation or parse exceeds its <see cref="ZenLimits"/>.</summary>
public class ZenLimitException : ZenException
{
    public ZenLimitException(string message) : base(message) { }
}
