# Zen Expression Language — Managed vs Native (.NET interop) comparison

A from-scratch implementation of the [GoRules ZEN expression language](https://docs.gorules.io/learn/zen-language/syntax)
evaluated **three ways**, benchmarked to answer one question:

> **Does the raw speed of unmanaged (Rust) code offset the cost of .NET interop
> against a genuinely performant pure-managed (C#) implementation?**

The three engines, all evaluating the *same* Zen subset:

| Engine | What it is |
| --- | --- |
| **`Zen.Managed`**   | Pure C# implementation (lexer, Pratt parser, struct-based evaluator). **The managed library under test.** |
| **`Zen.Native` + `Zen.Interop`** | A manual Rust `cdylib` implementing the same subset, called via my own P/Invoke wrapper. A clean "native eval speed + minimal interop" probe. Its global allocator is instrumented, so its **native heap is measurable**. |
| **`GoRules.Zen`** (NuGet) | The **official** native Rust engine shipped to .NET via UniFFI — the real-world "unmanaged engine via interop" path. |

`Zen.Tests` proves all three agree on a battery of expressions (managed↔native,
managed↔official). `Zen.Benchmarks` measures throughput and memory.

## TL;DR — does native win?

**No, not at this granularity.** A performant managed implementation matches or
beats both native engines for every expression size tested, because:

1. **Raw P/Invoke is cheap (~6.8 ns/call)** — but it is *not* the dominant cost.
   The cost is **marshalling**: the native engines must serialize the context to
   JSON, cross the boundary, and serialize the result back. That is µs-scale and
   dominates ns-scale expression work.
2. The official **`GoRules.Zen` pays a ~4 µs floor on every call** (async `Task`
   + thread-pool dispatch + full JSON context round-trip), so it is **20–34×
   slower** than managed pure-eval regardless of expression size. Its API offers
   no "pre-compiled / pre-parsed context" fast path.
3. The managed hot path allocates **zero GC bytes for scalar-producing
   expressions** (condition evaluation — discriminated `struct` values hold
   arrays/objects *by reference*). Expressions that *reshape* data (`map`,
   object literals, string building) do allocate — 264–824 B/op here — but still
   less than the native engines, which allocate on a **hidden native heap that
   .NET metrics cannot see**. That hidden heap is a memory-accountability trap,
   not an advantage.

The crossover where native *would* pay off requires per-call work large enough
to amortize the fixed marshalling cost — i.e. either enormous expressions or
many evaluations batched inside a single native call. Single-expression calls of
realistic size do not reach it.

## Results at a glance

![Evaluation throughput — compile-once / evaluate-many](docs/charts/eval-pure.svg)

![Evaluation throughput — JSON context per call](docs/charts/eval-json.svg)

![Pure-eval memory — managed is 0 only for scalar expressions](docs/charts/memory-pure.svg)

![JSON-eval memory — the native-heap blind spot](docs/charts/memory-json.svg)

![Isolated P/Invoke overhead](docs/charts/interop.svg)

*(Regenerate with `python3 scripts/generate_charts.py`; numbers transcribed from a
representative run in [`results/bench-full.txt`](results/bench-full.txt) — figures
vary a few % run-to-run. AMD Ryzen 9 5900X, .NET 8.0.28.)*

## Repository layout

```
src/Zen.Managed/      Pure C# engine (the library)
native/zen-native/    Rust cdylib (manual native engine + counting allocator)
src/Zen.Interop/      P/Invoke wrapper over libzen_native
src/Zen.Gorules/      Adapter over the official GoRules.Zen NuGet package
src/Zen.Tests/        xUnit parity (managed↔native 208, managed↔GoRules 69) — all green
src/Zen.Benchmarks/   BenchmarkDotNet suite + standalone --mem / --probe reports
docker/Dockerfile     Multi-stage build (Rust + .NET 8, Ubuntu 24.04 noble for glibc 2.39)
Zen.sln
```

## Build & run (Docker only)

```bash
docker build -t zen-dev -f docker/Dockerfile .

# C# iteration does NOT need an image rebuild (source is bind-mounted):
docker run --rm -v "$PWD":/work -w /work zen-dev dotnet build  Zen.sln -c Release
docker run --rm -v "$PWD":/work -w /work zen-dev dotnet test   src/Zen.Tests -c Release
docker run --rm -v "$PWD":/work -w /work zen-dev dotnet run    -c Release --project src/Zen.Benchmarks             # throughput
docker run --rm -v "$PWD":/work -w /work zen-dev dotnet run    -c Release --project src/Zen.Benchmarks -- --mem        # memory (incl. native heap)
docker run --rm -v "$PWD":/work -w /work zen-dev dotnet run    -c Release --project src/Zen.Benchmarks -- --overhead   # cold-start + footprint
docker run --rm -v "$PWD":/work -w /work zen-dev dotnet run    -c Release --project src/Zen.Benchmarks -- --probe     # 3-engine sanity

# strict compat (fail on unexpected GoRules divergences; default is lenient):
docker run --rm -e ZEN_STRICT_COMPAT=1 -v "$PWD":/work -w /work zen-dev dotnet test src/Zen.Tests -c Release
```

> The official `GoRules.Zen` native lib (`libzen_ffi.so`) requires **GLIBC 2.39**,
> so the image is Ubuntu 24.04 *noble* (the default `sdk:8.0` Debian image only
> has 2.36 and the lib fails to load).

## Results

Hardware: AMD Ryzen 9 5900X, .NET 8.0.28, Linux container. Full output:
[`results/bench-full.txt`](results/bench-full.txt). 7 scenarios: the first four are
scalar-producing (simple/complex × few/many); the last three are **allocating /
data-reshaping** (`map` → array, object literal, string building).

### Evaluation throughput (lower is better)

| Scenario | Managed pure | Native pure (manual) | GoRules (official) | Managed JSON | Native JSON (manual) | GoRules JSON |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| simple-few    | **148 ns** | 457 ns (3.1×) | 3 571 ns (24×) | 1 074 ns | 926 ns | 4 328 ns |
| complex-few   | **306 ns** | 558 ns (1.8×) | 6 436 ns (21×) | 1 623 ns | 1 280 ns | 6 559 ns |
| simple-many   | **2 340 ns** | 2 478 ns (1.1×) | 59 053 ns (25×) | 10 485 ns | 10 750 ns | 71 958 ns |
| complex-many  | **2 163 ns** | 2 183 ns (1.0×) | 67 934 ns (31×) | 6 252 ns | 5 360 ns | 71 742 ns |
| alloc-string  | **306 ns** | 969 ns (3.2×) | 4 356 ns (14×) | 1 074 ns | 1 462 ns | 5 460 ns |
| alloc-object  | **548 ns** | 1 871 ns (3.4×) | 9 804 ns (18×) | 1 537 ns | 2 291 ns | 27 439 ns |
| alloc-array   | **1 943 ns** | 5 227 ns (2.7×) | 25 382 ns (13×) | 4 670 ns | 6 123 ns | 18 905 ns |

Takeaways:
- **Managed wins every cell.** On pure-eval it ties manual-native only for the
  largest scalar expression (`complex-many`); everywhere else it is faster, and on
  the allocating expressions it beats manual-native by **2.7–3.4×**.
- **Allocating expressions are the honest case:** managed pure-eval is *not*
  zero-alloc here (it builds the result array/object/string) — see the memory
  table — yet it still leads on both speed and allocation.
- **GoRules is 13–31× slower** than managed pure-eval. Its `Evaluate<T>` API is
  async, always serializes the context object, and dispatches to the thread pool;
  it offers no pre-compiled / pre-parsed-context fast path.
- **JSON-eval** (parse context + eval): managed and manual-native are within a few
  percent on scalars; on allocating expressions managed pulls ahead because native
  must also marshal the (now non-scalar) result back across the boundary.

### Parse / compile (lower is better)

| Scenario | Managed | Native (manual) |
| --- | ---: | ---: |
| simple-few    | **522 ns** | 691 ns (1.3×) |
| complex-few   | **1 558 ns** | 1 913 ns (1.2×) |
| simple-many   | **5 751 ns** | 10 674 ns (1.9×) |
| complex-many  | **7 025 ns** | 11 150 ns (1.6×) |
| alloc-string  | **1 062 ns** | 1 163 ns (1.1×) |
| alloc-object  | **2 200 ns** | 2 566 ns (1.2×) |
| alloc-array   | **625 ns** | 684 ns (1.1×) |

Managed parsing is faster across the board (fewer allocations, no interop).

### Interop boundary (isolated P/Invoke cost)

| Method | Mean |
| --- | ---: |
| Managed add (inlined) | ~0 ns |
| Native `zen_add` (one P/Invoke) | **6.8 ns** |

So the boundary itself costs ~7 ns. That is *not* what slows the native engines down.

### Memory — and the native-heap blind spot (per op)

BenchmarkDotNet's `Allocated` column is **GC-heap only**. It makes the native
engines look nearly allocation-free, which is misleading. The `--mem` report uses
the instrumented allocator in the manual native lib to show the *real* picture:

**Pure-eval** (compile-once / eval-many) — managed is 0 only for scalar expressions:

| Scenario | Managed GC B/op | Native heap B/op |
| --- | ---: | ---: |
| simple-few / complex-few / simple-many / complex-many | **0** | ~140–148 |
| alloc-string | 264 | 593 |
| alloc-object | 552 | 833 |
| alloc-array  | 824 | 966 |

**JSON-eval** (parse context per call) — native hides most cost on its heap:

| Scenario | Managed GC B/op | Native heap B/op |
| --- | ---: | ---: |
| simple-few   | 952 | 1 281 |
| complex-few  | 1 080 | 1 344 |
| simple-many  | 11 192 | 8 732 |
| complex-many | 5 184 | 5 962 |
| alloc-string | 832 | 1 849 |
| alloc-object | 1 760 | 2 477 |
| alloc-array  | 4 640 | 4 400 |

- On the **pure** scalar hot path managed allocates **nothing**; on allocating
  expressions it allocates the result (264–824 B) — still less than native.
- On the **JSON** path, native pushes most allocation onto its **hidden heap** — BDN
  reports only ~150–560 managed B/op for native-json, but the *real* footprint
  (up to ~9 KB/op of serde-parsed context) only shows up via the counting allocator.
  **Don't trust GC-only numbers to compare against native code.**
- Native heap retained after 20 000 evals: **0 bytes** — no leaks; everything
  transient is freed.

## Analysis: where native *could* win, and why it doesn't here

A native engine wins when **per-call native CPU work ≫ fixed interop + marshalling
cost**. Here the marshalling floor (result JSON round-trip ≈ hundreds of ns for
the manual path; async + context-serialize ≈ ~4 µs for GoRules) is comparable to
or larger than the expression work itself (148 ns–2.3 µs). So the managed JIT —
already very fast on arithmetic and dictionary lookups, and allocating zero on the
scalar hot path (only modest amounts when reshaping data) — matches or beats native.

Native becomes attractive when you (a) make expressions large enough that real
compute dominates, or (b) **batch** many evaluations per interop call so the
marshalling is amortized. Single-expression evaluation calls of realistic size do
not clear that bar — which is the whole point of this comparison.

## Resource limits (deterministic compute/memory budgets)

The managed engine has a count-based resource budget (`ZenLimits`) so an
evaluation behaves **identically regardless of CPU load** — never a wall-clock
timeout that fails a normal expression on a busy machine.

```csharp
var expr = ZenExpression.Compile("sum(map(big, # + 1))");
var result = expr.Evaluate(context, ZenLimits.Default);   // enforces the budget
// throws ZenLimitException if a budget is exceeded
```

| Budget | Counts | Default | Strict |
| --- | --- | --- | --- |
| `MaxSteps` | AST node visits (compute) | 1,000,000 | 10,000 |
| `MaxAllocations` | structural allocs (arrays/objects/strings) | 1,000,000 | 10,000 |
| `MaxBytes` | estimated allocated bytes | 256 MB | 4 MB |
| `MaxSourceLength` | source text (parse guard) | 1 MB | 64 KB |
| `MaxParseDepth` | parser recursion depth | 1,000 | 200 |

- **Opt-in for eval:** `Evaluate(ctx)` enforces nothing (the fast path the
  benchmarks measure); pass a `ZenLimits` to sandbox untrusted input. Parse guards
  are on by default (cheap). When off, the guards add **no measurable overhead**.
- **Near-zero enforcement overhead:** `MaxSteps` is charged **O(1) up-front** at
  array-iterating ops (`sum`/`avg`/`min`/`max`/`map`/`filter`/`some`/`all`/`in`),
  not per AST node; static work is bounded by `MaxSourceLength` at parse time. So
  non-iterative expressions pay nothing to enforce limits. Measured (`LimitsBench`,
  `Evaluate` without limits vs with Default/Strict):

  | Scenario | Off | On (Default) | overhead |
  | --- | ---: | ---: | ---: |
  | simple-few   | 141 ns | 141 ns | ~0% |
  | complex-few  | 291 ns | 288 ns | ~0% |
  | simple-many  | 2 243 ns | 2 294 ns | +2% |
  | complex-many | 2 113 ns | 2 075 ns | ~0% |
  | alloc-string | 296 ns | 301 ns | +1% |
  | alloc-object | 534 ns | 556 ns | +4% |
  | alloc-array  | 1 937 ns | 1 931 ns | ~0% |

  (An earlier per-node counter charged 5–12%; the O(1) iteration design brought it
  to within noise.)
- **DoS-safe:** the language has no user-defined recursion, so there are no
  infinite loops; iterating operations are bounded by `MaxSteps`, allocations by
  `MaxAllocations`/`MaxBytes`. A hostile expression or huge input array aborts
  deterministically instead of hanging or OOMing.
- Aborts throw `ZenLimitException`. Covered by `LimitTests` (7 cases, incl.
  deterministic-repeat and hostile-input).

## Cold-start & footprint overhead

The per-op benchmarks above measure the steady-state hot path. The
`--overhead` report measures the fixed cost you pay once:

| | Managed | Native (manual) | GoRules (official) |
| --- | ---: | ---: | ---: |
| Binary footprint | 35 KB (`Zen.Managed.dll`) | +548 KB (`libzen_native.so`) | **~19 MB** (`libzen_ffi.so` 12.5 MB + `libcapstone.so` 6.7 MB) |
| Native `dlopen` | n/a (pure managed) | ~0.1 ms | (included below) |
| Cold first call | ~13 ms (JIT) | ~0.4 ms | **~55 ms** (dlopen 12 MB + UniFFI init + thread-pool) |
| Warm eval (simple) | ~145 ns | ~456 ns | ~3.6 µs |

Takeaways:
- **Pure managed has the smallest footprint** (35 KB, no native deps) and no
  `dlopen`; the official engine ships ~19 MB of native code and pays a ~55 ms
  cold-start on the first call.
- The manual native engine is a useful middle ground: 548 KB, sub-ms cold, and
  fast warm — but it still crosses the interop boundary per call.
- Cold numbers are one-shot per fresh process (min of 5 runs); warm is steady-state.

## Language subset

Implements standard-mode Zen: number/string/bool/null/array/object literals,
arithmetic (`+ - * / % ^`), comparisons, `and/or/not` (and `&& || !`), ternary,
`??`, member/index access (with negative indices and optional-chaining), `in` /
`not in` with ranges (`[a..b]`, `(a..b]`, …), and ~30 built-ins including
closures via `#` (`map`, `filter`, `some`, `all`, `sum`, …). Operator precedence
follows the published GoRules table. Precedence is exercised by the parity suite
against the official engine. Out of scope: template/backtick strings, string
slicing, assignment statements, decision-graph (JDM) evaluation.
