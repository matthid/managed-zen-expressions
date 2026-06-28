using System.Globalization;
using Zen.Managed.Ast;

namespace Zen.Managed;

/// <summary>
/// Walks an AST against a context and produces a <see cref="ZenValue"/>. The
/// same instance is reused across evaluations; only the context and the
/// closure element stack are per-evaluation state.
/// </summary>
internal sealed class Evaluator
{
    private ZenValue _context;
    private ZenValue[] _elementStack = new ZenValue[32];
    private int _elementTop = -1;

    // Resource accounting (only active when _limits != null).
    private ZenLimits? _limits;
    private long _steps;
    private long _allocs;
    private long _bytes;

    public ZenValue Evaluate(Node root, ZenValue context)
    {
        _context = context;
        _elementTop = -1;
        _limits = null;            // fast path: no enforcement, zero per-node overhead beyond a predicted branch
        return Eval(root);
    }

    public ZenValue Evaluate(Node root, ZenValue context, ZenLimits limits)
    {
        _context = context;
        _elementTop = -1;
        _limits = limits;
        _steps = 0;
        _allocs = 0;
        _bytes = 0;
        return Eval(root);
    }

    /// <summary>Charge one AST-node visit against the step budget. No-op without limits.</summary>
    private void ChargeStep()
    {
        if (_limits != null && ++_steps > _limits.MaxSteps)
            throw new ZenLimitException($"step budget exceeded ({_steps} > {_limits.MaxSteps})");
    }

    /// <summary>Charge a structural allocation (array/object/string) with a byte estimate.</summary>
    internal void ChargeAlloc(long count, long bytes)
    {
        if (_limits == null) return;
        _allocs += count;
        _bytes += bytes;
        if (_allocs > _limits.MaxAllocations)
            throw new ZenLimitException($"allocation budget exceeded ({_allocs} > {_limits.MaxAllocations})");
        if (_bytes > _limits.MaxBytes)
            throw new ZenLimitException($"byte budget exceeded ({_bytes} > {_limits.MaxBytes})");
    }

    private void PushElement(ZenValue v)
    {
        if (++_elementTop >= _elementStack.Length)
        {
            Array.Resize(ref _elementStack, _elementStack.Length * 2);
        }
        _elementStack[_elementTop] = v;
    }

    private ZenValue Eval(Node n)
    {
        if (_limits != null) ChargeStep();   // guarded: no call on the unlimited fast path
        switch (n.Kind)
        {
            case NodeKind.Literal: return n.Value;
            case NodeKind.Ident: return ResolveIdent(n.Name);
            case NodeKind.Current: return _elementTop >= 0 ? _elementStack[_elementTop] : ZenValue.Null;

            case NodeKind.Array:
            {
                var arr = new ZenValue[n.List.Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = Eval(n.List[i]);
                if (_limits != null) ChargeAlloc(1, arr.Length * 24L + 56);
                return ZenValue.FromArray(arr);
            }

            case NodeKind.Object:
            {
                var dict = new Dictionary<string, ZenValue>(n.Keys.Length, StringComparer.Ordinal);
                for (int i = 0; i < n.Keys.Length; i++) dict[n.Keys[i]] = Eval(n.List[i]);
                if (_limits != null) ChargeAlloc(1, dict.Count * 48L + 96);
                return ZenValue.FromObject(dict);
            }

            case NodeKind.Unary:
            {
                ZenValue v = Eval(n.A!);
                if (n.NotFlag) return ZenValue.FromBoolean(!v.IsTruthy); // logical not
                if (n.Inclusive) return v; // unary +
                if (v.TryGetNumber(out double d)) return ZenValue.FromNumber(-d);
                throw new ZenException("Unary minus requires a number");
            }

            case NodeKind.Binary: return EvalBinary(n);
            case NodeKind.Logical: return EvalLogical(n);
            case NodeKind.Compare: return ZenValue.FromBoolean(EvalCompare(n));
            case NodeKind.Ternary: return Eval(Eval(n.A!).IsTruthy ? n.B! : n.C!);

            case NodeKind.Coalesce:
            {
                ZenValue l = Eval(n.A!);
                return l.IsNull ? Eval(n.B!) : l;
            }

            case NodeKind.Member: return Member(Eval(n.A!), n.Name);
            case NodeKind.Index: return Index(Eval(n.A!), Eval(n.B!));

            case NodeKind.Call: return Call(n.Name, n.List);

            case NodeKind.In: return EvalIn(n);

            default:
                throw new ZenException($"Cannot evaluate node kind {n.Kind}");
        }
    }

    private ZenValue ResolveIdent(string name)
    {
        if (name == "$") return _context;
        if (_context.Kind == ZenKind.Object && _context.Object!.TryGetValue(name, out var v)) return v;
        return ZenValue.Null; // missing identifiers resolve to null (lenient access)
    }

    private static ZenValue Member(ZenValue obj, string name)
    {
        if (obj.Kind == ZenKind.Object)
            return obj.Object!.TryGetValue(name, out var v) ? v : ZenValue.Null;
        if (obj.IsNull) return ZenValue.Null; // optional chaining semantics
        throw new ZenException($"Cannot access '.{name}' on {obj.Kind}");
    }

    private static ZenValue Index(ZenValue obj, ZenValue key)
    {
        switch (obj.Kind)
        {
            case ZenKind.Null: return ZenValue.Null;
            case ZenKind.Array:
            {
                if (!key.TryGetNumber(out double k)) throw new ZenException("Array index must be a number");
                int idx = (int)k;
                var arr = obj.Array!;
                if (idx < 0) idx += arr.Length;
                if (idx < 0 || idx >= arr.Length) return ZenValue.Null;
                return arr[idx];
            }
            case ZenKind.Object:
                return obj.Object!.TryGetValue(key.String ?? key.ToString(), out var v) ? v : ZenValue.Null;
            default:
                throw new ZenException($"Cannot index {obj.Kind}");
        }
    }

    // ---- arithmetic -----------------------------------------------------------

    private ZenValue EvalBinary(Node n)
    {
        ZenValue l = Eval(n.A!);
        ZenValue r = Eval(n.B!);

        // String concatenation for `+` when either side is a string.
        if (n.BinOp == BinOp.Add && (l.Kind == ZenKind.String || r.Kind == ZenKind.String))
        {
            string s = Stringify(l) + Stringify(r);
            if (_limits != null) ChargeAlloc(1, s.Length * 2L + 40);
            return ZenValue.FromString(s);
        }

        double a = CanonNumber(l), b = CanonNumber(r);
        return n.BinOp switch
        {
            BinOp.Add => ZenValue.FromNumber(a + b),
            BinOp.Sub => ZenValue.FromNumber(a - b),
            BinOp.Mul => ZenValue.FromNumber(a * b),
            BinOp.Div => ZenValue.FromNumber(a / b),
            BinOp.Mod => ZenValue.FromNumber(a % b),
            BinOp.Pow => ZenValue.FromNumber(Math.Pow(a, b)),
            _ => throw new ZenException("Unknown binary op"),
        };
    }

    internal static double CanonNumber(ZenValue v)
    {
        switch (v.Kind)
        {
            case ZenKind.Number: return v.Number;
            case ZenKind.Boolean: return v.Boolean ? 1 : 0;
            case ZenKind.Null: return 0;
            case ZenKind.String:
                if (double.TryParse(v.String, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
                throw new ZenException($"Cannot convert string '{v.String}' to number");
            default:
                throw new ZenException($"Cannot convert {v.Kind} to number");
        }
    }

    private ZenValue EvalLogical(Node n)
    {
        bool l = Eval(n.A!).IsTruthy;
        if (n.Inclusive) // and
            return ZenValue.FromBoolean(l && Eval(n.B!).IsTruthy);
        return ZenValue.FromBoolean(l || Eval(n.B!).IsTruthy);
    }

    private bool EvalCompare(Node n)
    {
        ZenValue l = Eval(n.A!);
        ZenValue r = Eval(n.B!);

        if (n.CmpOp == CmpOp.Eq) return DeepEqual(l, r);
        if (n.CmpOp == CmpOp.Ne) return !DeepEqual(l, r);

        int c = Compare(l, r);
        return n.CmpOp switch
        {
            CmpOp.Lt => c < 0,
            CmpOp.Gt => c > 0,
            CmpOp.Le => c <= 0,
            CmpOp.Ge => c >= 0,
            _ => false,
        };
    }

    /// <summary>Ordinal comparison returning -1/0/1; both sides must be the same
    /// comparable kind (numbers or strings).</summary>
    private static int Compare(ZenValue l, ZenValue r)
    {
        if (l.Kind == ZenKind.String && r.Kind == ZenKind.String)
            return string.CompareOrdinal(l.String, r.String);
        double a = CanonNumber(l), b = CanonNumber(r);
        return a.CompareTo(b);
    }

    internal static bool DeepEqual(ZenValue a, ZenValue b)
    {
        if (a.Kind != b.Kind)
        {
            // null only equals null
            if (a.IsNull || b.IsNull) return false;
            return false;
        }
        switch (a.Kind)
        {
            case ZenKind.Null: return true;
            case ZenKind.Boolean: return a.Boolean == b.Boolean;
            case ZenKind.Number: return a.Number == b.Number;
            case ZenKind.String: return a.String == b.String;
            case ZenKind.Array:
            {
                var aa = a.Array!;
                var ba = b.Array!;
                if (aa.Length != ba.Length) return false;
                for (int i = 0; i < aa.Length; i++) if (!DeepEqual(aa[i], ba[i])) return false;
                return true;
            }
            case ZenKind.Object:
            {
                if (a.Object!.Count != b.Object!.Count) return false;
                foreach (var kv in a.Object)
                    if (!b.Object.TryGetValue(kv.Key, out var bv) || !DeepEqual(kv.Value, bv)) return false;
                return true;
            }
            default: return false;
        }
    }

    // ---- membership -----------------------------------------------------------

    private ZenValue EvalIn(Node n)
    {
        ZenValue value = Eval(n.A!);
        bool result;

        if (n.B!.Kind == NodeKind.Range)
        {
            double lo = CanonNumber(Eval(n.B.A!));
            double hi = CanonNumber(Eval(n.B.B!));
            if (!value.TryGetNumber(out double x)) result = false;
            else
            {
                bool lowOk = n.B.StartIncl ? x >= lo : x > lo;
                bool highOk = n.B.EndIncl ? x <= hi : x < hi;
                result = lowOk && highOk;
            }
        }
        else
        {
            ZenValue coll = Eval(n.B!);
            if (coll.Kind == ZenKind.Array)
            {
                result = false;
                foreach (var e in coll.Array!)
                    if (DeepEqual(e, value)) { result = true; break; }
            }
            else if (coll.Kind == ZenKind.Object)
            {
                result = value.Kind == ZenKind.String && value.String != null && coll.Object!.ContainsKey(value.String);
            }
            else if (coll.Kind == ZenKind.String)
            {
                result = value.Kind == ZenKind.String && coll.String!.Contains(value.String!);
            }
            else throw new ZenException("Right-hand side of 'in' must be an array, object or string");
        }

        return ZenValue.FromBoolean(n.Inclusive ? !result : result); // Inclusive holds "negated"
    }

    // ---- functions ------------------------------------------------------------

    private ZenValue Call(string name, Node[] args)
    {
        if (!Functions.TryGet(name, out var fn))
            throw new ZenException($"Unknown function '{name}'");
        return fn(this, args);
    }

    // Closures push the current element onto the stack while evaluating a body node.
    internal ZenValue EvalWithElement(Node body, ZenValue element)
    {
        PushElement(element);
        try { return Eval(body); }
        finally { _elementTop--; }
    }

    internal ZenValue GetArg(Node[] args, int i)
    {
        if (i >= args.Length) return ZenValue.Null;
        return Eval(args[i]);
    }

    // ---- stringification ------------------------------------------------------

    internal static string Stringify(ZenValue v) => v.Kind switch
    {
        ZenKind.Null => "null",
        ZenKind.Boolean => v.Boolean ? "true" : "false",
        ZenKind.String => v.String ?? "",
        ZenKind.Number => NumberToString(v.Number),
        _ => v.ToString(),
    };

    internal static string NumberToString(double v)
    {
        if (double.IsNaN(v)) return "NaN";
        if (double.IsPositiveInfinity(v)) return "Infinity";
        if (double.IsNegativeInfinity(v)) return "-Infinity";
        if (v == 0) return "0";
        double t = Math.Truncate(v);
        if (t == v && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
        return v.ToString("R", CultureInfo.InvariantCulture);
    }
}
