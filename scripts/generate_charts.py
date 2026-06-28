#!/usr/bin/env python3
"""Generate dependency-free SVG charts for the README from the benchmark numbers.

GitHub renders <img src="*.svg"> inline, so SVG keeps the repo text-friendly and
crisp at any size. Numbers are transcribed from results/bench-full.txt and the
--mem report (AMD Ryzen 9 5900X, .NET 8.0.28, Ubuntu 24.04 container).
"""
import math
import os

OUT = os.path.join(os.path.dirname(__file__), "..", "docs", "charts")
os.makedirs(OUT, exist_ok=True)

# --- palette ---
MANAGED = "#2ca02c"   # green
NATIVE = "#1f77b4"    # blue
GORULES = "#d62728"   # red
GRID = "#dddddd"
TEXT = "#222222"
MUTED = "#666666"

SCENARIOS = ["simple-few", "complex-few", "simple-many", "complex-many"]


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


def grouped_bars(series, categories, title, note, fmt, fname,
                 log=True, width=860, height=420, floor=None):
    """series: [(name, color, [values...])]. categories: [str]."""
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

    # y gridlines (5 decades or 5 linear ticks)
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

    # bars + value labels + category labels
    for gi, cat in enumerate(categories):
        for si, (name, color, vals) in enumerate(series):
            v = vals[gi]
            x = x0_grp(gi) + si * bar_w
            y = y_of(v)
            h = base_y - y
            if v <= 0:
                # draw a tiny stub so the "0" bar is visible
                y = base_y - 2; h = 2
            parts.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{bar_w*0.86:.1f}" height="{h:.1f}" '
                         f'fill="{color}" rx="1.5"/>')
            label = "0" if v <= 0 else fmt(v)
            parts.append(f'<text x="{x+bar_w*0.43:.1f}" y="{y-4:.1f}" text-anchor="middle" '
                         f'font-size="9.5" fill="{TEXT}">{label}</text>')
        cx = margin_l + gi * grp_w + grp_w / 2
        parts.append(f'<text x="{cx:.1f}" y="{base_y+18:.1f}" text-anchor="middle" font-size="12" '
                     f'fill="{TEXT}">{cat}</text>')

    # axes
    parts.append(f'<line x1="{margin_l}" y1="{base_y}" x2="{margin_l+plot_w}" y2="{base_y}" '
                 f'stroke="{TEXT}" stroke-width="1.2"/>')

    # legend
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


# ---------------------------------------------------------------------------
# Chart 1 — pure-eval throughput (compile once, evaluate many)
# ---------------------------------------------------------------------------
grouped_bars(
    series=[
        ("Managed (pure)", MANAGED, [152.5, 308.8, 2412.1, 2167.3]),
        ("Native manual (pure)", NATIVE, [469.7, 574.7, 2561.3, 2213.0]),
        ("GoRules.Zen", GORULES, [3918.1, 6293.4, 62160.8, 67466.0]),
    ],
    categories=SCENARIOS,
    title="Evaluation throughput — compile-once / evaluate-many (pre-parsed context)",
    note="log scale · lower is better",
    fmt=fmt_ns,
    fname="eval-pure.svg",
)

# ---------------------------------------------------------------------------
# Chart 2 — JSON-eval throughput (parse context + eval, realistic per-call)
# ---------------------------------------------------------------------------
grouped_bars(
    series=[
        ("Managed (JSON)", MANAGED, [1095.7, 1611.8, 10874.9, 6265.1]),
        ("Native manual (JSON)", NATIVE, [974.9, 1316.9, 10557.6, 5086.0]),
        ("GoRules.Zen", GORULES, [4400.3, 6540.4, 68307.4, 73761.6]),
    ],
    categories=SCENARIOS,
    title="Evaluation throughput — JSON context per call (parse + eval)",
    note="log scale · lower is better",
    fmt=fmt_ns,
    fname="eval-json.svg",
)

# ---------------------------------------------------------------------------
# Chart 3 — memory per op: managed GC vs real (incl. native heap)
# ---------------------------------------------------------------------------
grouped_bars(
    series=[
        ("Managed GC (JSON path)", "#7bc47f", [952, 1080, 11192, 5184]),
        ("Native heap, real (JSON)", NATIVE, [1281, 1344, 8732, 5962]),
        ("Managed (pure path)", MANAGED, [0, 0, 0, 0]),
    ],
    categories=SCENARIOS,
    title="Memory per op — the native-heap blind spot (BenchmarkDotNet only sees the GC bars)",
    note="log scale · .NET metrics miss native heap; instrumented allocator reveals it",
    fmt=fmt_bytes, floor=1,
    fname="memory.svg",
)

# ---------------------------------------------------------------------------
# Chart 4 — isolated interop boundary cost
# ---------------------------------------------------------------------------
grouped_bars(
    series=[
        ("Managed inline add", MANAGED, [0.0]),
        ("Native zen_add (1 P/Invoke)", NATIVE, [6.81]),
    ],
    categories=["a + b"],
    title="Isolated P/Invoke overhead — the boundary itself is cheap (~7 ns)",
    note="linear scale · this is NOT what slows the native engines down",
    fmt=fmt_ns, log=False, floor=0,
    fname="interop.svg", width=560, height=320,
)

print("done")
