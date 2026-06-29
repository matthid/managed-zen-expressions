using System.Diagnostics;
using System.Runtime.InteropServices;
using Zen.Gorules;
using Zen.Interop;
using Zen.Managed;
using Zen.ZenEngine;

namespace Zen.Benchmarks;

/// <summary>
/// Measures the fixed "general overhead" of each engine that the per-op
/// benchmarks do NOT show: the binary footprint you ship, the one-off native
/// library load (dlopen), and cold first-touch (JIT + first call) vs warm.
///
/// Cold figures are one-shot for this process; for stability run the mode
/// several times in fresh processes (the runner script does this and reports min).
/// </summary>
public static class OverheadReport
{
    private const string Expr = "a + b * c - d";
    private const string CtxJson = "{\"a\":1.5,\"b\":2,\"c\":3,\"d\":0.5}";
    private static readonly ZenValue Ctx = ZenJson.Parse(CtxJson);

    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("================================================================");
        Console.WriteLine("  GENERAL OVERHEAD  (footprint · native load · cold vs warm)");
        Console.WriteLine("================================================================");

        PrintFootprint();

        Console.WriteLine();
        Console.WriteLine("-- cold first-touch in THIS process (one-shot; JIT + lib load included) --");

        // Order matters: each engine's first call is cold. Measure native dlopen
        // explicitly before anything touches Zen.Interop (which lazy-loads on use).
        long nativeDlopenUs = MeasureNativeDlopen();
        Console.WriteLine($"  native dlopen (libzen_native)      : {nativeDlopenUs,8:N0} µs");

        var (mCompileMs, mEvalMs) = ColdManaged();
        Console.WriteLine($"  managed  cold compile              : {mCompileMs,8:N2} ms");
        Console.WriteLine($"  managed  cold eval                 : {mEvalMs,8:N2} ms");

        var (nCompileMs, nEvalMs) = ColdNative();
        Console.WriteLine($"  native   cold compile (+first pin) : {nCompileMs,8:N2} ms");
        Console.WriteLine($"  native   cold eval                 : {nEvalMs,8:N2} ms");

        var (gCompileMs, gEvalMs) = ColdGorules();
        Console.WriteLine($"  GoRules  cold first call*          : {gCompileMs + gEvalMs,8:N2} ms");
        Console.WriteLine("    * includes libzen_ffi dlopen + UniFFI init + JIT + thread-pool spawn");

        var (zeCompileMs, zeEvalMs) = ColdZenEngine();
        Console.WriteLine($"  ZenEngine cold first call*         : {zeCompileMs + zeEvalMs,8:N2} ms");
        Console.WriteLine("    * includes libzen_uniffi dlopen + JIT + first compiled-eval (synchronous)");

        Console.WriteLine();
        Console.WriteLine("-- warm steady-state (median of 1000 evals; for context vs the cold figures) --");
        var me = ZenExpression.Compile(Expr);
        Console.WriteLine($"  managed  warm eval : {MedianNs(() => me.Evaluate(Ctx), 1000),8:N0} ns");
        using (var ne = NativeZenExpression.Compile(Expr))
        using (var nc = NativeContext.Parse(CtxJson))
        {
            Console.WriteLine($"  native   warm eval : {MedianNs(() => ne.Evaluate(nc), 1000),8:N0} ns");
        }
        var ge = GorulesZenExpression.Compile(Expr);
        Console.WriteLine($"  GoRules  warm eval : {MedianUs(() => ge.Evaluate(CtxJson), 200),8:N1} µs");
        using (var zee = ZenEngineExpression.Compile(Expr).UseContext(CtxJson))
        {
            Console.WriteLine($"  ZenEngine warm eval : {MedianNs(() => zee.Evaluate(), 1000),8:N0} ns");
        }
        Console.WriteLine();
    }

    private static void PrintFootprint()
    {
        Console.WriteLine();
        Console.WriteLine("-- binary footprint (what you ship) --");
        Row("Zen.Managed.dll  (this managed engine)", Size("Zen.Managed.dll"));
        Row("Zen.Interop.dll  (manual P/Invoke wrapper)", Size("Zen.Interop.dll"));
        Row("libzen_native.so (manual native engine)", Size(Environment.GetEnvironmentVariable("ZEN_NATIVE_LIB")));
        Row("GoRules.Zen.dll  (official managed binding)", Size("GoRules.Zen.dll"));
        Row("libzen_ffi.so    (official native engine)", FindSize("libzen_ffi.so"));
        Row("libcapstone.so   (official native dep)", FindSize("libcapstone.so"));
        Row("GoRules.ZenEngine.dll (2nd official binding)", Size("GoRules.ZenEngine.dll"));
        Row("libzen_uniffi.so (ZenEngine native engine)", FindSize("libzen_uniffi.so"));
    }

    // ---- cold measurements (first touch) ----

    private static long MeasureNativeDlopen()
    {
        string? path = Environment.GetEnvironmentVariable("ZEN_NATIVE_LIB");
        if (string.IsNullOrEmpty(path)) return -1;
        var sw = Stopwatch.StartNew();
        NativeLibrary.TryLoad(path, out var h);
        sw.Stop();
        if (h != IntPtr.Zero) NativeLibrary.Free(h);
        return sw.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;
    }

    private static (double compileMs, double evalMs) ColdManaged()
    {
        var sw = Stopwatch.StartNew();
        var expr = ZenExpression.Compile(Expr);
        sw.Stop();
        double compile = sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
        sw.Restart();
        expr.Evaluate(Ctx);
        sw.Stop();
        return (compile, sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency);
    }

    private static (double compileMs, double evalMs) ColdNative()
    {
        var sw = Stopwatch.StartNew();
        using var expr = NativeZenExpression.Compile(Expr);
        sw.Stop();
        double compile = sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
        using var nc = NativeContext.Parse(CtxJson);
        sw.Restart();
        expr.Evaluate(nc);
        sw.Stop();
        return (compile, sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency);
    }

    private static (double compileMs, double evalMs) ColdGorules()
    {
        var sw = Stopwatch.StartNew();
        var expr = GorulesZenExpression.Compile(Expr);
        sw.Stop();
        double compile = sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
        sw.Restart();
        expr.Evaluate(CtxJson);
        sw.Stop();
        return (compile, sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency);
    }

    private static (double compileMs, double evalMs) ColdZenEngine()
    {
        var sw = Stopwatch.StartNew();
        using var expr = ZenEngineExpression.Compile(Expr).UseContext(CtxJson);
        sw.Stop();
        double compile = sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
        sw.Restart();
        expr.Evaluate();
        sw.Stop();
        return (compile, sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency);
    }

    // ---- warm helpers ----

    private static double MedianNs(Action op, int n)
    {
        var xs = new long[n];
        for (int i = 0; i < n; i++) { var sw = Stopwatch.StartNew(); op(); sw.Stop(); xs[i] = sw.ElapsedTicks; }
        Array.Sort(xs);
        return xs[n / 2] * 1_000_000_000.0 / Stopwatch.Frequency;
    }

    private static double MedianUs(Action op, int n)
    {
        var xs = new long[n];
        for (int i = 0; i < n; i++) { var sw = Stopwatch.StartNew(); op(); sw.Stop(); xs[i] = sw.ElapsedTicks; }
        Array.Sort(xs);
        return xs[n / 2] * 1_000_000.0 / Stopwatch.Frequency;
    }

    // ---- footprint helpers ----

    private static long Size(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return -1;
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(path) ? new FileInfo(path).Length : (File.Exists(fileName) ? new FileInfo(fileName).Length : -1);
    }

    private static long FindSize(string fileName)
    {
        var dir = AppContext.BaseDirectory;
        var found = Directory.GetFiles(dir, fileName, SearchOption.AllDirectories);
        return found.Length > 0 ? new FileInfo(found[0]).Length : -1;
    }

    private static void Row(string label, long bytes)
        => Console.WriteLine($"  {label,-48} {FmtBytes(bytes)}");
    private static string FmtBytes(long b) => b < 0 ? "n/a" : b >= 1024 ? $"{b / 1024.0:N1} KB" : $"{b} B";
}
