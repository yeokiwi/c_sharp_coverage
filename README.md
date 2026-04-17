# C# Coverage — Statement / Decision / MCDC

A Roslyn-based source-rewriting coverage tool for C# that reports **statement**,
**decision (branch)**, and **MCDC** (both unique-cause and masking) coverage.

Accepts a single `.cs` file, a `.csproj`, or an `.sln` as input. Produces an
HTML report with per-line coloring and per-decision MCDC truth tables.

## Layout

```
CSharpCoverage.sln
src/
  CSharpCoverage.Runtime/   netstandard2.0 — probe API + observation buffer + flush
  CSharpCoverage.Core/      net8.0 — Roslyn rewriter, map, MCDC analyzer
  CSharpCoverage.Report/    net8.0 — HTML / text / json renderers
  CSharpCoverage.Cli/       net8.0 console — `coverage` with `instrument|report|analyze`
tests/
  CSharpCoverage.Core.Tests/      unit tests
  CalculatorDemo.Coverable/       decision logic extracted from WPF MainWindow
  CalculatorDemo.Tests/           xUnit tests driving Coverable
CalculatorDemo/                   the original WPF sample (net10.0-windows)
```

## Build

```
dotnet build CSharpCoverage.sln -c Debug
```

## End-to-end (cross-platform, uses extracted Coverable + xUnit)

```
dotnet run --project src/CSharpCoverage.Cli/CSharpCoverage.Cli.csproj -- analyze \
  --project tests/CalculatorDemo.Coverable/CalculatorDemo.Coverable.csproj \
  --test-project tests/CalculatorDemo.Tests/CalculatorDemo.Tests.csproj \
  --driver "dotnet test" \
  --output ./coverage-report --mcdc masking
```

This mirrors the enclosing solution into `_coverage_shadow/<hash>/src/`, rewrites
`.cs` files under the target project, drops a `Directory.Build.props` that
references `CSharpCoverage.Runtime.dll`, builds the shadow, runs the driver
(`dotnet test` inside the shadow's tests project so `ProjectReference` resolves
to the instrumented DLL), then renders HTML + text + JSON to `./coverage-report/`.

Expected results on the current suite:

| Metric                 | Value        |
|------------------------|--------------|
| Statement coverage     | 98.6 %       |
| Decision coverage      | 90.9 %       |
| Masking MCDC           | 88.0 %       |

Unique-cause MCDC on `OnWindowKeyDown`'s 4-condition OR chain is partial
(short-circuit masking means some pairs cannot be constructed); the report
annotates this per decision.

## Manual WPF smoke (Windows only)

The WPF `CalculatorDemo` targets `net10.0-windows` and cannot be driven
headlessly. To collect coverage interactively on Windows:

```
coverage instrument CalculatorDemo\CalculatorDemo.csproj --output .\_shadow
dotnet build .\_shadow\src\CalculatorDemo\CalculatorDemo.csproj -c Debug
set COVERAGE_OUTPUT=%cd%\coverage.json
.\_shadow\src\CalculatorDemo\bin\Debug\net10.0-windows\CalculatorDemo.exe
REM click digits / operators / memory / sqrt / = / C / CE, then close the window
coverage report --data coverage.json --map .\_shadow\coverage.map.json --output report-wpf
```

`COVERAGE_OUTPUT` controls where the runtime writes `coverage.json`; the runtime
flushes on `AppDomain.ProcessExit` (triggered when the WPF window closes).

## CLI

```
coverage instrument <target> [--output <dir>] [--runtime <path>] [--exclude <glob>]...

coverage report    --data <coverage.json> --map <coverage.map.json>
                   [--output <dir>] [--mcdc unique-cause|masking]
                   [--format html|text|json]... [--threshold <pct>]

coverage analyze   --project <csproj|sln> --driver "<shell>"
                   [--test-project <relpath>] [--source-root <dir>]
                   [--output <dir>] [--mcdc unique-cause|masking]
```

## Known limitations

- Loose `.cs` input is wrapped in a synthetic `net8.0` library — no runnable
  coverage without a project.
- Source generators that key off unmodified source may produce slightly
  different output post-rewrite.
- Records' synthesized equality and primary-constructor code is not covered
  (only source statements are).
- Strict unique-cause MCDC is unreachable for decisions whose conditions are
  joined by short-circuit `||` / `&&` at the same nesting level; the report
  flags these per decision rather than failing.
- Probe overhead in hot loops is roughly 30 % slowdown.
