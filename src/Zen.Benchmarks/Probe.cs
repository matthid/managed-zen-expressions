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
            // string-building variants to find what GoRules supports:
            ("concat('a','b','c')", "{}"),
            ("upper('abc')", "{}"),
            ("'x' + string(id)", "{\"id\":42}"),
            ("prefix + '-' + string(id) + '-' + upper(status)", "{\"prefix\":\"ORD\",\"id\":4815,\"status\":\"pending\"}"),
            ("prefix + ' ' + upper(status)", "{\"prefix\":\"ORD\",\"status\":\"pending\"}"),
            ("upper(prefix) + '-' + lower(status)", "{\"prefix\":\"ORD\",\"status\":\"PENDING\"}"),
        };

        foreach (var (expr, ctx) in cases)
        {
            string managed, manualNative, gorules;
            try { managed = ZenJson.Serialize(ZenExpression.Compile(expr).Evaluate(ctx)); }
            catch (Exception ex) { managed = "THROW: " + ex.Message.Split('\n')[0]; }
            try { manualNative = ZenJson.Serialize(NativeZenExpression.Compile(expr).Evaluate(ctx)); }
            catch (Exception ex) { manualNative = "THROW: " + ex.Message.Split('\n')[0]; }
            try
            {
                var gr = GorulesZenExpression.Compile(expr);
                using var doc = JsonDocument.Parse(ctx);
                gorules = ZenJson.Serialize(gr.Evaluate(doc.RootElement));
            }
            catch (Exception ex) { gorules = "THROW: " + ex.GetType().Name + ": " + ex.Message.Split('\n')[0]; }

            Console.WriteLine($"expr: {expr}");
            Console.WriteLine($"  managed      : {managed}");
            Console.WriteLine($"  manual native: {manualNative}");
            Console.WriteLine($"  GoRules.Zen  : {gorules}");
            Console.WriteLine();
        }
    }
}
