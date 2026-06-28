using Xunit;
using Zen.Interop;
using Zen.Managed;

namespace Zen.Tests;

public class ParityTests
{
    private const double Tolerance = 1e-9;

    public static IEnumerable<object[]> Cases => ParityCases.All;

    [Fact]
    public void Native_Library_Is_Loaded()
    {
        // In the canonical Docker build the native lib is always present.
        Assert.True(NativeMemory.IsAvailable, "Native library failed to load (is ZEN_NATIVE_LIB set?)");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Managed_And_Native_Agree(string name, string expression, string contextJson)
    {
        // Compile once in each runtime.
        using var native = NativeZenExpression.Compile(expression);
        var managed = ZenExpression.Compile(expression);

        // Evaluate the same context through each.
        ZenValue nativeResult = native.Evaluate(contextJson);
        ZenValue managedResult = managed.Evaluate(contextJson);

        bool equal = ZenCompare.DeepEquals(managedResult, nativeResult, Tolerance);
        Assert.True(equal,
            $"[{name}] managed and native diverged.\n" +
            $"  expr: {expression}\n" +
            $"  ctx:  {contextJson}\n" +
            $"  managed: {Describe(managedResult)}\n" +
            $"  native:  {Describe(nativeResult)}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Managed_Evaluates_Without_Throwing(string name, string expression, string contextJson)
    {
        var managed = ZenExpression.Compile(expression);
        var result = managed.Evaluate(contextJson);
        Assert.True(result.Kind != (ZenKind)99, name); // just ensure no exception
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Pure_Eval_Path_Matches_Json_Path(string name, string expression, string contextJson)
    {
        // The managed API offers both a pre-parsed context eval and a JSON-string
        // eval; they must agree. (Guards the benchmark's two managed paths.)
        var managed = ZenExpression.Compile(expression);
        ZenValue parsed = ZenJson.Parse(contextJson);
        ZenValue fromParsed = managed.Evaluate(parsed);
        ZenValue fromJson = managed.Evaluate(contextJson);
        Assert.True(ZenCompare.DeepEquals(fromParsed, fromJson, Tolerance),
            $"[{name}] managed parsed-context and json-context diverged.");
    }

    private static string Describe(ZenValue v) =>
        $"{v.Kind} = {ZenJson.Serialize(v)}";
}
