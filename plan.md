# Development plan

This document captures the direction of **CSharpCoverage** — a Roslyn-based
statement / decision / MCDC coverage tool for C#. It covers what is in the
tree today, what's coming next, and the constraints the project chooses to
live with.

## 1. Vision

A self-contained, cross-platform coverage analyzer for C# that:

- Measures **statement**, **decision (branch)**, and **MCDC**
  (unique-cause + masking, DO-178C) on the same run.
- Works on `.cs`, `.csproj`, or `.sln` inputs without modifying the user's
  source tree.
- Produces a human-readable HTML report *and* machine-readable JSON for CI.
- Builds and runs under any currently-deployed .NET SDK (6, 8, 10).
- Has zero runtime dependencies beyond the .NET BCL.

Non-goals: IDE integration, language-server protocol, multi-language
(F# / VB) coverage, line-rate dashboards.

## 2. Status snapshot

| Area                                 | State          |
|--------------------------------------|----------------|
| Roslyn rewriter (stmt/dec/cond)      | Done           |
| Shadow build pipeline                | Done           |
| Runtime probe API + JSON flush       | Done           |
| MCDC analyzer (unique-cause + mask.) | Done           |
| HTML / text / JSON reporters         | Done (HTML ok) |
| CLI (`instrument` / `report` / `analyze`) | Done     |
| `--include` / `--exclude` file filters | Done         |
| Multi-SDK (net6.0 baseline + RollForward) | Done      |
| Line-number preservation through rewriter reparenting | Done |
| CalculatorDemo end-to-end verification | 56 tests, 98.6/90.9/88.0 |
| Core.Tests unit suite                | **Stub** — project exists, no tests |
| WPF manual smoke on CalculatorDemo / PhotoViewerDemo | Manual only |
| CI                                   | Not set up     |
| Cobertura / OpenCover XML export     | Not started    |
| IDE integration                      | Out of scope   |

Branch of record: `main`. Feature development happens on
`claude/csharp-coverage-analyzer-*` branches and merges into main via
revert-the-revert or merge commits as appropriate.

## 3. Architecture

```
CSharpCoverage.sln
├── src/
│   ├── CSharpCoverage.Runtime/      netstandard2.0 — probes + JSON flush
│   ├── CSharpCoverage.Core/         net6.0        — rewriter, map, MCDC
│   ├── CSharpCoverage.Report/       net6.0        — HTML/text/JSON rendering
│   └── CSharpCoverage.Cli/          net6.0 exe    — `coverage` verbs
├── tests/
│   ├── CSharpCoverage.Core.Tests/   unit tests (stub)
│   ├── CalculatorDemo.Coverable/    decision logic extracted from WPF MainWindow
│   └── CalculatorDemo.Tests/        xUnit driving Coverable (56 tests)
├── CalculatorDemo/                  WPF sample (net10.0-windows)
└── PhotoViewerDemo/                 WPF sample (net10.0-windows)
```

Data flow:

```
source tree ──► shadow copy ──► rewriter ──► annotated .cs + originals/
                                              │
                                              ▼
                                    Directory.Build.props
                                              │
                                              ▼
                                        dotnet build
                                              │
                                              ▼
                                 driver (dotnet test / exe / ...)
                                              │
                                              ▼   (on ProcessExit)
                                         coverage.json
                                              │
                       coverage.map.json  ◄───┤
                                              ▼
                                       MCDCAnalyzer
                                              │
                                              ▼
                            HTML / text / JSON report in ./report/
```

Key design choices (already implemented, worth preserving):

- **Shadow-copy rewriting.** The user's tree is never modified. Each shadow
  is keyed by a hash of the target path.
- **Leaf wrapping, not operator rewriting.** `Cond(fid, did, idx, leaf)`
  wraps each atomic condition, letting C#'s native short-circuit leave
  un-evaluated leaves recorded as *not-evaluated* tri-state.
- **`Directory.Build.props` injection.** The shadow gets a generated
  props file that references `CSharpCoverage.Runtime.dll`; no source
  `.csproj` is edited.
- **`originals/` sidecar.** Un-instrumented source is stashed under
  `<shadow>/originals/<relpath>`, and `FileInfoEntry.Absolute` points
  there, so HTML reports render clean C# (not probe-injected code)
  even after the source tree changes.
- **Pre-pass line annotation.** A `SourceLocationAnnotator` stamps every
  node with its real `(line, column)` via `SyntaxAnnotation`, so record
  sites keep the correct line when the rewriter reparents a node into a
  synthesized block.

## 4. Roadmap

### Milestone M1 — Hardening the current pipeline (next 1–2 weeks)

- [ ] **Populate `CSharpCoverage.Core.Tests`.** The project exists as a stub;
      it needs real unit tests:
      - Rewriter snapshot tests on synthetic snippets (if / while / switch /
        ternary / `??` / compound boolean / nested / async / yield).
      - `SourceLocationAnnotator` round-trip test: parse a snippet, rewrite,
        assert recorded lines match the original.
      - `MCDCAnalyzer` tests against canonical DO-178C truth-table fixtures
        (2-, 3-, and 4-condition AND / OR / mixed).
- [ ] **CI.** GitHub Actions workflow matrix on SDK 6, 8, 10 × Linux /
      Windows. Build, test, run the full `analyze` pipeline on
      `CalculatorDemo.Coverable`, assert coverage >= thresholds.
- [ ] **Coverage regression guard.** Persist `summary.json` from CI and
      fail the build if any metric drops more than 2 pp from the last
      merged main.

### Milestone M2 — CI-friendly output formats

- [ ] **Cobertura XML** exporter (`--format cobertura`). Covers the case
      where downstream tooling (Azure DevOps, GitLab, Codecov) expects it.
- [ ] **LCOV** exporter (`--format lcov`). Smallest widely-accepted format.
- [ ] **JUnit-style XML summary** for CI dashboards that group by metric.
- [ ] **`--baseline <file>` / `--diff`** produce a diff report against a
      previous `coverage.json`, highlighting lines that regressed.
- [ ] **`--threshold` per metric**, e.g. `--threshold stmt=80 dec=70 mcdc=60`
      (today's `--threshold N` applies to the minimum of all three).

### Milestone M3 — Roslyn coverage of the long tail

The rewriter is good on mainstream C# but has known gaps. Each item below
is a small rewriter feature with paired tests.

- [ ] **Async / await.** Probes inside `async` methods work today; verify
      `await using`, `await foreach`, async streams (`yield return` inside
      `IAsyncEnumerable`) are instrumented correctly.
- [ ] **Pattern matching.** `is` patterns, switch-expression arms with
      `when` clauses, list patterns, property patterns, relational
      patterns. Each arm should be a decision.
- [ ] **Records.** Primary-constructor parameter initializers should be
      probed; synthesized `Equals`/`GetHashCode` should be skipped, not
      counted as missed.
- [ ] **Collection expressions.** `[a, b, ..rest]` should not confuse the
      rewriter; no probes inside the collection literal itself.
- [ ] **Local functions.** Already handled, but add a test covering
      captured locals (which change Roslyn's expansion shape).
- [ ] **Expression-bodied members.** Already expanded to
      `{ return expr; }`; regression-test lambdas and switch-expression
      arms that pass through the expander.
- [ ] **Expression trees.** Detect lambda-to-`Expression<T>` via
      `SemanticModel` and skip instrumentation inside — wrapping leaves
      with method calls breaks LINQ providers. Not in place today.

### Milestone M4 — Performance & scale

- [ ] **Benchmark.** Add a `bench/` project that times instrumented vs
      un-instrumented runs of a compute-heavy loop. Goal: probe overhead
      under 10 % on straight-line code (today ~30 %).
- [ ] **Reduce probe cost.** `Stmt` / `Branch` / `Cond` go through a
      `ConcurrentDictionary`. For single-threaded drivers (the common
      case) a per-thread bitset + batched merge should cut overhead
      significantly.
- [ ] **Skip probes for trivially-straight-line methods** behind a flag —
      some users just want decision/MCDC coverage and don't need per-stmt.
- [ ] **Shadow caching.** If source hasn't changed since the last
      `instrument`, skip the rewrite. Keep a manifest keyed by file hash.

### Milestone M5 — Ergonomics

- [ ] **`coverage init` command.** Drop a `coverage.config.json` with the
      project's include globs / thresholds / driver command, so day-to-day
      use is just `coverage run`.
- [ ] **`coverage watch`.** Re-runs when source files change — useful for
      local dev.
- [ ] **HTML polish.** Sortable table, dark mode, per-folder rollup.
- [ ] **Readable decision IDs in the HTML annotations.** Today it's
      "decision #35"; prefer "line 96: `if (EraseDisplay)`".
- [ ] **Source-map support** for generators. When source generators
      emit a `#line` directive, map probes back to the generator input.

### Milestone M6 — Edge cases and correctness

- [ ] **Incremental correctness audit.** Run the tool against 5–10 real
      OSS projects, pick up any rewriter crash or build-break, add a
      regression fixture, fix.
- [ ] **Stress test concurrency.** Parallel tests writing to the observation
      buffer. Verify thread-local flush + `ConcurrentDictionary` merge give
      correct per-decision counts.
- [ ] **Flush on crash.** Today we rely on `AppDomain.ProcessExit`. If
      the process segfaults or is `kill -9`'d, no data is written. Add a
      periodic flush option (`--flush-every <sec>`).
- [ ] **Windows path handling.** All path comparisons use
      `StringComparison.OrdinalIgnoreCase`; extend the Core.Tests suite
      to drive the same scenarios on a Windows runner via CI.

## 5. Testing strategy

Three tiers:

1. **Unit tests** — `CSharpCoverage.Core.Tests`. Exercise the rewriter and
   MCDC analyzer against synthetic inputs. These must run headless,
   fast, and deterministically on all three target SDKs.
2. **End-to-end tests** — `CalculatorDemo.Coverable` + `.Tests`. Exercises
   the full pipeline (instrument → build → drive → report) on a small
   but decision-dense body of code. Asserts 56 tests pass and the
   summary matches `98.6 / 90.9 / 88.0`. Runs in CI on Linux.
3. **Manual WPF smoke** — `CalculatorDemo` and `PhotoViewerDemo`.
   Cannot be driven headlessly. Instructions live in the README;
   release checklist runs this once per Windows release.

Any bug fix has to land with at least a tier-1 regression test.

## 6. CLI surface (current and stable)

```
coverage instrument <target>
    [--output <dir>] [--runtime <path>]
    [--include <glob>]... [--include-from <file>]
    [--exclude <glob>]... [--exclude-from <file>] [--verbose]

coverage report
    --data <coverage.json> --map <coverage.map.json>
    [--output <dir>] [--mcdc unique-cause|masking]
    [--format html|text|json]... [--threshold <pct>]

coverage analyze
    --project <csproj|sln> --driver "<shell>"
    [--test-project <relpath>] [--source-root <dir>]
    [--include <glob>]... [--include-from <file>]
    [--exclude <glob>]... [--exclude-from <file>]
    [--output <dir>] [--mcdc unique-cause|masking]
```

Stability contract:

- Flag names listed above are frozen. Renaming requires a deprecation
  cycle (accept old name, warn, remove two releases later).
- Exit codes are frozen: `0` success, `1` usage, `2` unhandled,
  `3` threshold breach, `4` no data produced.
- New flags are added only; removing one is a breaking change.

## 7. Release plan

Semantic versioning. Pre-1.0 the API is still fluid; 1.0 ships once:

- M1 (hardening + CI) is done.
- At least one M2 item (Cobertura or LCOV) is in.
- 2+ real projects outside this repo have run the tool to completion
  without hand-holding.

Release cadence: ad-hoc until 1.0. Post-1.0, monthly on the last
working Friday unless there's nothing material to ship.

## 8. Known limitations (deliberate; documented in README)

These are trade-offs, not bugs:

- Loose `.cs` input is wrapped in a synthetic `net6.0` library — no
  runnable coverage without a user-supplied driver.
- Source generators that key off unmodified source may produce slightly
  different output post-rewrite.
- Records' synthesized equality / primary-constructor code is not
  covered — only authored source is.
- Strict unique-cause MCDC is unreachable for decisions whose
  conditions are joined by short-circuit `||` / `&&` at the same
  nesting level; the report annotates these per decision.
- Probe overhead in hot loops is ~30 % today (see M4).

## 9. Contributing workflow

1. Branch from `main` with a descriptive name.
2. Make changes with paired tests (tier 1 at minimum).
3. Run `dotnet build CSharpCoverage.sln` and the end-to-end verification
   locally:
   ```
   dotnet run --project src/CSharpCoverage.Cli -- analyze \
     --project tests/CalculatorDemo.Coverable/CalculatorDemo.Coverable.csproj \
     --driver "dotnet test tests/CalculatorDemo.Tests/CalculatorDemo.Tests.csproj" \
     --output ./report
   ```
   Expect `98.6 / 90.9 / 88.0` (± the effect of your change).
4. Update `README.md` if you touched the CLI surface or behavior.
5. PR against `main`. Include the before/after coverage summary.
