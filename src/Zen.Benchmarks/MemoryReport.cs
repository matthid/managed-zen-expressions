using Zen.Interop;
using Zen.Managed;

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
        PrintHeader();
        foreach (var s in Scenarios.All)
        {
            using var nativeExpr = NativeZenExpression.Compile(s.Expression);
            using var nativeCtx = NativeContext.Parse(s.ContextJson);
            var managedExpr = ZenExpression.Compile(s.Expression);
            var managedCtx = ZenJson.Parse(s.ContextJson);

            double mPure = ManagedBytes(() => managedExpr.Evaluate(managedCtx));
            double mJson = ManagedBytes(() => managedExpr.Evaluate(s.ContextJson));
            double nPure = NativeBytes(() => nativeExpr.Evaluate(nativeCtx));
            double nJson = NativeBytes(() => nativeExpr.Evaluate(s.ContextJson));

            PrintRow(s.Name + " pure", mPure, nPure);
            PrintRow(s.Name + " json", mJson, nJson);
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

    private static double NativeBytes(Action op)
    {
        for (int i = 0; i < 1000; i++) op();   // warm up
        NativeMemory.ResetCounters();
        for (int i = 0; i < N; i++) op();
        return (double)NativeMemory.AllocatedBytes / N;
    }

    private static void PrintHeader()
    {
        Console.WriteLine($"  {"scenario",-18}{"managed B/op",14}{"native B/op",14}{"real B/op",14}{"native share",14}");
        Console.WriteLine($"  {new string('-', 18)}{new string('-', 14)}{new string('-', 14)}{new string('-', 14)}{new string('-', 14)}");
    }

    private static void PrintRow(string label, double managed, double native)
    {
        double real = managed + native;
        double share = real > 0 ? native / real : 0;
        Console.WriteLine($"  {label,-18}{managed,14:N0}{native,14:N0}{real,14:N0}{share,13:P0}");
    }
}
