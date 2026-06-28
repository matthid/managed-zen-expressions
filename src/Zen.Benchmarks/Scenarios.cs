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
    public static IEnumerable<string> StandardNames => All.Where(s => s.Group != "heavy").Select(s => s.Name);
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

        // --- heavy scenarios: probe where (if anywhere) native overtakes managed
        // under high load. dataN = arrays of N; the contexts are generated below. ---
        AddHeavy(list);
        return list.ToArray();
    }

    /// <summary>Heavy scenarios only (for the focused HeavyBench + crossover chart).</summary>
    public static readonly string[] HeavyNames = HeavyScenarioNames();

    private static string[] HeavyScenarioNames() => All.Where(s => s.Group == "heavy").Select(s => s.Name).ToArray();

    private static void AddHeavy(List<Scenario> list)
    {
        // 1000-element array context, reused by several scenarios.
        var data1k = new StringBuilder();
        for (int i = 1; i <= 1000; i++) { if (i > 1) data1k.Append(','); data1k.Append(i); }
        string ctx1k = "{\"data\":[" + data1k + "]}";

        // 1) Heavy compute, SCALAR result -> cheap marshal; tests raw eval speed.
        list.Add(new() { Name = "heavy-sum-1k", Group = "heavy",
            Expression = "sum(data)", ContextJson = ctx1k });

        // 1b) Heavy INTERMEDIATE allocation, SCALAR result. map() builds a 1000-element
        // array that is consumed by sum() and never returned -> on native the array lives
        // on the native heap (no GC) and only a number is marshalled back. This is the
        // case that should favour native most.
        list.Add(new() { Name = "heavy-sum-map-1k", Group = "heavy",
            Expression = "sum(map(data, # * # + 1))", ContextJson = ctx1k });

        // 2) Heavy compute via a wide arithmetic expression (200 terms), scalar result.
        var sbA = new StringBuilder();
        var sbC = new StringBuilder("{");
        for (int i = 0; i < 200; i++) {
            if (i > 0) sbA.Append('+');
            sbA.Append("v").Append(i);
            if (i > 0) sbC.Append(',');
            sbC.Append("\"v").Append(i).Append("\":").Append(i + 1);
        }
        sbC.Append('}');
        list.Add(new() { Name = "heavy-arith-200", Group = "heavy",
            Expression = sbA.ToString(), ContextJson = sbC.ToString() });

        // 3) Heavy compute + LARGE result array (1000 elements) -> native pays marshal-back.
        list.Add(new() { Name = "heavy-map-1k", Group = "heavy",
            Expression = "map(data, # * # + 1)", ContextJson = ctx1k });

        // 4) Heavy compute + ~500-element filtered result.
        list.Add(new() { Name = "heavy-filter-1k", Group = "heavy",
            Expression = "filter(data, # > 500)", ContextJson = ctx1k });

        // 5) Heavy ALLOCATION: map producing 100 objects (3 keys each).
        var rows = new StringBuilder("[");
        for (int i = 0; i < 100; i++) {
            if (i > 0) rows.Append(',');
            rows.Append("{\"x\":").Append(i + 1).Append(",\"y\":").Append(100 - i).Append('}');
        }
        rows.Append(']');
        list.Add(new() { Name = "heavy-map-objects-100", Group = "heavy",
            Expression = "map(rows, { a: #.x * 2, b: #.y + 1, c: #.x + #.y })",
            ContextJson = "{\"rows\":" + rows + "}" });
    }
}
