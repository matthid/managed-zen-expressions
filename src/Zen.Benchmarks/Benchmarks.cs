using BenchmarkDotNet.Attributes;
using Zen.Gorules;
using Zen.Interop;
using Zen.Managed;
using Zen.ZenEngine;

namespace Zen.Benchmarks;

/// <summary>
/// Evaluation throughput. Four paths per scenario:
///   * Managed_Pure     - pre-parsed ZenValue context (isolates pure eval)
///   * Native_Pure      - pre-parsed native context handle (isolates native eval + interop)
///   * Managed_Json     - JSON string context (parse + eval)
///   * Native_Json      - JSON string context via interop (native parse + eval + marshal)
/// </summary>
[MemoryDiagnoser]
public class EvalBench
{
    [ParamsSource(nameof(Names))]
    public string ScenarioName { get; set; } = "";

    public static IEnumerable<string> Names => Scenarios.StandardNames;

    private Scenario s = null!;

    [GlobalSetup]
    public void Setup()
    {
        s = Scenarios.ByName(ScenarioName);
        s.ManagedExpr = ZenExpression.Compile(s.Expression);
        s.ManagedCtx = ZenJson.Parse(s.ContextJson);
        s.NativeExpr = NativeZenExpression.Compile(s.Expression);
        s.NativeCtx = NativeContext.Parse(s.ContextJson);
        s.GorulesExpr = GorulesZenExpression.Compile(s.Expression);
        s.GorulesDoc = System.Text.Json.JsonDocument.Parse(s.ContextJson);
        s.GorulesCtx = s.GorulesDoc.RootElement.Clone();
        s.ZenEngineExpr = ZenEngineExpression.Compile(s.Expression).UseContext(s.ContextJson);
    }

    [GlobalCleanup]
    public void Cleanup() => s.ZenEngineExpr?.Dispose();

    [Benchmark(Baseline = true)]
    public ZenValue Managed_Pure() => s.ManagedExpr!.Evaluate(s.ManagedCtx);

    [Benchmark]
    public ZenValue Native_Pure() => s.NativeExpr!.Evaluate(s.NativeCtx!);

    [Benchmark]
    public ZenValue Gorules_Pure() => s.GorulesExpr!.Evaluate(s.GorulesCtx);

    [Benchmark]
    public ZenValue ZenEngine_Pure() => s.ZenEngineExpr!.Evaluate();

    [Benchmark]
    public ZenValue Managed_Json() => s.ManagedExpr!.Evaluate(s.ContextJson);

    [Benchmark]
    public ZenValue Native_Json() => s.NativeExpr!.Evaluate(s.ContextJson);

    [Benchmark]
    public ZenValue Gorules_Json() => s.GorulesExpr!.Evaluate(s.ContextJson);

    [Benchmark]
    public ZenValue ZenEngine_Json() => s.ZenEngineExpr!.Evaluate(s.ContextJson);
}

/// <summary>
/// Parse/compile throughput: turning source text into a reusable compiled form.
/// </summary>
[MemoryDiagnoser]
public class ParseBench
{
    [ParamsSource(nameof(Names))]
    public string ScenarioName { get; set; } = "";

    public static IEnumerable<string> Names => Scenarios.StandardNames;

    private Scenario s = null!;

    [GlobalSetup]
    public void Setup() => s = Scenarios.ByName(ScenarioName);

    [Benchmark(Baseline = true)]
    public ZenExpression Managed() => ZenExpression.Compile(s.Expression);

    [Benchmark]
    public void Native()
    {
        // Compile and immediately free so we measure steady-state (not a handle leak).
        using var expr = NativeZenExpression.Compile(s.Expression);
    }
}

/// <summary>
/// Isolates the cost of crossing the managed/native boundary with no work on
/// either side beyond the call itself.
/// </summary>
[MemoryDiagnoser]
public class InteropOverheadBench
{
    [Benchmark(Baseline = true)]
    public double Managed_Add() => 1.5 + 2.5;

    [Benchmark]
    public double Native_Add() => NativeMemory.ProbeAdd(1.5, 2.5);
}

/// <summary>
/// Measures the overhead of ENFORCING <see cref="ZenLimits"/> on the hot path:
/// Evaluate without limits (baseline) vs with Default / Strict budgets.
/// </summary>
[MemoryDiagnoser]
public class LimitsBench
{
    [ParamsSource(nameof(Names))]
    public string ScenarioName { get; set; } = "";

    public static IEnumerable<string> Names => Scenarios.StandardNames;

    private Scenario s = null!;

    [GlobalSetup]
    public void Setup()
    {
        s = Scenarios.ByName(ScenarioName);
        s.ManagedExpr = ZenExpression.Compile(s.Expression);
        s.ManagedCtx = ZenJson.Parse(s.ContextJson);
    }

    [Benchmark(Baseline = true)]
    public ZenValue Off() => s.ManagedExpr!.Evaluate(s.ManagedCtx);

    [Benchmark]
    public ZenValue On_Default() => s.ManagedExpr!.Evaluate(s.ManagedCtx, ZenLimits.Default);

    [Benchmark]
    public ZenValue On_Strict() => s.ManagedExpr!.Evaluate(s.ManagedCtx, ZenLimits.Strict);
}

/// <summary>
/// Heavy-load scenarios: probe where (if anywhere) the native engine overtakes
/// managed as per-call work grows. Same six paths as <see cref="EvalBench"/>, but
/// only the <c>heavy</c> scenarios (large arrays, wide arithmetic, big result sets).
/// </summary>
[MemoryDiagnoser]
public class HeavyBench
{
    [ParamsSource(nameof(Names))]
    public string ScenarioName { get; set; } = "";

    public static IEnumerable<string> Names => Scenarios.HeavyNames;

    private Scenario s = null!;

    [GlobalSetup]
    public void Setup()
    {
        s = Scenarios.ByName(ScenarioName);
        s.ManagedExpr = ZenExpression.Compile(s.Expression);
        s.ManagedCtx = ZenJson.Parse(s.ContextJson);
        s.NativeExpr = NativeZenExpression.Compile(s.Expression);
        s.NativeCtx = NativeContext.Parse(s.ContextJson);
        s.GorulesExpr = GorulesZenExpression.Compile(s.Expression);
        s.GorulesDoc = System.Text.Json.JsonDocument.Parse(s.ContextJson);
        s.GorulesCtx = s.GorulesDoc.RootElement.Clone();
        s.ZenEngineExpr = ZenEngineExpression.Compile(s.Expression).UseContext(s.ContextJson);
    }

    [GlobalCleanup]
    public void Cleanup() => s.ZenEngineExpr?.Dispose();

    [Benchmark(Baseline = true)]
    public ZenValue Managed_Pure() => s.ManagedExpr!.Evaluate(s.ManagedCtx);

    [Benchmark]
    public ZenValue Native_Pure() => s.NativeExpr!.Evaluate(s.NativeCtx!);

    [Benchmark]
    public ZenValue Gorules_Pure() => s.GorulesExpr!.Evaluate(s.GorulesCtx);

    [Benchmark]
    public ZenValue ZenEngine_Pure() => s.ZenEngineExpr!.Evaluate();

    [Benchmark]
    public ZenValue Managed_Json() => s.ManagedExpr!.Evaluate(s.ContextJson);

    [Benchmark]
    public ZenValue Native_Json() => s.NativeExpr!.Evaluate(s.ContextJson);

    [Benchmark]
    public ZenValue Gorules_Json() => s.GorulesExpr!.Evaluate(s.ContextJson);

    [Benchmark]
    public ZenValue ZenEngine_Json() => s.ZenEngineExpr!.Evaluate(s.ContextJson);
}
