using System.Text;
using Xunit;
using Zen.Managed;

namespace Zen.Tests;

/// <summary>
/// Exercises the deterministic resource budget (<see cref="ZenLimits"/>). Limits
/// are count-based, so the same input+context+limits always behaves identically
/// regardless of machine load — the assertions below are deterministic.
/// </summary>
public class LimitTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void Default_Limits_Do_Not_Affect_Normal_Expressions()
    {
        var expr = ZenExpression.Compile("(price * quantity * (1 - discount) + shipping) * (1 + tax)");
        var ctx = ZenJson.Parse("{\"price\":19.99,\"quantity\":7,\"discount\":0.1,\"shipping\":5,\"tax\":0.08}");
        // Default budget is generous; normal expressions must evaluate unchanged.
        var result = expr.Evaluate(ctx, ZenLimits.Default);
        Assert.True(ZenCompare.DeepEquals(result, expr.Evaluate(ctx), Tol));
    }

    [Fact]
    public void Step_Budget_Aborts_Iteration_Over_Large_Array()
    {
        // 10 000 elements; the map body is ~3 nodes each => ~30 000 node visits.
        var ctx = ZenJson.Parse("{\"big\":[" + Range(1, 10000) + "]}");
        var expr = ZenExpression.Compile("sum(map(big, # + 1))");

        // Unlimited: completes and returns the correct sum.
        double expected = expr.Evaluate(ctx).Number;

        // Tight step budget: aborts deterministically.
        var tight = new ZenLimits { MaxSteps = 1000 };
        var ex = Assert.Throws<ZenLimitException>(() => expr.Evaluate(ctx, tight));
        Assert.Contains("step budget", ex.Message);

        // Sanity: the unlimited result is correct.
        Assert.Equal(10000d * 10001d / 2d + 10000d, expected, Tol);
    }

    [Fact]
    public void Allocation_Budget_Aborts_Large_Result_Array()
    {
        var ctx = ZenJson.Parse("{\"big\":[" + Range(1, 5000) + "]}");
        var expr = ZenExpression.Compile("map(big, # * 2)");

        // Building a 5 000-element result array allocates well over 1 KiB.
        var tight = new ZenLimits { MaxBytes = 1024 };
        var ex = Assert.Throws<ZenLimitException>(() => expr.Evaluate(ctx, tight));
        Assert.Contains("byte budget", ex.Message);
    }

    [Fact]
    public void Parse_Depth_Aborts_Deeply_Nested_Source()
    {
        // 5 000 nested parentheses — far beyond the default parse-depth cap.
        var deep = "(" + new string('(', 5000) + "1" + new string(')', 5000) + ")";
        var ex = Assert.Throws<ZenLimitException>(() => ZenExpression.Compile(deep));
        Assert.Contains("parse depth", ex.Message);
    }

    [Fact]
    public void Source_Length_Aborts_Oversized_Source()
    {
        var huge = new string('1', 100);
        var limits = new ZenLimits { MaxSourceLength = 10 };
        var ex = Assert.Throws<ZenLimitException>(() => ZenExpression.Compile(huge, limits));
        Assert.Contains("MaxSourceLength", ex.Message);
    }

    [Fact]
    public void Limits_Are_Deterministic_Across_Repeats()
    {
        var ctx = ZenJson.Parse("{\"big\":[" + Range(1, 2000) + "]}");
        var expr = ZenExpression.Compile("sum(map(big, # + 1))");
        var limits = new ZenLimits { MaxSteps = 500 };

        // Same input + limits => same outcome every time (count-based, not time-based).
        for (int i = 0; i < 5; i++)
            Assert.Throws<ZenLimitException>(() => expr.Evaluate(ctx, limits));
    }

    [Fact]
    public void Strict_Preset_Aborts_Hostile_Input()
    {
        // 6 000 elements x ~3 body nodes > Strict.MaxSteps (10 000) => deterministic abort.
        var ctx = ZenJson.Parse("{\"big\":[" + Range(1, 6000) + "]}");
        var expr = ZenExpression.Compile("map(big, # * #)", ZenLimits.Strict);
        Assert.Throws<ZenLimitException>(() => expr.Evaluate(ctx, ZenLimits.Strict));
    }

    private static string Range(int from, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++) { if (i > 0) sb.Append(','); sb.Append(from + i); }
        return sb.ToString();
    }
}
