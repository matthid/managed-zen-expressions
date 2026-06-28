namespace Zen.Managed;

/// <summary>
/// Structural comparison of two values, with an optional numeric tolerance.
/// Used by the parity tests to compare managed and native results; the
/// tolerance absorbs last-ULP differences between <c>Math.Pow</c> and
/// <c>f64::powf</c> in the two runtimes.
/// </summary>
public static class ZenCompare
{
    public static bool DeepEquals(ZenValue a, ZenValue b, double tolerance = 0.0)
        => Equals(a, b, tolerance);

    private static bool Equals(ZenValue a, ZenValue b, double tol)
    {
        if (a.Kind != b.Kind) return false;
        switch (a.Kind)
        {
            case ZenKind.Null: return true;
            case ZenKind.Boolean: return a.Boolean == b.Boolean;
            case ZenKind.Number:
                if (tol <= 0) return a.Number == b.Number;
                double diff = System.Math.Abs(a.Number - b.Number);
                double scale = System.Math.Max(1.0, System.Math.Max(System.Math.Abs(a.Number), System.Math.Abs(b.Number)));
                return diff <= tol * scale;
            case ZenKind.String: return a.String == b.String;
            case ZenKind.Array:
            {
                var aa = a.Array!; var ba = b.Array!;
                if (aa.Length != ba.Length) return false;
                for (int i = 0; i < aa.Length; i++) if (!Equals(aa[i], ba[i], tol)) return false;
                return true;
            }
            case ZenKind.Object:
            {
                if (a.Object!.Count != b.Object!.Count) return false;
                foreach (var kv in a.Object)
                    if (!b.Object.TryGetValue(kv.Key, out var bv) || !Equals(kv.Value, bv, tol)) return false;
                return true;
            }
            default: return false;
        }
    }
}
