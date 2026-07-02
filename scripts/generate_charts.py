#!/usr/bin/env python3
"""Generate dependency-free SVG charts for the README from the benchmark numbers.

GitHub renders <img src="*.svg"> inline, so SVG keeps the repo text-friendly and
crisp at any size. Numbers are transcribed from results/bench-full.txt and the
--mem report (AMD Ryzen 9 5900X, .NET 8.0.28, Ubuntu 24.04 container).

Scenarios cover BOTH scalar-producing expressions (condition evaluation, where
managed hits 0 alloc) AND allocating/reshaping expressions (map / object / string
building, where it does not) — so the comparison is fair.
"""
import math
import os

OUT = os.path.join(os.path.dirname(__file__), "..", "docs", "charts")
os.makedirs(OUT, exist_ok=True)

MANAGED = "#2ca02c"
NATIVE = "#1f77b4"
GORULES = "#d62728"
ZENENGINE = "#ff7f0e"
MANAGED_LIGHT = "#9cdb9c"
GRID = "#dddddd"
TEXT = "#222222"
MUTED = "#666666"

SCENARIOS = ["simple-few", "complex-few", "simple-many", "complex-many",
             "alloc-string", "alloc-object", "alloc-array"]


def fmt_ns(v):
    if v >= 1000:
        return f"{v/1000:.1f} µs"
    if v >= 10:
        return f"{v:.0f} ns"
    return f"{v:.1f} ns"


def fmt_bytes(v):
    if v >= 1024:
        return f"{v/1024:.1f} KB"
    return f"{v:.0f} B"


def fmt_kb(v):
    if v >= 1024:
        return f"{v/1024:.1f} MB"
    return f"{v:.0f} KB"


def fmt_ms(v):
    if v > 0 and v < 1:
        return f"{v*1000:.0f} µs"
    return f"{v:.1f} ms"


def fmt_us(v):
    if v >= 1000:
        return f"{v/1000:.1f} ms"
    return f"{v:.1f} µs"


def grouped_bars(series, categories, title, note, fmt, fname,
                 log=True, width=980, height=430, floor=None):
    margin_l, margin_r, margin_t, margin_b = 64, 16, 56, 96
    plot_w = width - margin_l - margin_r
    plot_h = height - margin_t - margin_b

    all_vals = [v for _, _, vals in series for v in vals]
    nz = [v for v in all_vals if v > 0]
    vmin = (floor if floor else min(nz)) if nz else 0
    vmax = max(all_vals) if all_vals else 1
    if log:
        lo, hi = math.log10(max(vmin, 1e-9)), math.log10(max(vmax, vmin * 10))
        span = hi - lo if hi > lo else 1
        def y_of(v):
            if v <= 0:
                return margin_t + plot_h
            return margin_t + plot_h - (math.log10(v) - lo) / span * plot_h
    else:
        span = vmax - vmin if vmax > vmin else 1
        def y_of(v):
            return margin_t + plot_h - (v - vmin) / span * plot_h

    n_grp = len(categories)
    n_ser = len(series)
    grp_w = plot_w / n_grp
    bar_w = (grp_w * 0.78) / n_ser
    x0_grp = lambda i: margin_l + i * grp_w + grp_w * 0.11

    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" '
             f'viewBox="0 0 {width} {height}" font-family="Segoe UI,Helvetica,Arial,sans-serif">']
    parts.append(f'<rect width="{width}" height="{height}" fill="#ffffff"/>')
    parts.append(f'<text x="{width/2:.0f}" y="26" text-anchor="middle" font-size="17" '
                 f'font-weight="600" fill="{TEXT}">{title}</text>')

    if log:
        ticks = []
        e = math.floor(lo)
        while e <= math.ceil(hi):
            ticks.append(10 ** e)
            e += 1
    else:
        ticks = [vmin + span * i / 5 for i in range(6)]
    base_y = margin_t + plot_h
    for t in ticks:
        y = y_of(t)
        parts.append(f'<line x1="{margin_l}" y1="{y:.1f}" x2="{margin_l+plot_w}" y2="{y:.1f}" '
                     f'stroke="{GRID}" stroke-width="1"/>')
        parts.append(f'<text x="{margin_l-8}" y="{y+4:.1f}" text-anchor="end" font-size="11" '
                     f'fill="{MUTED}">{fmt(t)}</text>')

    for gi, cat in enumerate(categories):
        for si, (name, color, vals) in enumerate(series):
            v = vals[gi]
            x = x0_grp(gi) + si * bar_w
            y = y_of(v)
            h = base_y - y
            if v <= 0:
                y = base_y - 2; h = 2
            parts.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{bar_w*0.86:.1f}" height="{h:.1f}" '
                         f'fill="{color}" rx="1.5"/>')
            label = "0" if v <= 0 else fmt(v)
            parts.append(f'<text x="{x+bar_w*0.43:.1f}" y="{y-4:.1f}" text-anchor="middle" '
                         f'font-size="9" fill="{TEXT}">{label}</text>')
        cx = margin_l + gi * grp_w + grp_w / 2
        parts.append(f'<text x="{cx:.1f}" y="{base_y+18:.1f}" text-anchor="middle" font-size="11" '
                     f'fill="{TEXT}">{cat}</text>')

    parts.append(f'<line x1="{margin_l}" y1="{base_y}" x2="{margin_l+plot_w}" y2="{base_y}" '
                 f'stroke="{TEXT}" stroke-width="1.2"/>')

    lx = margin_l
    ly = height - 26
    for name, color, _ in series:
        parts.append(f'<rect x="{lx}" y="{ly-11}" width="14" height="14" fill="{color}" rx="2"/>')
        parts.append(f'<text x="{lx+20}" y="{ly}" font-size="12" fill="{TEXT}">{name}</text>')
        lx += 26 + len(name) * 7 + 24

    if note:
        parts.append(f'<text x="{width-12}" y="{height-8}" text-anchor="end" font-size="11" '
                     f'font-style="italic" fill="{MUTED}">{note}</text>')

    parts.append('</svg>')
    with open(os.path.join(OUT, fname), "w") as f:
        f.write("\n".join(parts))
    print("wrote", fname)


# ---- pure-eval throughput (ns) ----
grouped_bars(
    series=[
        ("Managed (pure)", MANAGED, [132.0, 284.8, 2124.9, 2030.0, 302.0, 606.4, 1975.5]),
        ("Native manual (pure)", NATIVE, [247.8, 333.8, 2070.1, 1786.9, 682.3, 1853.9, 3997.4]),
        ("GoRules.Zen", GORULES, [3715.5, 6571.3, 59196.7, 62367.3, 4749.2, 36092.5, 21826.2]),
        ("GoRules.ZenEngine", ZENENGINE, [1194.1, 1630.7, 8834.6, 5395.9, 1361.2, 3752.3, 6287.8]),
    ],
    categories=SCENARIOS,
    title="Evaluation throughput — compile-once / evaluate-many (pre-parsed context)",
    note="log scale · lower is better · ZenEngine (compiled+sync) ~3-12x faster than GoRules.Zen, still trails managed",
    fmt=fmt_ns, fname="eval-pure.svg",
)

# ---- JSON-eval throughput (ns) ----
grouped_bars(
    series=[
        ("Managed (JSON)", MANAGED, [546.8, 978.4, 7083.7, 4409.3, 598.8, 1055.2, 3150.7]),
        ("Native manual (JSON)", NATIVE, [656.6, 993.4, 9412.6, 4633.7, 1197.1, 1809.2, 4927.3]),
        ("GoRules.Zen", GORULES, [4411.9, 7645.9, 63891.0, 65884.2, 5825.2, 32194.9, 26310.7]),
        ("GoRules.ZenEngine", ZENENGINE, [1285.1, 1726.2, 8342.4, 5359.0, 1440.9, 2965.9, 6799.3]),
    ],
    categories=SCENARIOS,
    title="Evaluation throughput — JSON context per call (parse + eval)",
    note="log scale · lower is better · ZenEngine's Rust serde nearly ties managed on large contexts",
    fmt=fmt_ns, fname="eval-json.svg",
)

# ---- memory: pure-eval allocation per op (managed GC vs native real heap) ----
grouped_bars(
    series=[
        ("Managed GC", MANAGED, [0, 0, 0, 0, 264, 552, 824]),
        ("Native heap (real)", NATIVE, [145, 140, 148, 142, 593, 833, 966]),
    ],
    categories=SCENARIOS,
    title="Pure-eval memory per op — managed is 0 only for scalar expressions",
    note="log scale · allocating expressions (right 3) do allocate on the managed hot path · all 4 engines' numbers in the table below",
    fmt=fmt_bytes, floor=1, fname="memory-pure.svg",
)

# ---- memory: JSON-eval — the native-heap blind spot ----
grouped_bars(
    series=[
        ("Managed GC", MANAGED, [880, 1008, 11120, 5112, 760, 1128, 2040]),
        ("Native heap (real)", NATIVE, [1281, 1344, 8732, 5962, 1849, 2477, 4400]),
    ],
    categories=SCENARIOS,
    title="JSON-eval memory per op — .NET metrics see only the green bar; native heap is hidden",
    note="log scale · BenchmarkDotNet's 'Allocated' misses the blue native heap entirely",
    fmt=fmt_bytes, floor=1, fname="memory-json.svg",
)

# ---- isolated interop overhead ----
grouped_bars(
    series=[
        ("Managed inline add", MANAGED, [0.0]),
        ("Native zen_add (1 P/Invoke)", NATIVE, [6.81]),
    ],
    categories=["a + b"],
    title="Isolated P/Invoke overhead — the boundary itself is cheap (~7 ns)",
    note="linear scale · this is NOT what slows the native engines down",
    fmt=fmt_ns, log=False, floor=0, fname="interop.svg", width=560, height=320,
)

# ---- parse / compile throughput (ns) ----
grouped_bars(
    series=[
        ("Managed", MANAGED, [522, 1558, 5751, 7025, 1062, 2200, 625]),
        ("Native (manual)", NATIVE, [691, 1913, 10674, 11150, 1163, 2566, 684]),
    ],
    categories=SCENARIOS,
    title="Parse / compile throughput (source text → compiled)",
    note="log scale · lower is better · left 4 = scalar, right 3 = allocating",
    fmt=fmt_ns, fname="parse.svg",
)

# ---- limits enforcement overhead (ns): Evaluate off vs on ----
grouped_bars(
    series=[
        ("Off (no limits)", MANAGED, [141, 291, 2243, 2113, 296, 534, 1937]),
        ("On (Default limits)", NATIVE, [141, 288, 2294, 2075, 301, 556, 1931]),
    ],
    categories=SCENARIOS,
    title="Resource-limit enforcement overhead — Evaluate off vs on (near-zero)",
    note="log scale · bars overlap because O(1) charging adds ~0-4%",
    fmt=fmt_ns, fname="limits-overhead.svg",
)

# ---- binary footprint per engine (KB) ----
grouped_bars(
    series=[
        ("Footprint shipped", MANAGED, [38.5, 556.6, 19227.4, 12499.7]),
    ],
    categories=["Managed", "Native (manual)", "GoRules.Zen", "GoRules.ZenEngine"],
    title="Binary footprint — what you ship per engine",
    note="log scale · GoRules.Zen ~19 MB (libzen_ffi + libcapstone); ZenEngine ~12.5 MB (no capstone)",
    fmt=fmt_kb, fname="footprint.svg", width=620, height=320,
)

# ---- cold first call per engine (ms) ----
grouped_bars(
    series=[
        ("Cold first call", MANAGED, [8.92, 0.36, 59.69, 7.32]),
    ],
    categories=["Managed", "Native (manual)", "GoRules.Zen", "GoRules.ZenEngine"],
    title="Cold first call (fresh process: lib load + JIT + first eval)",
    note="log scale · GoRules.Zen ~60 ms (thread-pool); ZenEngine 7.3 ms (sync, beats managed JIT)",
    fmt=fmt_ms, fname="cold-start.svg", width=620, height=320,
)

# ---- heavy-load crossover (pure-eval, µs) ----
HEAVY = ["sum-1k", "sum-map-1k", "arith-200", "filter-1k", "map-1k", "map-obj-100"]
grouped_bars(
    series=[
        ("Managed (pure)", MANAGED, [3.733, 73.620, 9.262, 56.554, 77.821, 31.682]),
        ("Native (pure)", NATIVE, [2.815, 69.174, 9.434, 98.081, 184.979, 78.319]),
        ("GoRules.Zen", GORULES, [154.537, 229.508, 141.012, 269.371, 383.095, 251.153]),
        ("GoRules.ZenEngine", ZENENGINE, [50.971, 139.080, 31.559, 162.836, 243.001, 146.661]),
    ],
    categories=HEAVY,
    title="Heavy load — pure-eval: ZenEngine parses context per call (~ties managed JSON); managed pure still leads",
    note="log scale · lower is better · ZenEngine_Pure lines up with managed JSON (Rust re-parses the context bytes each call)",
    fmt=fmt_us, fname="heavy-pure.svg",
)

# ---- heavy-load crossover (JSON-eval, µs) ----
grouped_bars(
    series=[
        ("Managed (JSON)", MANAGED, [50.950, 116.531, 29.529, 101.458, 131.256, 56.696]),
        ("Native (JSON)", NATIVE, [24.085, 89.891, 53.034, 116.793, 202.177, 118.224]),
        ("GoRules.Zen", GORULES, [177.231, 247.859, 141.739, 304.704, 417.552, 277.981]),
        ("GoRules.ZenEngine", ZENENGINE, [49.801, 142.354, 32.831, 155.811, 261.358, 148.853]),
    ],
    categories=HEAVY,
    title="Heavy load — JSON per call: native leads when allocation stays native + scalar returns",
    note="log scale · lower is better · sum-1k / sum-map-1k favour native; map/filter (large result) favour managed",
    fmt=fmt_us, fname="heavy-json.svg",
)

print("done")
