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
        ("Managed (pure)", MANAGED, [147.5, 306.3, 2340.3, 2163.1, 305.8, 547.6, 1943.3]),
        ("Native manual (pure)", NATIVE, [456.5, 557.6, 2478.2, 2182.9, 969.4, 1871.1, 5226.5]),
        ("GoRules.Zen", GORULES, [3570.8, 6436.2, 59053.3, 67933.6, 4356.4, 9803.5, 25382.2]),
    ],
    categories=SCENARIOS,
    title="Evaluation throughput — compile-once / evaluate-many (pre-parsed context)",
    note="log scale · lower is better · left 4 = scalar, right 3 = allocating",
    fmt=fmt_ns, fname="eval-pure.svg",
)

# ---- JSON-eval throughput (ns) ----
grouped_bars(
    series=[
        ("Managed (JSON)", MANAGED, [1074.3, 1622.6, 10485.4, 6252.3, 1074.2, 1536.8, 4670.1]),
        ("Native manual (JSON)", NATIVE, [925.5, 1279.8, 10750.2, 5359.7, 1461.7, 2290.7, 6122.5]),
        ("GoRules.Zen", GORULES, [4327.8, 6558.5, 71957.8, 71741.6, 5459.7, 27439.1, 18904.8]),
    ],
    categories=SCENARIOS,
    title="Evaluation throughput — JSON context per call (parse + eval)",
    note="log scale · lower is better · left 4 = scalar, right 3 = allocating",
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
    note="log scale · allocating expressions (right 3) do allocate on the managed hot path",
    fmt=fmt_bytes, floor=1, fname="memory-pure.svg",
)

# ---- memory: JSON-eval — the native-heap blind spot ----
grouped_bars(
    series=[
        ("Managed GC", MANAGED, [952, 1080, 11192, 5184, 832, 1760, 4640]),
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
        ("Footprint shipped", MANAGED, [35, 591, 19228]),
    ],
    categories=["Managed", "Native (manual)", "GoRules (official)"],
    title="Binary footprint — what you ship per engine",
    note="log scale · GoRules ~19 MB (libzen_ffi + libcapstone) vs managed 35 KB",
    fmt=fmt_kb, fname="footprint.svg", width=620, height=320,
)

# ---- cold first call per engine (ms) ----
grouped_bars(
    series=[
        ("Cold first call", MANAGED, [13.0, 0.4, 55.0]),
    ],
    categories=["Managed", "Native (manual)", "GoRules (official)"],
    title="Cold first call (fresh process: lib load + JIT + first eval)",
    note="log scale · GoRules ~55 ms (dlopen 12 MB + UniFFI + thread-pool)",
    fmt=fmt_ms, fname="cold-start.svg", width=620, height=320,
)

# ---- heavy-load crossover (pure-eval, µs) ----
HEAVY = ["sum-1k", "arith-200", "filter-1k", "map-1k", "map-obj-100"]
grouped_bars(
    series=[
        ("Managed (pure)", MANAGED, [3.711, 9.304, 64.696, 85.299, 34.163]),
        ("Native (pure)", NATIVE, [3.303, 10.608, 129.238, 222.610, 108.190]),
        ("GoRules", GORULES, [163.696, 147.498, 296.014, 416.519, 276.224]),
    ],
    categories=HEAVY,
    title="Heavy load — pure-eval (pre-parsed context): native wins only on sum-1k",
    note="log scale · lower is better · native edges ahead on scalar-result compute (sum-1k)",
    fmt=fmt_us, fname="heavy-pure.svg",
)

# ---- heavy-load crossover (JSON-eval, µs) ----
grouped_bars(
    series=[
        ("Managed (JSON)", MANAGED, [83.791, 42.947, 136.012, 150.624, 79.576]),
        ("Native (JSON)", NATIVE, [29.212, 57.683, 150.589, 254.694, 152.648]),
        ("GoRules", GORULES, [189.404, 135.064, 333.826, 478.283, 310.717]),
    ],
    categories=HEAVY,
    title="Heavy load — JSON context per call: native 2.9x faster on sum-1k (serde > hand-rolled JSON)",
    note="log scale · lower is better · large-array serde parse beats the managed JSON reader",
    fmt=fmt_us, fname="heavy-json.svg",
)

print("done")
