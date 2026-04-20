# C# Coverage — Statement / Decision / MCDC

A Roslyn-based source-rewriting coverage tool for C#. It measures three
criteria on the same run:

- **Statement coverage** — did each executable statement run?
- **Decision (branch) coverage** — did each boolean decision take both the
  true and the false outcome at least once?
- **MCDC (Modified Condition / Decision Coverage)** — for every atomic
  condition inside a compound boolean, is there a pair of observations that
  proves that condition independently affected the decision outcome? Both
  **unique-cause** and **masking** variants (DO-178C) are implemented and
  selectable from the CLI.

The tool accepts any of three input shapes:

| Input       | What happens                                                                 |
|-------------|------------------------------------------------------------------------------|
| `.cs` file  | Wrapped in a synthetic `net8.0` class library, then instrumented.            |
| `.csproj`   | That project (and the enclosing solution tree) is mirrored and instrumented. |
| `.sln`      | Every `.csproj` referenced from the solution is instrumented.                |

Output is an HTML report with per-line hit/miss coloring, per-decision
truth-table pages, and `summary.txt` / `summary.json` for CI consumption.

## How it works

1. **Shadow copy.** The target's enclosing tree is mirrored into
   `_coverage_shadow/<hash>/src/`. The original source is never modified.
2. **Rewrite.** A Roslyn `CSharpSyntaxRewriter` injects probe calls:
   - `CoverageRuntime.Stmt(fid, sid)` before each statement
   - `CoverageRuntime.Branch(fid, did, expr)` wrapping every decision
   - `CoverageRuntime.Cond(fid, did, idx, leaf)` wrapping every atomic
     condition (leaves of `&&` / `||` / `!`). C#'s native short-circuit is
     preserved — `Cond` is only invoked when the leaf actually evaluates, so
     masked conditions naturally appear as "not evaluated" in observations.
   - `CoverageRuntime.Case(fid, did, caseIdx)` as the first statement of
     each `switch` section.
3. **Runtime reference.** A `Directory.Build.props` is dropped at the shadow
   root that references `CSharpCoverage.Runtime.dll`, so no source
   `.csproj` edits are needed.
4. **Build & drive.** `dotnet build` compiles the shadow; a user-supplied
   driver (typically `dotnet test`) exercises the instrumented code.
5. **Flush.** The runtime serializes `coverage.json` to the path in
   `COVERAGE_OUTPUT` on `AppDomain.ProcessExit`.
6. **Report.** The analyzer loads `coverage.json` + `coverage.map.json`,
   computes MCDC, and renders HTML/text/JSON.

## Repository layout

```
CSharpCoverage.sln
src/
  CSharpCoverage.Runtime/   netstandard2.0 — probe API, observation buffer, flush
  CSharpCoverage.Core/      net8.0 — Roslyn rewriter, map, MCDC analyzer
  CSharpCoverage.Report/    net8.0 — HTML / text / JSON renderers
  CSharpCoverage.Cli/       net8.0 console — `coverage instrument|report|analyze`
tests/
  CSharpCoverage.Core.Tests/    unit tests
  CalculatorDemo.Coverable/     decision logic extracted from WPF MainWindow
  CalculatorDemo.Tests/         xUnit tests driving Coverable (56 tests)
CalculatorDemo/                 original WPF sample (net10.0-windows)
```

## Installation

### Prerequisites

- **.NET SDK 8.0** or newer. Verify with `dotnet --info`.
  - Linux (Debian / Ubuntu): `sudo apt-get install -y dotnet-sdk-8.0`
  - macOS (Homebrew): `brew install --cask dotnet-sdk`
  - Windows: install from <https://dotnet.microsoft.com/download/dotnet/8.0>
- For the manual WPF smoke on the original `CalculatorDemo`, you also need
  Windows and the `.NET 10 Desktop Runtime` (WPF workload). Everything else
  is cross-platform.

### Clone and build

```
git clone <this-repo-url> c_sharp_coverage
cd c_sharp_coverage
dotnet build CSharpCoverage.sln -c Debug
```

The CLI is produced at
`src/CSharpCoverage.Cli/bin/Debug/net8.0/coverage.dll`.

### Optional: install as a local tool

```
dotnet publish src/CSharpCoverage.Cli/CSharpCoverage.Cli.csproj -c Release -o ./dist
# then invoke as:
dotnet ./dist/coverage.dll <verb> ...
```

For convenience on Linux / macOS you can add a shell alias:

```
alias coverage='dotnet /absolute/path/to/dist/coverage.dll'
```

On Windows PowerShell:

```
function coverage { dotnet "C:\path\to\dist\coverage.dll" $args }
```

All command examples below assume `coverage` is available that way. When
running straight from source use `dotnet run --project src/CSharpCoverage.Cli/CSharpCoverage.Cli.csproj --`
instead.

## CLI reference

```
coverage instrument <target>
    [--output <dir>] [--runtime <path>] [--exclude <glob>]... [--verbose]

coverage report
    --data <coverage.json> --map <coverage.map.json>
    [--output <dir>] [--mcdc unique-cause|masking]
    [--format html|text|json]... [--threshold <pct>]

coverage analyze
    --project <csproj|sln> --driver "<shell>"
    [--test-project <relpath>] [--source-root <dir>]
    [--output <dir>] [--mcdc unique-cause|masking]
```

| Flag               | Applies to      | Meaning                                                                                        |
|--------------------|-----------------|------------------------------------------------------------------------------------------------|
| `--output`         | all             | Output directory (shadow root for `instrument`, report dir for the others). Default per verb.  |
| `--runtime`        | instrument      | Path to `CSharpCoverage.Runtime.dll`. Auto-discovered from the CLI's base dir by default.      |
| `--exclude <glob>` | instrument      | Skip rewriting files matching the glob. Repeatable.                                            |
| `--verbose`        | instrument      | Stream MSBuild output.                                                                         |
| `--data`           | report          | Path to `coverage.json` produced by an instrumented run.                                       |
| `--map`            | report          | Path to `coverage.map.json` produced at instrument time.                                       |
| `--mcdc`           | report, analyze | `unique-cause` or `masking` (default `masking`).                                               |
| `--format`         | report          | `html`, `text`, `json`. Repeatable; default `html text`.                                       |
| `--threshold <N>`  | report          | Exit code 3 if the lowest metric falls below `N` %.                                            |
| `--project`        | analyze         | Target `.csproj` or `.sln`.                                                                    |
| `--driver`         | analyze         | Shell command that exercises the instrumented code (typically `dotnet test`).                  |
| `--test-project`   | analyze         | Relative path (from source root) to the test csproj; driver cwd is that project's directory.   |
| `--source-root`    | analyze         | Override the auto-detected mirror root (defaults to the enclosing solution directory).         |

Exit codes: `0` success, `1` usage error, `2` unhandled exception, `3`
threshold breach, `4` driver ran but produced no `coverage.json`.

## Run procedures

All three verbs can be chained manually (`instrument` → build → driver →
`report`), or you can use `analyze` to do all of it in one shot.

### 1. One-shot `analyze` (recommended)

For a project that already has an xUnit test project referencing it.

```
coverage analyze \
  --project      path/to/MyLibrary/MyLibrary.csproj \
  --test-project path/to/MyLibrary.Tests/MyLibrary.Tests.csproj \
  --driver       "dotnet test" \
  --output       ./coverage-report \
  --mcdc         masking
```

After it finishes, open `./coverage-report/index.html`.

### 2. Instrument + build + run + report (manual)

Useful when the driver isn't `dotnet test`, e.g. a console app, an
integration harness, or a manual smoke run.

```
# (a) Instrument
coverage instrument path/to/MyApp/MyApp.csproj --output ./_shadow

# (b) Build the shadow
dotnet build ./_shadow/src/path/to/MyApp/MyApp.csproj -c Debug

# (c) Drive the instrumented binary
export COVERAGE_OUTPUT=$(pwd)/coverage.json   # Linux/macOS
# set COVERAGE_OUTPUT=%cd%\coverage.json      # Windows cmd
dotnet ./_shadow/src/path/to/MyApp/bin/Debug/net8.0/MyApp.dll  # or run the tests etc.

# (d) Render the report
coverage report \
  --data ./coverage.json \
  --map  ./_shadow/coverage.map.json \
  --output ./coverage-report \
  --mcdc masking \
  --format html --format text --format json
```

`COVERAGE_OUTPUT` defaults to `./coverage.json` in the process's working
directory if unset.

### 3. Single `.cs` file

```
coverage instrument path/to/Foo.cs --output ./_shadow
dotnet build ./_shadow/src/_CoverageTarget.csproj
# write a tiny driver that `new Foo().DoThing()` and run it
```

### 4. Whole solution

Point `--project` at a `.sln` and `analyze` picks up every csproj:

```
coverage analyze \
  --project      MyProduct.sln \
  --test-project tests/MyProduct.Tests/MyProduct.Tests.csproj \
  --driver       "dotnet test" \
  --output       ./coverage-report
```

### 5. CI threshold gate

```
coverage report --data coverage.json --map _shadow/coverage.map.json \
                --output ./coverage-report --threshold 80
echo $?   # 3 if any metric dropped below 80 %
```

## Verification on the bundled sample

`CalculatorDemo/` is a WPF calculator targeting `net10.0-windows`. Its
decision-heavy logic is also extracted line-for-line into
`tests/CalculatorDemo.Coverable/` behind an `ICalculatorIo` seam so it can
be exercised headlessly by the xUnit project `tests/CalculatorDemo.Tests/`.

From the repo root:

```
dotnet run --project src/CSharpCoverage.Cli/CSharpCoverage.Cli.csproj -- analyze \
  --project      tests/CalculatorDemo.Coverable/CalculatorDemo.Coverable.csproj \
  --test-project tests/CalculatorDemo.Tests/CalculatorDemo.Tests.csproj \
  --driver       "dotnet test" \
  --output       ./coverage-report \
  --mcdc         masking
```

Current results on the 56-test suite:

| Metric               | Value   |
|----------------------|---------|
| Statement coverage   | 98.6 %  |
| Decision coverage    | 90.9 %  |
| Masking MCDC         | 88.0 %  |

Unique-cause MCDC on `OnWindowKeyDown`'s 4-condition OR chain is partial —
short-circuit evaluation means some condition pairs cannot be constructed
under strict unique-cause rules. The report flags these cases per decision
rather than silently failing.

## Manual WPF smoke (Windows only)

The original WPF `CalculatorDemo` cannot be driven headlessly. To collect
coverage on it interactively:

```
coverage instrument CalculatorDemo\CalculatorDemo.csproj --output .\_shadow
dotnet build .\_shadow\src\CalculatorDemo\CalculatorDemo.csproj -c Debug
set COVERAGE_OUTPUT=%cd%\coverage.json
.\_shadow\src\CalculatorDemo\bin\Debug\net10.0-windows\CalculatorDemo.exe
REM click digits / operators / memory / sqrt / = / C / CE, then close the window
coverage report --data coverage.json --map .\_shadow\coverage.map.json --output report-wpf
```

The runtime flushes on window close (via `AppDomain.ProcessExit`).

## Report layout

```
<out>/index.html                      sortable per-file totals
<out>/files/<file>.html               source with line classes:
                                        stmt-hit | stmt-miss
                                        br-full  | br-partial | br-none
<out>/decisions/f<fid>_d<did>.html    truth-table per decision:
                                        rows = observations, columns = conditions + outcome,
                                        with MCDC-pair proof annotations
<out>/assets.css                      stylesheet
<out>/summary.txt                     console summary
<out>/summary.json                    machine-readable totals
```

Colors: green = covered, red = missed, yellow = partial decision.

## Troubleshooting

- **`no coverage.json produced`** — the driver didn't actually exercise
  instrumented code, or the process crashed before `ProcessExit`. Verify
  the driver exit code, and confirm `COVERAGE_OUTPUT` points where you
  expect. The runtime also writes on `AppDomain.DomainUnload` as a fallback.
- **Tests load the un-instrumented DLL** — this happens when the test
  project lives outside the shadow. Use `--test-project` so the test
  csproj is mirrored into the shadow alongside the target; its
  `ProjectReference` then resolves to the instrumented copy.
- **MSBuild restore errors about missing runtime reference** — the
  runtime DLL couldn't be located. Pass `--runtime
  /path/to/CSharpCoverage.Runtime.dll` explicitly to `instrument`.
- **Decisions show 0% after instrumentation** — ensure the shadow was
  actually rebuilt after `instrument`; stale `bin/obj` under the shadow
  will silently run old code. `analyze` always rebuilds.

## Known limitations

- Loose `.cs` input is wrapped in a synthetic `net8.0` library — no
  runnable coverage without a driver.
- Source generators keyed off unmodified source may produce slightly
  different output post-rewrite.
- Records' synthesized equality and primary-constructor code is not
  covered (only source statements are).
- Strict unique-cause MCDC is unreachable for decisions whose conditions
  are joined by short-circuit `||` / `&&` at the same nesting level; the
  report flags these per decision rather than failing.
- Probe overhead in hot loops is roughly 30 % slowdown.
