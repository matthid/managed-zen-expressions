# Zen Expression Language — Managed vs Native (.NET interop) comparison

A from-scratch implementation of the [GoRules ZEN expression language](https://docs.gorules.io/learn/zen-language/syntax)
evaluated **three ways**, benchmarked to answer one question:

> **Does the raw speed of unmanaged (Rust) code offset the cost of .NET interop
> against a genuinely performant pure-managed (C#) implementation?**

The README is in two parts:

- **[Part 1 — Initial implementation](#part-1--initial-implementation-managed-vs-native-benchmarks):**
  the original managed-vs-native benchmark study (throughput, parse, interop, memory, charts, analysis).
- **[Part 2 — Variants & features](#part-2--variants--features):** everything added
  on top — the three engine variants, deterministic resource limits (+ an
  enforcement-overhead optimization), cold-start/footprint overhead, strict-compat
  mode, and test coverage.

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

## Repository layout

```
src/Zen.Managed/      Pure C# engine (the library)
native/zen-native/    Rust cdylib (manual native engine + counting allocator)
src/Zen.Interop/      P/Invoke wrapper over libzen_native
src/Zen.Gorules/      Adapter over the official GoRules.Zen NuGet package
src/Zen.Tests/        xUnit parity + limits (301 tests) — all green
src/Zen.Benchmarks/   BenchmarkDotNet suite + standalone --mem / --overhead / --probe reports
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

---

# Part 1 — Initial implementation: managed vs native benchmarks

The original study: the same Zen subset evaluated three ways, measured on
throughput, parse, interop and memory across simple/complex × few/many
parameters, plus allocating (data-reshaping) expressions for fairness.

## Detailed results

Hardware: AMD Ryzen 9 5900X, .NET 8.0.28, Linux container. Full output:
[`results/bench-full.txt`](results/bench-full.txt). 7 scenarios: the first four are
scalar-producing (simple/complex × few/many); the last three are **allocating /
data-reshaping** (`map` → array, object literal, string building). Charts are
generated by `python3 scripts/generate_charts.py`; figures vary a few % run-to-run.

### Evaluation throughput (lower is better)

![Evaluation throughput — compile-once / evaluate-many (pre-parsed context)](docs/charts/eval-pure.svg)

![Evaluation throughput — JSON context per call](docs/charts/eval-json.svg)

| Scenario | Managed pure | Native pure (manual) | GoRules (official) | Managed JSON | Native JSON (manual) | GoRules JSON |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| simple-few    | **148 ns** | 457 ns (3.1×) | 3 571 ns (24×) | 590 ns | 926 ns | 4 328 ns |
| complex-few   | **306 ns** | 558 ns (1.8×) | 6 436 ns (21×) | 989 ns | 1 280 ns | 6 559 ns |
| simple-many   | **2 340 ns** | 2 478 ns (1.1×) | 59 053 ns (25×) | 7 899 ns | 10 750 ns | 71 958 ns |
| complex-many  | **2 163 ns** | 2 183 ns (1.0×) | 67 934 ns (31×) | 4 510 ns | 5 360 ns | 71 742 ns |
| alloc-string  | **306 ns** | 969 ns (3.2×) | 4 356 ns (14×) | 593 ns | 1 462 ns | 5 460 ns |
| alloc-object  | **548 ns** | 1 871 ns (3.4×) | 9 804 ns (18×) | 1 015 ns | 2 291 ns | 27 439 ns |
| alloc-array   | **1 943 ns** | 5 227 ns (2.7×) | 25 382 ns (13×) | 3 384 ns | 6 123 ns | 18 905 ns |

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

#### Fairness: how each engine ingests context

The two paths measure two realistic regimes, and the cost each engine pays is a
real capability difference, not a measurement artifact:

- **JSON-string path (`*_Json`)** — all three start from the *same raw JSON string*
  and must parse it. Managed parses once (System.Text.Json → `ZenValue`); the manual
  native binding takes raw bytes and parses once (serde); the **official GoRules
  binding takes a context *object* and `JsonSerializer.Serialize`s it on every call**
  (confirmed in its source) — so from a JSON string it pays parse + serialize + native
  parse. Same input; the difference is each engine's real API cost.
- **Pre-parsed path (`*_Pure`)** — managed and manual-native can **cache a parsed
  context** (a `ZenValue` / a native context handle) and evaluate with **no per-call
  JSON**. The official GoRules binding **cannot** — it serializes the context object
  every call (no reusable-context API), so `GoRules_Pure` still pays serialization.

This is the whole point of the comparison: a managed engine can hold the context as
a **live in-process object** and skip serialization entirely, which native engines
structurally cannot — they must serialize any object to cross the boundary.

### Parse / compile (lower is better)

![Parse / compile throughput (source text → compiled)](docs/charts/parse.svg)

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

![Isolated P/Invoke overhead](docs/charts/interop.svg)

| Method | Mean |
| --- | ---: |
| Managed add (inlined) | ~0 ns |
| Native `zen_add` (one P/Invoke) | **6.8 ns** |

So the boundary itself costs ~7 ns. That is *not* what slows the native engines down.

### Memory — and the native-heap blind spot (per op)

BenchmarkDotNet's `Allocated` column is **GC-heap only**. It makes the native
engines look nearly allocation-free, which is misleading. The `--mem` report uses
the instrumented allocator in the manual native lib to show the *real* picture:

![Pure-eval memory — managed is 0 only for scalar expressions](docs/charts/memory-pure.svg)

![JSON-eval memory — the native-heap blind spot](docs/charts/memory-json.svg)

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

## Heavy load: where native *does* beat managed

The chart trend is real — native catches up as work grows. A dedicated `HeavyBench`
pushes it: large arrays (1 000 elements), a 200-term arithmetic expression, and
map/filter over big result sets. (GoRules stays slow throughout — the crossover is
between **managed and manual-native**.)

![Heavy load — pure-eval (pre-parsed context)](docs/charts/heavy-pure.svg)

![Heavy load — JSON context per call](docs/charts/heavy-json.svg)

| Scenario (managed vs native) | Managed pure | Native pure | Managed JSON | Native JSON |
| --- | ---: | ---: | ---: | ---: |
| heavy-sum-1k (sum of 1 000, scalar result) | 3.5 µs | **2.95 µs** ✅ | 55.9 µs | **26.6 µs** ✅ |
| heavy-arith-200 (200-term arithmetic, scalar) | **9.6 µs** | 9.5 µs | **31.7 µs** | 57.8 µs |
| heavy-filter-1k (~500-element result) | **59.5 µs** | 108.8 µs | **107.3 µs** | 131.2 µs |
| heavy-map-1k (1 000-element result) | **79.2 µs** | 194.8 µs | **135.3 µs** | 222.0 µs |
| heavy-map-objects-100 (100 objects result) | **33.5 µs** | 94.7 µs | **58.1 µs** | 134.5 µs |

The finding is the **opposite** of an allocation-driven crossover:

- **Native wins only on `heavy-sum-1k`** — heavy *compute* with a **scalar**
  result. The cheap marshal (one number back) lets native's raw eval speed show
  (pure: ~16% faster), and on the JSON path native is still **~2.1× faster** even
  after the managed JSON reader was optimized.
- **Managed wins every allocation-heavy scenario** (large result arrays/objects):
  native must marshal the whole result back across the boundary, which dominates.
  So *more allocations make managed's lead bigger, not smaller*.
- **JSON reader optimized (3 iterations):** `ZenJson.Parse` went from `JsonDocument`
  → `Utf8JsonReader`+`ArrayPool` → a **UTF-16 `ReadOnlySpan<char>` parser** (reads
  the input string directly — no UTF-8 encode step, since `Utf8JsonReader` is
  UTF-8-only — and single-pass). Each step sped up **every** JSON path: standard
  scenarios ~25–45% faster overall, and `heavy-sum-1k` JSON **84 → 56 µs**. Native's
  `serde` still parses the 1 000-element array faster (≈27 µs); the remaining gap
  is `serde`'s maturity on bulk number
  tokenization, which is hard to close further without a custom UTF-8 number reader.

So: **native overtakes managed on compute-bound work with small results** (e.g.
`sum`/`reduce` over large inputs), and **managed dominates allocation-bound work**
and any path where the result must be marshalled. ([`results/heavy-bench.txt`](results/heavy-bench.txt))

---

# Part 2 — Variants & features

Everything added on top of the initial benchmark study.

## Engine variants

The comparison runs the **same Zen subset** through three engines (see the table
at the top):

- **`Zen.Managed`** — pure C#; the library under test. Zero native deps, zero
  GC alloc on the scalar hot path.
- **`GoRules.Zen`** (official NuGet) — **the library you'd actually ship**: the
  real-world "unmanaged engine via interop" path (native Rust via UniFFI). Used
  as-is, unmodified. It appears in *every* chart because it's the production
  choice this study is really weighing managed against.
- **`Zen.Native` + `Zen.Interop`** — a *reference* manual Rust `cdylib` + P/Invoke
  wrapper (not a published package). It has a lean raw-bytes C ABI and a **counting
  global allocator** (so its native heap is measurable — the thing .NET metrics
  cannot see). It's included to isolate "native eval speed + minimal interop" —
  i.e. how fast native *could* be with an ideal binding — which is why it beats the
  official library on every cell.

All three are exercised by the same `Scenarios` matrix (simple/complex × few/many
+ allocating) and the same parity battery.

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
| `MaxSteps` | array elements processed by iterating ops (`sum`/`avg`/`min`/`max`/`map`/`filter`/`some`/`all`/`in`) | 1,000,000 | 1,000 |
| `MaxAllocations` | structural allocs (arrays/objects/strings) | 1,000,000 | 10,000 |
| `MaxBytes` | estimated allocated bytes | 256 MB | 4 MB |
| `MaxSourceLength` | source text (parse guard) | 1 MB | 64 KB |
| `MaxParseDepth` | parser recursion depth | 1,000 | 200 |

- **Opt-in for eval:** `Evaluate(ctx)` enforces nothing (the fast path the
  benchmarks measure); pass a `ZenLimits` to sandbox untrusted input. Parse guards
  are on by default (cheap). When off, the guards add **no measurable overhead**.
- **Near-zero enforcement overhead:** `MaxSteps` is charged **O(1) up-front** at
  array-iterating ops, not per AST node; static work is bounded by
  `MaxSourceLength` at parse time. So non-iterative expressions pay nothing to
  enforce limits. Measured (`LimitsBench`, `Evaluate` off vs on):

  ![Resource-limit enforcement overhead — Evaluate off vs on](docs/charts/limits-overhead.svg)

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
  to within noise.) Limits are **managed-only** — the native path is just a lib
  call, and you cannot budget someone else's compiled `.so` from the outside.
- **DoS-safe:** the language has no user-defined recursion, so there are no
  infinite loops; iterating operations are bounded by `MaxSteps`, allocations by
  `MaxAllocations`/`MaxBytes`. A hostile expression or huge input array aborts
  deterministically instead of hanging or OOMing.
- Aborts throw `ZenLimitException`. Covered by `LimitTests`.

## Cold-start & footprint overhead

The per-op benchmarks in Part 1 measure the steady-state hot path. The
`--overhead` report measures the fixed cost you pay once:

![Binary footprint — what you ship per engine](docs/charts/footprint.svg)

![Cold first call (fresh process: lib load + JIT + first eval)](docs/charts/cold-start.svg)

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

## Strict-compat mode

`Zen.Tests/GorulesParityTests` compares our engine against the **official GoRules**
reference on the full case battery. Compat is controlled by `ZEN_STRICT_COMPAT`:

- **Off (default):** if GoRules rejects an expression our engine accepts, the case
  is soft-skipped — so extending the language beyond the reference does not fail
  the suite.
- **On (`ZEN_STRICT_COMPAT=1`):** a GoRules rejection **fails** unless the case is
  on the `KnownSupersetCases` allowlist. This catches *unexpected* regressions on
  the shared subset while the language grows.

Strict mode surfaced 6 features our engine extends beyond GoRules (now
allowlisted): `concat()`, negative indexing (`items[-1]`), string relational
comparison (`'a' < 'b'`), `replace()`, `substring()`, and `'needle' in 'haystack'`.

## Test coverage

301 tests, green in both default and strict modes:

- **`ParityTests`** — managed ↔ manual-native agree on the full battery
  (simple/complex × few/many + allocating), plus the managed pure vs JSON paths.
- **`GorulesParityTests`** — managed ↔ official GoRules reference (see strict mode above).
- **`LimitTests`** — step/allocation/byte/parse-depth/parse-length budgets abort
  deterministically, including a hostile-input case and a deterministic-repeat check.

## Language subset

Implements standard-mode Zen: number/string/bool/null/array/object literals,
arithmetic (`+ - * / % ^`), comparisons, `and/or/not` (and `&& || !`), ternary,
`??`, member/index access (with negative indices and optional-chaining), `in` /
`not in` with ranges (`[a..b]`, `(a..b]`, …), and ~30 built-ins including
closures via `#` (`map`, `filter`, `some`, `all`, `sum`, …). Operator precedence
follows the published GoRules table. Precedence is exercised by the parity suite
against the official engine. Out of scope: template/backtick strings, string
slicing, assignment statements, decision-graph (JDM) evaluation.
