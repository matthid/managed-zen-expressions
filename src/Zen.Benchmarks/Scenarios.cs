using System.Text;
using System.Text.Json;
using Zen.Gorules;
using Zen.Interop;
using Zen.Managed;

namespace Zen.Benchmarks;

/// <summary>
/// The benchmark matrix: simple/complex logic crossed with few/many input
/// parameters. Each scenario carries the expression, its JSON context, and
/// lazily-built run artifacts (compiled expressions, pre-parsed contexts).
/// </summary>
public sealed class Scenario
{
    public string Name { get; init; } = "";
    public string Expression { get; init; } = "";
    public string ContextJson { get; init; } = "{}";
    public string Group { get; init; } = "";   // "simple" / "complex"

    public ZenExpression? ManagedExpr { get; set; }
    public ZenValue ManagedCtx { get; set; }
    public NativeZenExpression? NativeExpr { get; set; }
    public NativeContext? NativeCtx { get; set; }
    public GorulesZenExpression? GorulesExpr { get; set; }
    public JsonDocument? GorulesDoc { get; set; }
    public JsonElement GorulesCtx { get; set; }

    public override string ToString() => Name;
}

public static class Scenarios
{
    public static readonly Scenario[] All = Build();

    public static IEnumerable<string> AllNames => All.Select(s => s.Name);
    public static Scenario ByName(string name) => All.First(s => s.Name == name);

    private static Scenario[] Build()
    {
        var list = new List<Scenario>
        {
            new() {
                Name = "simple-few",
                Group = "simple",
                Expression = "a + b * c - d",
                ContextJson = "{\"a\":1.5,\"b\":2,\"c\":3,\"d\":0.5}",
            },
            new() {
                Name = "complex-few",
                Group = "complex",
                Expression = "(price * quantity * (1 - discount) + shipping) * (1 + tax) > budget ? 'over' : 'ok'",
                ContextJson = "{\"price\":19.99,\"quantity\":7,\"discount\":0.1,\"shipping\":5.0,\"tax\":0.08,\"budget\":200}",
            },
        };

        // --- many-parameter scenarios (generated) ---
        const int n = 50;
        var sbSum = new StringBuilder();
        var sbCtx = new StringBuilder("{");
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sbSum.Append('+');
            sbSum.Append("v").Append(i);
            if (i > 0) sbCtx.Append(',');
            sbCtx.Append("\"v").Append(i).Append("\":").Append(i + 1);
        }
        sbCtx.Append('}');
        list.Add(new()
        {
            Name = "simple-many",
            Group = "simple",
            Expression = sbSum.ToString(),
            ContextJson = sbCtx.ToString(),
        });

        const int f = 20;
        var sbWide = new StringBuilder("round((");
        var sbObj = new StringBuilder("{\"data\":{");
        for (int i = 0; i < f; i++)
        {
            if (i > 0) sbWide.Append('+');
            sbWide.Append("data.f").Append(i).Append(" * ").Append(i + 1);
            if (i > 0) sbObj.Append(',');
            sbObj.Append("\"f").Append(i).Append("\":").Append((i + 1) * 0.5);
        }
        sbWide.Append(") / ").Append(f).Append(", 2) > 50 ? 'high' : 'low'");
        sbObj.Append("}}");
        list.Add(new()
        {
            Name = "complex-many",
            Group = "complex",
            Expression = sbWide.ToString(),
            ContextJson = sbObj.ToString(),
        });

        // --- allocating scenarios: expressions that RESHAPE data (build arrays,
        // objects or strings on the hot path). These are the honest counterpoint
        // to the scalar scenarios above: managed pure-eval is NOT zero-alloc here. ---
        var nums = new StringBuilder();
        for (int i = 0; i < 20; i++) { if (i > 0) nums.Append(','); nums.Append(i + 1); }
        list.Add(new()
        {
            Name = "alloc-array",
            Group = "allocating",
            Expression = "map(numbers, # * # + offset)",
            ContextJson = "{\"numbers\":[" + nums + "],\"offset\":1}",
        });

        list.Add(new()
        {
            Name = "alloc-object",
            Group = "allocating",
            Expression = "{ total: sum(prices), count: len(prices), avg: round(sum(prices) / len(prices), 2), max: max(prices) }",
            ContextJson = "{\"prices\":[10,20,30,40,50]}",
        });

        list.Add(new()
        {
            Name = "alloc-string",
            Group = "allocating",
            Expression = "prefix + '-' + string(id) + '-' + upper(status)",
            ContextJson = "{\"prefix\":\"ORD\",\"id\":4815,\"status\":\"pending\"}",
        });

        return list.ToArray();
    }
}
