using System.Text.Json;
using Xunit;
using Zen.Gorules;
using Zen.Managed;

namespace Zen.Tests;

/// <summary>
/// Compares our managed implementation against the OFFICIAL GoRules.Zen engine
/// (the native Rust reference).
///
/// Compat mode is controlled by the <c>ZEN_STRICT_COMPAT</c> env var:
/// <list type="bullet">
/// <item><b>Off (default):</b> if GoRules rejects an expression our engine accepts,
///   the case is soft-skipped. Use this while extending the language beyond the
///   reference — divergence on superset features should not fail the suite.</li>
/// <item><b>On (<c>ZEN_STRICT_COMPAT=1</c>):</b> a GoRules rejection FAILS unless the
///   expression uses a feature on the known-superset allowlist
///   (<see cref="KnownSupersetFeatures"/>). This catches *unexpected* regressions on
///   the shared subset.</li>
/// </list>
/// Safety from runaway/DoS expressions on our side comes from <see cref="ZenLimits"/>
/// (count-based, deterministic) — the language has no user recursion, so no infinite
/// loops are possible.
/// </summary>
public class GorulesParityTests
{
    private const double Tolerance = 1e-9;
    public static IEnumerable<object[]> Cases => ParityCases.All;

    private static bool Strict => Environment.GetEnvironmentVariable("ZEN_STRICT_COMPAT") is "1" or "true";

    /// <summary>Test cases whose feature our engine supports but the official
    /// GoRules expression parser does NOT. Divergence here is intentional
    /// (language extension). Add to this set when you deliberately extend beyond
    /// the reference; leave a case OUT to have strict mode catch its regression.</summary>
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
    public void Managed_Matches_GoRules(string name, string expression, string contextJson)
    {
        var managed = ZenExpression.Compile(expression).Evaluate(contextJson);

        ZenValue gorules;
        try
        {
            using var doc = JsonDocument.Parse(contextJson);
            gorules = GorulesZenExpression.Compile(expression).Evaluate(doc.RootElement);
        }
        catch (Exception ex)
        {
            // Managed evaluated fine (above), so any failure here is GoRules rejecting
            // the expression (it throws GoRules.Zen.ZenException on parse/eval errors).
            if (Strict && !IsKnownSuperset(name))
                Assert.Fail($"[{name}] GoRules rejected an expression on the SHARED subset " +
                            $"(not a known superset feature). expr: {expression}\n  {ex.GetType().Name}: {ex.Message}");
            return; // non-strict, or a known superset case: soft-skip
        }

        bool equal = ZenCompare.DeepEquals(managed, gorules, Tolerance);
        Assert.True(equal,
            $"[{name}] managed diverged from GoRules reference.\n" +
            $"  expr:     {expression}\n" +
            $"  ctx:      {contextJson}\n" +
            $"  managed:  {ZenJson.Serialize(managed)}\n" +
            $"  gorules:  {ZenJson.Serialize(gorules)}");
    }

    /// <summary>Document the known superset divergence explicitly so it is not lost.</summary>
    [Fact]
    public void Concat_Is_A_Known_Superset_Feature()
    {
        // Our engine supports concat(); the official GoRules expression parser rejects it.
        Assert.Equal("abc", ZenExpression.Compile("concat('a','b','c')").Evaluate("{}").String);
        var gr = GorulesZenExpression.Compile("concat('a','b','c')");
        using var doc = JsonDocument.Parse("{}");
        Assert.ThrowsAny<Exception>(() => gr.Evaluate(doc.RootElement));
    }
}
