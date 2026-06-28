# Zen Expression Language — Managed vs Native (.NET interop) comparison

A from-scratch implementation of the [GoRules ZEN expression language](https://docs.gorules.io/learn/zen-language/syntax)
in two runtimes, plus a benchmark harness that answers the question:

> **Does the raw speed of unmanaged (Rust) code offset the cost of P/Invoke
> interop against a performant pure-managed (C#) implementation?**

## Repository layout

| Path | What |
| --- | --- |
| `src/Zen.Managed/`   | Pure C# implementation of ZEN (lexer, Pratt parser, evaluator, built-ins). **The library.** |
| `native/zen-native/` | Rust `cdylib` implementing the *same* language subset, exposed via a C ABI. Includes a counting global allocator so native heap use is measurable. |
| `src/Zen.Interop/`   | Thin C# P/Invoke wrapper over `libzen_native.so`. |
| `src/Zen.Tests/`     | xUnit parity tests: asserts managed and native produce identical results across simple/complex expressions × few/many parameters. |
| `src/Zen.Benchmarks/`| BenchmarkDotNet harness comparing the two runtimes (parse / eval / end-to-end) and measuring managed GC allocations **and** native heap bytes. |
| `docker/Dockerfile`  | Multi-stage build (Rust + .NET 8 SDK). The canonical build/test/bench environment. |

## Build & run (Docker only)

```bash
docker build -t zen-dev -f docker/Dockerfile .

# iterate on C# without rebuilding the image (source is bind-mounted):
docker run --rm -v "$PWD":/work -w /work zen-dev dotnet build -c Release
docker run --rm -v "$PWD":/work -w /work zen-dev dotnet test  src/Zen.Tests/Zen.Tests.csproj -c Release
docker run --rm -v "$PWD":/work -w /work zen-dev dotnet run   -c Release --project src/Zen.Benchmarks
```

The native library is baked into the image at `/opt/zen/libzen_native.so`
(path advertised via `$ZEN_NATIVE_LIB`).

## Status

Implementation in progress — see the git log for incremental, validated changes.
Final results and analysis land in this README once the benchmark suite runs.
