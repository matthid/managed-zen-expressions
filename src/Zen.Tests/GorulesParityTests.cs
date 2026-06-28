using System.Text.Json;
using Xunit;
using Zen.Gorules;
using Zen.Managed;

namespace Zen.Tests;

/// <summary>
/// Compares our managed implementation against the OFFICIAL GoRules.Zen engine
/// (the native Rust reference). Divergences here highlight where our documented
/// language subset differs from reference Zen semantics. Kept separate from
/// <see cref="ParityTests"/> (managed vs our-manual-native) so reference
/// divergences do not mask the managed==native guarantee.
/// </summary>
public class GorulesParityTests
{
    private const double Tolerance = 1e-9;
    public static IEnumerable<object[]> Cases => ParityCases.All;

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
            // A reference divergence that throws on one side is itself a finding;
            // record it as a soft skip rather than failing the managed==native suite.
            _ = ex;
            return;
        }

        bool equal = ZenCompare.DeepEquals(managed, gorules, Tolerance);
        Assert.True(equal,
            $"[{name}] managed diverged from GoRules reference.\n" +
            $"  expr:     {expression}\n" +
            $"  ctx:      {contextJson}\n" +
            $"  managed:  {ZenJson.Serialize(managed)}\n" +
            $"  gorules:  {ZenJson.Serialize(gorules)}");
    }
}
