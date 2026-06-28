using System.Globalization;

namespace Zen.Managed;

/// <summary>
/// The set of value kinds in the Zen expression language. All numbers are
/// IEEE-754 double precision (matching the JavaScript-like number model used
/// by GoRules Zen), which keeps managed and native implementations byte-for-byte
/// comparable.
/// </summary>
public enum ZenKind : byte
{
    Null = 0,
    Boolean,
    Number,
    String,
    Array,
    Object,
}

/// <summary>
/// A first-class Zen value. Implemented as a discriminated struct so that
/// scalar values (null / bool / number) never allocate on the GC heap during
/// evaluation; only strings, arrays and objects do. Returning the struct by
/// value through the evaluator keeps the hot path allocation-light.
/// </summary>
public readonly struct ZenValue
{
    public readonly ZenKind Kind;
    public readonly double Number;
    public readonly bool Boolean;
    public readonly string? String;
    public readonly ZenValue[]? Array;
    public readonly IReadOnlyDictionary<string, ZenValue>? Object;

    private ZenValue(ZenKind kind) { Kind = kind; Number = 0; Boolean = false; String = null; Array = null; Object = null; }
    private ZenValue(double n) { Kind = ZenKind.Number; Number = n; Boolean = false; String = null; Array = null; Object = null; }
    private ZenValue(bool b) { Kind = ZenKind.Boolean; Boolean = b; Number = 0; String = null; Array = null; Object = null; }
    private ZenValue(string s) { Kind = ZenKind.String; String = s; Number = 0; Boolean = false; Array = null; Object = null; }
    private ZenValue(ZenValue[] a) { Kind = ZenKind.Array; Array = a; Number = 0; Boolean = false; String = null; Object = null; }
    private ZenValue(IReadOnlyDictionary<string, ZenValue> o) { Kind = ZenKind.Object; Object = o; Number = 0; Boolean = false; String = null; Array = null; }

    public static readonly ZenValue Null = new(ZenKind.Null);
    public static readonly ZenValue True = new(true);
    public static readonly ZenValue False = new(false);

    public static ZenValue FromNumber(double n) => new(n);
    public static ZenValue FromBoolean(bool b) => b ? True : False;
    public static ZenValue FromString(string s) => new(s);
    public static ZenValue FromArray(ZenValue[] a) => new(a);
    public static ZenValue FromObject(IReadOnlyDictionary<string, ZenValue> o) => new(o);

    public bool IsNull => Kind == ZenKind.Null;
    public bool IsTruthy => Kind switch
    {
        ZenKind.Null => false,
        ZenKind.Boolean => Boolean,
        ZenKind.Number => Number != 0,
        ZenKind.String => !string.IsNullOrEmpty(String),
        ZenKind.Array => Array!.Length != 0,
        ZenKind.Object => Object!.Count != 0,
        _ => false,
    };

    public bool TryGetNumber(out double v)
    {
        if (Kind == ZenKind.Number) { v = Number; return true; }
        if (Kind == ZenKind.Boolean) { v = Boolean ? 1 : 0; return true; }
        if (Kind == ZenKind.String && double.TryParse(String, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return true;
        v = 0;
        return false;
    }

    public override string ToString() => Kind switch
    {
        ZenKind.Null => "null",
        ZenKind.Boolean => Boolean ? "true" : "false",
        ZenKind.Number => Number.ToString("R", CultureInfo.InvariantCulture),
        ZenKind.String => String ?? "",
        _ => $"<{Kind}>",
    };
}
