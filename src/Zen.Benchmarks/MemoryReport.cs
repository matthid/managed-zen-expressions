using System.Text.Json;
using Zen.Gorules;
using Zen.Interop;
using Zen.Managed;
using Zen.ZenEngine;

namespace Zen.Benchmarks;

/// <summary>
/// Side-by-side memory accounting that BenchmarkDotNet cannot give on its own:
/// .NET's GC metrics are blind to native heap, so a naive BDN run would show the
/// interop path as nearly allocation-free. This report measures, per operation:
///   - managed bytes/op (GC.GetAllocatedBytesForCurrentThread)
///   - native  bytes/op (counting global allocator in libzen_native)
///   - "real"  bytes/op  (managed + native) — the true footprint comparison.
/// </summary>
public static class MemoryReport
{
    private const int N = 20_000;

    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("  MEMORY REPORT  (bytes allocated per operation, N=" + N + ")");
        Console.WriteLine("============================================================");
        Console.WriteLine();

        // ----- eval paths -----
        Console.WriteLine("-- eval (JSON context, the realistic per-call path) --");
        Console.WriteLine("  GoRules.Zen / ZenEngine native heaps are opaque; only managed-side bytes are shown.");
        PrintHeader();
        foreach (var s in Scenarios.All)
        {
            using var nativeExpr = NativeZenExpression.Compile(s.Expression);
            using var nativeCtx = NativeContext.Parse(s.ContextJson);
            var managedExpr = ZenExpression.Compile(s.Expression);
            var managedCtx = ZenJson.Parse(s.ContextJson);
            var gorulesExpr = GorulesZenExpression.Compile(s.Expression);
            using var zenEngineExpr = ZenEngineExpression.Compile(s.Expression).UseContext(s.ContextJson);

            double mPure = ManagedBytes(() => managedExpr.Evaluate(managedCtx));
            double mJson = ManagedBytes(() => managedExpr.Evaluate(s.ContextJson));
            double nPure = NativeBytes(() => nativeExpr.Evaluate(nativeCtx));
            double nJson = NativeBytes(() => nativeExpr.Evaluate(s.ContextJson));
            double gManaged = ManagedBytesProcess(() => gorulesExpr.Evaluate(s.ContextJson));
            // ZenEngine is synchronous (no thread-pool dispatch), so per-thread accounting is exact.
            double zeManaged = ManagedBytes(() => zenEngineExpr.Evaluate());

            PrintRow(s.Name + " pure", mPure, nPure);
            PrintRow(s.Name + " json (managed)", mJson, nJson);
            PrintRow(s.Name + " json (GoRules)", gManaged, -1, note: "native opaque");
            PrintRow(s.Name + " zenengine (compiled)", zeManaged, -1, note: "native opaque");
        }

        // ----- parse -----
        Console.WriteLine();
        Console.WriteLine("-- parse/compile (source text -> compiled) --");
        PrintHeader();
        foreach (var s in Scenarios.All)
        {
            double mParse = ManagedBytes(() => ZenExpression.Compile(s.Expression));
            double nParse = NativeBytes(() => { using var e = NativeZenExpression.Compile(s.Expression); });
            PrintRow(s.Name, mParse, nParse);
        }

        // ----- interop overhead -----
        Console.WriteLine();
        Console.WriteLine("-- interop boundary (no work, just the call) --");
        PrintHeader();
        double mAdd = ManagedBytes(() => NativeMemory.ProbeAdd(1.0, 2.0)); // still counts managed wrapper frame
        double nAdd = NativeBytes(() => NativeMemory.ProbeAdd(1.0, 2.0));
        PrintRow("zen_add", mAdd, nAdd);

        // ----- retained native memory across a long run (leak check) -----
        Console.WriteLine();
        NativeMemory.ResetCounters();
        var probe = Scenarios.ByName("complex-many");
        using (var ne = NativeZenExpression.Compile(probe.Expression))
        using (var nc = NativeContext.Parse(probe.ContextJson))
        {
            for (int i = 0; i < N; i++) ne.Evaluate(nc);
        }
        Console.WriteLine($"  native heap retained after {N} evals (complex-many): {NativeMemory.InUseBytes:N0} bytes " +
                          "(transient buffers should be freed back to ~0)");
        Console.WriteLine();
    }

    private static double ManagedBytes(Action op)
    {
        for (int i = 0; i < 1000; i++) op();   // warm up
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < N; i++) op();
        long after = GC.GetAllocatedBytesForCurrentThread();
        return (double)(after - before) / N;
    }

    // Process-wide allocation delta. Used for the GoRules engine, which dispatches
    // its work to the thread pool, so per-thread accounting on the calling thread
    // would miss the allocations done on the worker thread.
    private static double ManagedBytesProcess(Action op)
    {
        for (int i = 0; i < 1000; i++) op();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        for (int i = 0; i < N; i++) op();
        long after = GC.GetTotalAllocatedBytes(precise: true);
        return (double)(after - before) / N;
    }

    private static double NativeBytes(Action op)
    {
        for (int i = 0; i < 1000; i++) op();   // warm up
        NativeMemory.ResetCounters();
        for (int i = 0; i < N; i++) op();
        return (double)NativeMemory.AllocatedBytes / N;
    }

    private static void PrintHeader()
    {
        Console.WriteLine($"  {"scenario",-22}{"managed B/op",14}{"native B/op",14}{"real B/op",14}{"note",-16}");
        Console.WriteLine($"  {new string('-', 22)}{new string('-', 14)}{new string('-', 14)}{new string('-', 14)}{new string('-', 16)}");
    }

    private static void PrintRow(string label, double managed, double nativeB, string note = "")
    {
        string nativeStr = nativeB < 0 ? "-" : $"{nativeB:N0}";
        double real = nativeB < 0 ? managed : managed + nativeB;
        Console.WriteLine($"  {label,-22}{managed,14:N0}{nativeStr,14}{real,14:N0}{note,-16}");
    }
}
