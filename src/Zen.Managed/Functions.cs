using Zen.Managed.Ast;

namespace Zen.Managed;

using ZenFunc = Func<Evaluator, Node[], ZenValue>;

/// <summary>
/// Built-in function table. Higher-order functions (map/filter/some/all/reduce)
/// evaluate a body node per element via <see cref="Evaluator.EvalWithElement"/>,
/// binding <c>#</c> to the current element.
/// </summary>
internal static class Functions
{
    private static readonly Dictionary<string, ZenFunc> _table = Build();

    public static bool TryGet(string name, out ZenFunc fn) => _table.TryGetValue(name, out fn!);

    private static double N(ZenValue v) => Evaluator.CanonNumber(v);

    private static Dictionary<string, ZenFunc> Build()
    {
        var t = new Dictionary<string, ZenFunc>(64, StringComparer.Ordinal);

        // --- collection / aggregate ---
        t["len"] = (ev, a) =>
        {
            var v = ev.GetArg(a, 0);
            return v.Kind switch
            {
                ZenKind.Array => ZenValue.FromNumber(v.Array!.Length),
                ZenKind.String => ZenValue.FromNumber((v.String ?? "").Length),
                ZenKind.Object => ZenValue.FromNumber(v.Object!.Count),
                _ => throw new ZenException("len() expects an array, string or object"),
            };
        };

        t["sum"] = (ev, a) =>
        {
            var v = ev.GetArg(a, 0);
            if (v.Kind != ZenKind.Array) throw new ZenException("sum() expects an array");
            ev.ChargeSteps(v.Array!.Length);
            double s = 0;
            foreach (var e in v.Array) s += N(e);
            return ZenValue.FromNumber(s);
        };

        t["avg"] = (ev, a) =>
        {
            var v = ev.GetArg(a, 0);
            if (v.Kind != ZenKind.Array) throw new ZenException("avg() expects an array");
            if (v.Array!.Length == 0) return ZenValue.FromNumber(0);
            ev.ChargeSteps(v.Array.Length);
            double s = 0;
            foreach (var e in v.Array) s += N(e);
            return ZenValue.FromNumber(s / v.Array.Length);
        };

        t["min"] = (ev, a) => { var v = ev.GetArg(a, 0); if (v.Kind == ZenKind.Array && v.Array!.Length > 0) ev.ChargeSteps(v.Array.Length); return AggregateMinMax(v, max: false); };
        t["max"] = (ev, a) => { var v = ev.GetArg(a, 0); if (v.Kind == ZenKind.Array && v.Array!.Length > 0) ev.ChargeSteps(v.Array.Length); return AggregateMinMax(v, max: true); };

        t["count"] = (ev, a) =>
        {
            var v = ev.GetArg(a, 0);
            return v.Kind == ZenKind.Array ? ZenValue.FromNumber(v.Array!.Length) : ZenValue.FromNumber(0);
        };

        // --- numeric ---
        t["abs"] = (ev, a) => ZenValue.FromNumber(Math.Abs(N(ev.GetArg(a, 0))));
        t["floor"] = (ev, a) => ZenValue.FromNumber(Math.Floor(N(ev.GetArg(a, 0))));
        t["ceil"] = (ev, a) => ZenValue.FromNumber(Math.Ceiling(N(ev.GetArg(a, 0))));
        t["round"] = (ev, a) =>
        {
            double x = N(ev.GetArg(a, 0));
            if (a.Length > 1)
            {
                int dp = (int)N(ev.GetArg(a, 1));
                double m = Math.Pow(10, dp);
                return ZenValue.FromNumber(Math.Round(x * m) / m);
            }
            return ZenValue.FromNumber(Math.Round(x));
        };
        t["sqrt"] = (ev, a) => ZenValue.FromNumber(Math.Sqrt(N(ev.GetArg(a, 0))));
        t["pow"] = (ev, a) => ZenValue.FromNumber(Math.Pow(N(ev.GetArg(a, 0)), N(ev.GetArg(a, 1))));
        t["int"] = (ev, a) => ZenValue.FromNumber(Math.Truncate(N(ev.GetArg(a, 0))));

        // --- conversion ---
        t["number"] = (ev, a) =>
        {
            var v = ev.GetArg(a, 0);
            return v.Kind == ZenKind.Number ? v : ZenValue.FromNumber(N(v));
        };
        t["string"] = (ev, a) => ZenValue.FromString(Evaluator.Stringify(ev.GetArg(a, 0)));
        t["boolean"] = (ev, a) => ZenValue.FromBoolean(ev.GetArg(a, 0).IsTruthy);

        // --- string ---
        t["upper"] = (ev, a) => { string s = (ev.GetArg(a, 0).String ?? "").ToUpperInvariant(); ev.ChargeAlloc(1, s.Length * 2L + 40); return ZenValue.FromString(s); };
        t["lower"] = (ev, a) => { string s = (ev.GetArg(a, 0).String ?? "").ToLowerInvariant(); ev.ChargeAlloc(1, s.Length * 2L + 40); return ZenValue.FromString(s); };
        t["trim"] = (ev, a) => { string s = (ev.GetArg(a, 0).String ?? "").Trim(); ev.ChargeAlloc(1, s.Length * 2L + 40); return ZenValue.FromString(s); };
        t["concat"] = (ev, a) =>
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < a.Length; i++) sb.Append(Evaluator.Stringify(ev.GetArg(a, i)));
            string s = sb.ToString();
            ev.ChargeAlloc(1, s.Length * 2L + 40);
            return ZenValue.FromString(s);
        };
        t["contains"] = (ev, a) =>
            ZenValue.FromBoolean((ev.GetArg(a, 0).String ?? "").Contains(ev.GetArg(a, 1).String ?? ""));
        t["startsWith"] = (ev, a) =>
            ZenValue.FromBoolean((ev.GetArg(a, 0).String ?? "").StartsWith(ev.GetArg(a, 1).String ?? ""));
        t["endsWith"] = (ev, a) =>
            ZenValue.FromBoolean((ev.GetArg(a, 0).String ?? "").EndsWith(ev.GetArg(a, 1).String ?? ""));
        t["indexOf"] = (ev, a) =>
            ZenValue.FromNumber((ev.GetArg(a, 0).String ?? "").IndexOf(ev.GetArg(a, 1).String ?? "", StringComparison.Ordinal));
        t["substring"] = (ev, a) =>
        {
            string s = ev.GetArg(a, 0).String ?? "";
            int start = (int)N(ev.GetArg(a, 1));
            if (start < 0) start = 0;
            string result;
            if (a.Length > 2)
            {
                int end = (int)N(ev.GetArg(a, 2));
                if (end < start) end = start;
                if (end > s.Length) end = s.Length;
                result = s.Substring(start, end - start);
            }
            else result = start >= s.Length ? "" : s.Substring(start);
            ev.ChargeAlloc(1, result.Length * 2L + 40);
            return ZenValue.FromString(result);
        };
        t["replace"] = (ev, a) =>
        { string s = (ev.GetArg(a, 0).String ?? "").Replace(ev.GetArg(a, 1).String ?? "", ev.GetArg(a, 2).String ?? ""); ev.ChargeAlloc(1, s.Length * 2L + 40); return ZenValue.FromString(s); };
        t["split"] = (ev, a) =>
        {
            string s = ev.GetArg(a, 0).String ?? "";
            string sep = ev.GetArg(a, 1).String ?? "";
            var parts = sep.Length == 0 ? new[] { s } : s.Split(sep);
            ev.ChargeAlloc(parts.Length, s.Length * 2L + parts.Length * 40L + 56);
            return ZenValue.FromArray(parts.Select(ZenValue.FromString).ToArray());
        };

        // --- higher-order (closures) ---
        t["map"] = (ev, a) => HigherOrder(ev, a, mode: "map");
        t["filter"] = (ev, a) => HigherOrder(ev, a, mode: "filter");
        t["some"] = (ev, a) => HigherOrder(ev, a, mode: "some");
        t["all"] = (ev, a) => HigherOrder(ev, a, mode: "all");

        return t;
    }

    private static ZenValue AggregateMinMax(ZenValue v, bool max)
    {
        if (v.Kind != ZenKind.Array) throw new ZenException("min()/max() expects an array");
        if (v.Array!.Length == 0) return ZenValue.Null;
        double best = N(v.Array[0]);
        for (int i = 1; i < v.Array.Length; i++)
        {
            double c = N(v.Array[i]);
            if (max ? c > best : c < best) best = c;
        }
        return ZenValue.FromNumber(best);
    }

    private static ZenValue HigherOrder(Evaluator ev, Node[] a, string mode)
    {
        var src = ev.GetArg(a, 0);
        if (src.Kind != ZenKind.Array)
            throw new ZenException($"{mode}() expects an array as the first argument");
        ev.ChargeSteps(src.Array!.Length);   // O(1) up-front; aborts before the work if over budget
        Node body = a[1];

        switch (mode)
        {
            case "map":
            {
                var result = new ZenValue[src.Array!.Length];
                for (int i = 0; i < result.Length; i++)
                    result[i] = ev.EvalWithElement(body, src.Array[i]);
                ev.ChargeAlloc(1, result.Length * 24L + 56);
                return ZenValue.FromArray(result);
            }
            case "filter":
            {
                var result = new List<ZenValue>();
                foreach (var e in src.Array!)
                    if (ev.EvalWithElement(body, e).IsTruthy) result.Add(e);
                var arr = result.ToArray();
                ev.ChargeAlloc(1, arr.Length * 24L + 56);
                return ZenValue.FromArray(arr);
            }
            case "some":
            {
                foreach (var e in src.Array!)
                    if (ev.EvalWithElement(body, e).IsTruthy) return ZenValue.True;
                return ZenValue.False;
            }
            case "all":
            {
                foreach (var e in src.Array!)
                    if (!ev.EvalWithElement(body, e).IsTruthy) return ZenValue.False;
                return ZenValue.True;
            }
            default: throw new ZenException("Unknown higher-order mode");
        }
    }
}
