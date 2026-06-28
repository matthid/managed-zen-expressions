using System.Text.Json;
using Zen.Gorules;
using Zen.Interop;
using Zen.Managed;

namespace Zen.Benchmarks;

/// <summary>Quick functional sanity check across all three engines.</summary>
public static class Probe
{
    public static void Run()
    {
        var cases = new (string expr, string ctx)[]
        {
            ("a + b * c", "{\"a\":1,\"b\":2,\"c\":3}"),
            ("price * quantity * (1 - discount)", "{\"price\":10,\"quantity\":3,\"discount\":0.1}"),
            ("sum(map(items, # * 2))", "{\"items\":[1,2,3,4]}"),
            ("not true", "{}"),
            ("!true", "{}"),
        };

        foreach (var (expr, ctx) in cases)
        {
            var managed = ZenExpression.Compile(expr).Evaluate(ctx);
            var manualNative = NativeZenExpression.Compile(expr).Evaluate(ctx);

            var gr = GorulesZenExpression.Compile(expr);
            using var doc = JsonDocument.Parse(ctx);
            var gorules = gr.Evaluate(doc.RootElement);

            Console.WriteLine($"expr: {expr}");
            Console.WriteLine($"  managed     : {ZenJson.Serialize(managed)}");
            Console.WriteLine($"  manual native: {ZenJson.Serialize(manualNative)}");
            Console.WriteLine($"  GoRules.Zen : {ZenJson.Serialize(gorules)}");
            Console.WriteLine($"  match(managed==gorules): {ZenCompare.DeepEquals(managed, gorules, 1e-9)}");
            Console.WriteLine();
        }
    }
}
