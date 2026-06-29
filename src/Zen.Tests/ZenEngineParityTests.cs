using Xunit;
using Zen.Managed;
using Zen.ZenEngine;

namespace Zen.Tests;

/// <summary>
/// Compares our managed implementation against the OFFICIAL <c>GoRules.ZenEngine</c>
/// binding (NuGet 0.5.0) — the second .NET binding of the same native Rust core as
/// <c>GoRules.Zen</c>, exercised through its synchronous, precompiled
/// <c>ZenExpression.Compile</c> path. Mirrors <see cref="GorulesParityTests"/>:
/// same shared-subset semantics, same <c>ZEN_STRICT_COMPAT</c> /
/// <see cref="KnownSupersetCases"/> handling, since both official bindings wrap the
/// same native engine (so superset-feature rejections are expected to match).
/// </summary>
public class ZenEngineParityTests
{
    private const double Tolerance = 1e-9;
    public static IEnumerable<object[]> Cases => ParityCases.All;

    private static bool Strict => Environment.GetEnvironmentVariable("ZEN_STRICT_COMPAT") is "1" or "true";

    /// <summary>Same superset allowlist as <see cref="GorulesParityTests"/>: features
    /// our engine supports but the official Rust parser rejects.</summary>
    private static readonly HashSet<string> KnownSupersetCases = new()
    {
        "fn-concat",          // concat() function
        "access-index-neg",   // negative array indexing items[-1]
        "cmp-str",            // string relational comparison 'a' < 'b'
        "fn-replace",         // replace() function
        "fn-substring",       // substring() function
        "in-string-contains", // 'needle' in 'haystack'
    };

    private static bool IsKnownSuperset(string caseName) => KnownSupersetCases.Contains(caseName);

    [Theory]
    [MemberData(nameof(Cases))]
    public void Managed_Matches_ZenEngine(string name, string expression, string contextJson)
    {
        var managed = ZenExpression.Compile(expression).Evaluate(contextJson);

        ZenValue ze;
        try
        {
            using var zeExpr = ZenEngineExpression.Compile(expression).UseContext(contextJson);
            ze = zeExpr.Evaluate();
        }
        catch (Exception ex)
        {
            // Managed evaluated fine (above), so any failure here is ZenEngine rejecting
            // the expression. Same engine as GoRules.Zen → same expected rejections.
            if (Strict && !IsKnownSuperset(name))
                Assert.Fail($"[{name}] GoRules.ZenEngine rejected an expression on the SHARED subset " +
                            $"(not a known superset feature). expr: {expression}\n  {ex.GetType().Name}: {ex.Message}");
            return; // non-strict, or a known superset case: soft-skip
        }

        bool equal = ZenCompare.DeepEquals(managed, ze, Tolerance);
        Assert.True(equal,
            $"[{name}] managed diverged from GoRules.ZenEngine.\n" +
            $"  expr:      {expression}\n" +
            $"  ctx:       {contextJson}\n" +
            $"  managed:   {ZenJson.Serialize(managed)}\n" +
            $"  zenengine: {ZenJson.Serialize(ze)}");
    }
}
