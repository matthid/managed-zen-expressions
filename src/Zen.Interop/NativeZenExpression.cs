using Zen.Managed;

namespace Zen.Interop;

/// <summary>
/// A compiled native (Rust) Zen expression. Wraps an opaque native handle and
/// parses results back into <see cref="ZenValue"/> for direct comparison with
/// the managed implementation.
/// </summary>
public sealed class NativeZenExpression : IDisposable
{
    private IntPtr _handle;

    private NativeZenExpression(IntPtr handle) { _handle = handle; }

    public static NativeZenExpression Compile(string source)
        => new(NativeBindings.Compile(source));

    /// <summary>Parse JSON context + evaluate (the realistic per-call path).</summary>
    public ZenValue Evaluate(string contextJson)
        => ZenJson.Parse(NativeBindings.EvalJson(_handle, contextJson));

    /// <summary>Evaluate against a pre-parsed native context (isolates eval from parsing).</summary>
    public ZenValue Evaluate(NativeContext context)
        => ZenJson.Parse(NativeBindings.EvalCtx(_handle, context.Handle));

    /// <summary>The raw result JSON string (no parse-back; useful for benchmarks).</summary>
    public string EvaluateRaw(string contextJson)
        => NativeBindings.EvalJson(_handle, contextJson);

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeBindings.ExprFree(_handle);
            _handle = IntPtr.Zero;
        }
    }
}

/// <summary>A pre-parsed native context object, reusable across evaluations.</summary>
public sealed class NativeContext : IDisposable
{
    internal IntPtr Handle { get; }

    private NativeContext(IntPtr handle) { Handle = handle; }

    public static NativeContext Parse(string json)
        => new(NativeBindings.CtxParse(json));

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) NativeBindings.CtxFree(Handle);
    }
}

/// <summary>Native heap accounting (read from the counting global allocator).</summary>
public static class NativeMemory
{
    public static bool IsAvailable
    {
        get { try { return NativeBindings.IsAvailable; } catch { return false; } }
    }

    public static ulong AllocatedBytes => NativeBindings.MemAllocatedBytes;
    public static ulong DeallocatedBytes => NativeBindings.MemDeallocatedBytes;
    public static ulong InUseBytes => NativeBindings.MemInUseBytes;
    public static ulong AllocCount => NativeBindings.MemAllocCount;
    public static ulong DeallocCount => NativeBindings.MemDeallocCount;
    public static void ResetCounters() => NativeBindings.MemReset();
}
