using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Zen.Interop;

using Zen.Managed;

/// <summary>
/// Low-level P/Invoke surface over <c>libzen_native</c>. The library is
/// resolved at first use from <c>ZEN_NATIVE_LIB</c> (or the default search
/// path) via a DllImport resolver, so consumers never hard-code a path.
/// </summary>
internal static class NativeBindings
{
    private const string Lib = "zen_native";
    private static readonly IntPtr s_handle;

    static NativeBindings()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeBindings).Assembly, Resolve);
        s_handle = Resolve(Lib, typeof(NativeBindings).Assembly, null);
        if (s_handle == IntPtr.Zero)
            throw new ZenException(
                $"Could not load native Zen library. Set ZEN_NATIVE_LIB to the full path of libzen_native.so/dylib/dll.");
    }

    public static bool IsAvailable => s_handle != IntPtr.Zero;

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? search)
    {
        if (name != Lib) return IntPtr.Zero;
        string? env = Environment.GetEnvironmentVariable("ZEN_NATIVE_LIB");
        if (!string.IsNullOrEmpty(env) && NativeLibrary.TryLoad(env, assembly, search, out var h))
            return h;
        if (NativeLibrary.TryLoad("zen_native", assembly, search, out h))
            return h;
        if (NativeLibrary.TryLoad("zen_native", out h))
            return h;
        return IntPtr.Zero;
    }

    // ---- compiled expressions & contexts --------------------------------------

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe IntPtr zen_compile(byte* src, UIntPtr len, out IntPtr err);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void zen_expr_free(UIntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe IntPtr zen_ctx_parse(byte* json, UIntPtr len, out IntPtr err);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void zen_ctx_free(UIntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int zen_eval_ctx(UIntPtr expr, UIntPtr ctx,
        out IntPtr outPtr, out UIntPtr outLen, out IntPtr err);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int zen_eval_json(UIntPtr expr, byte* json, UIntPtr len,
        out IntPtr outPtr, out UIntPtr outLen, out IntPtr err);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void zen_free(IntPtr ptr, UIntPtr len);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern double zen_add(double a, double b);

    // ---- memory stats ---------------------------------------------------------

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong zen_mem_allocated_bytes();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong zen_mem_deallocated_bytes();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong zen_mem_in_use_bytes();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong zen_mem_alloc_count();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong zen_mem_dealloc_count();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void zen_mem_reset();

    // ---- friendly wrappers ----------------------------------------------------

    public static unsafe IntPtr Compile(string source)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        IntPtr h;
        fixed (byte* p = bytes)
        {
            h = zen_compile(p, (UIntPtr)bytes.Length, out IntPtr err);
            if (h == IntPtr.Zero) throw new ZenException(ReadError(err));
        }
        return h;
    }

    public static void ExprFree(IntPtr handle) => zen_expr_free((UIntPtr)handle);

    public static unsafe IntPtr CtxParse(string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        IntPtr h;
        fixed (byte* p = bytes)
        {
            h = zen_ctx_parse(p, (UIntPtr)bytes.Length, out IntPtr err);
            if (h == IntPtr.Zero) throw new ZenException(ReadError(err));
        }
        return h;
    }

    public static void CtxFree(IntPtr handle) => zen_ctx_free((UIntPtr)handle);

    /// <summary>Evaluate against a pre-parsed context handle. Returns the result JSON.</summary>
    public static string EvalCtx(IntPtr expr, IntPtr ctx)
    {
        int rc = zen_eval_ctx((UIntPtr)expr, (UIntPtr)ctx, out IntPtr outPtr, out UIntPtr outLen, out IntPtr err);
        if (rc != 0) throw new ZenException(ReadError(err));
        return ReadOwned(outPtr, outLen);
    }

    /// <summary>Evaluate against a JSON context string (parse + eval). Returns the result JSON.</summary>
    public static unsafe string EvalJson(IntPtr expr, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        fixed (byte* p = bytes)
        {
            int rc = zen_eval_json((UIntPtr)expr, p, (UIntPtr)bytes.Length, out IntPtr outPtr, out UIntPtr outLen, out IntPtr err);
            if (rc != 0) throw new ZenException(ReadError(err));
            return ReadOwned(outPtr, outLen);
        }
    }

    /// <summary>Trivial call used to measure raw interop overhead.</summary>
    public static double Add(double a, double b) => zen_add(a, b);

    public static ulong MemAllocatedBytes => zen_mem_allocated_bytes();
    public static ulong MemDeallocatedBytes => zen_mem_deallocated_bytes();
    public static ulong MemInUseBytes => zen_mem_in_use_bytes();
    public static ulong MemAllocCount => zen_mem_alloc_count();
    public static ulong MemDeallocCount => zen_mem_dealloc_count();
    public static void MemReset() => zen_mem_reset();

    // ---- marshalling helpers --------------------------------------------------

    private static string ReadOwned(IntPtr ptr, UIntPtr len)
    {
        if (ptr == IntPtr.Zero) return "null";
        int n = checked((int)(ulong)len);
        string s;
        unsafe { s = Encoding.UTF8.GetString((byte*)ptr, n); }
        zen_free(ptr, len);
        return s;
    }

    /// <summary>Errors are emitted as null-terminated UTF-8 buffers. We read them
    /// as C strings and free them with their UTF-8 byte length.</summary>
    private static string ReadError(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return "unknown native error";
        string s = Marshal.PtrToStringUTF8(ptr) ?? "unknown native error";
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        zen_free(ptr, (UIntPtr)bytes.Length);
        return s;
    }
}
