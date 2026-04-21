using System.Diagnostics;
using CSharpCoverage.Core.Analysis;
using CSharpCoverage.Core.Instrumentation;
using CSharpCoverage.Core.Model;
using CSharpCoverage.Report;

namespace CSharpCoverage.Cli;

internal static class CommandRouter
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h" || args[0] == "help")
            return Help(0);

        try
        {
            return args[0] switch
            {
                "instrument" => Instrument(args[1..]),
                "report" => Report(args[1..]),
                "analyze" => Analyze(args[1..]),
                _ => Help(1)
            };
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 2;
        }
    }

    private static int Help(int code)
    {
        Console.WriteLine(@"coverage — C# statement/decision/MCDC coverage

Usage:
  coverage instrument <target> [--output <dir>] [--runtime <path>]
                      [--include <glob>]... [--include-from <file>]
                      [--exclude <glob>]... [--exclude-from <file>] [--verbose]
  coverage report     --data <json> --map <json> [--output <dir>]
                      [--mcdc unique-cause|masking] [--format html|text|json]...
  coverage analyze    --project <csproj|sln> --driver ""<shell>""
                      [--include <glob>]... [--include-from <file>]
                      [--exclude <glob>]... [--exclude-from <file>]
                      [--output <dir>] [--mcdc unique-cause|masking]

Notes:
  instrument rewrites a shadow copy of <target> (file/csproj/sln) with runtime probes.
  report     consumes coverage.json + coverage.map.json and renders HTML/text.
  analyze    = instrument + dotnet build + driver + report (one-shot).

File selection:
  By default every eligible .cs file under the target project is instrumented.
  Pass --include one or more times to restrict instrumentation to an allow-list:
    --include MainWindow.cs              (basename match)
    --include ""**/Calculator*.cs""        (glob, matches anywhere in the tree)
    --include src/Foo/Bar.cs             (project-relative path)
  Globs are matched against the absolute path, the project-relative path, and
  the basename — whichever first matches wins. Files passing no include are
  copied verbatim and not rewritten. --exclude is a deny-list applied after.
  --include-from / --exclude-from read patterns from a file, one per line
  ('#' starts a comment, blank lines are ignored).
");
        return code;
    }

    private static (Dictionary<string, List<string>> opts, List<string> pos) Parse(string[] args)
    {
        var opts = new Dictionary<string, List<string>>();
        var pos = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var key = args[i].Substring(2);
                string val = (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[++i] : "true";
                if (!opts.TryGetValue(key, out var list)) opts[key] = list = new List<string>();
                list.Add(val);
            }
            else pos.Add(args[i]);
        }
        return (opts, pos);
    }

    // Reject unknown flags with a helpful suggestion. Silently letting --ouput
    // through (instead of --output) used to swallow the value and send output
    // to the default path, which is a very confusing UX failure.
    private static void RequireKnownFlags(Dictionary<string, List<string>> opts, string verb, IReadOnlyCollection<string> known)
    {
        foreach (var key in opts.Keys)
        {
            if (known.Contains(key)) continue;
            var suggestion = ClosestMatch(key, known);
            var hint = suggestion != null ? $" (did you mean --{suggestion}?)" : "";
            throw new ArgumentException($"unknown flag --{key} for '{verb}'{hint}");
        }
    }

    private static string? ClosestMatch(string input, IEnumerable<string> candidates)
    {
        string? best = null;
        int bestDist = int.MaxValue;
        foreach (var c in candidates)
        {
            var d = Levenshtein(input, c);
            if (d < bestDist) { bestDist = d; best = c; }
        }
        // Only suggest when the edit distance is small relative to the length.
        return bestDist <= Math.Max(2, input.Length / 3) ? best : null;
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        return d[a.Length, b.Length];
    }

    private static readonly HashSet<string> InstrumentFlags = new()
    {
        "output", "runtime", "include", "include-from", "exclude", "exclude-from", "verbose"
    };

    private static readonly HashSet<string> ReportFlags = new()
    {
        "data", "map", "output", "mcdc", "format", "threshold"
    };

    private static readonly HashSet<string> AnalyzeFlags = new()
    {
        "project", "test-project", "source-root", "driver", "output",
        "runtime", "mcdc", "include", "include-from", "exclude", "exclude-from"
    };

    private static int Instrument(string[] args)
    {
        var (opts, pos) = Parse(args);
        RequireKnownFlags(opts, "instrument", InstrumentFlags);
        if (pos.Count < 1) { Console.Error.WriteLine("instrument requires a target"); return 1; }

        var io = new InstrumentOptions
        {
            Target = pos[0],
            Output = opts.TryGetValue("output", out var o) ? o[0] : "",
            RuntimeAssemblyPath = opts.TryGetValue("runtime", out var r) ? r[0] : null,
            Verbose = opts.ContainsKey("verbose")
        };
        if (opts.TryGetValue("exclude", out var ex)) io.Excludes.AddRange(ex);
        if (opts.TryGetValue("include", out var inc)) io.Includes.AddRange(inc);
        if (opts.TryGetValue("include-from", out var incFiles))
            foreach (var f in incFiles) io.Includes.AddRange(ReadGlobList(f));
        if (opts.TryGetValue("exclude-from", out var excFiles))
            foreach (var f in excFiles) io.Excludes.AddRange(ReadGlobList(f));

        var result = ShadowProjectBuilder.Run(io);
        Console.WriteLine($"Instrumented → {result.ShadowRoot}");
        Console.WriteLine($"Map          → {result.MapPath}");
        Console.WriteLine($"Projects     : {result.ShadowProjects.Count}");
        Console.WriteLine($"Files        : {result.Map.Files.Count}");
        Console.WriteLine($"Statements   : {result.Map.Statements.Count}");
        Console.WriteLine($"Decisions    : {result.Map.Decisions.Count}");
        Console.WriteLine($"Conditions   : {result.Map.Decisions.Sum(d => d.Conditions.Count)}");
        return 0;
    }

    private static int Report(string[] args)
    {
        var (opts, _) = Parse(args);
        RequireKnownFlags(opts, "report", ReportFlags);
        string data = opts.GetValueOrDefault("data")?[0] ?? throw new ArgumentException("--data required");
        string mapPath = opts.GetValueOrDefault("map")?[0] ?? throw new ArgumentException("--map required");
        string outDir = opts.GetValueOrDefault("output")?[0] ?? "coverage-report";
        string mcdcStr = opts.GetValueOrDefault("mcdc")?[0] ?? "masking";
        var formats = opts.GetValueOrDefault("format") ?? new List<string> { "html", "text" };

        var mode = mcdcStr.Replace("-", "").Equals("uniquecause", StringComparison.OrdinalIgnoreCase)
            ? McdcMode.UniqueCause : McdcMode.Masking;

        var map = CoverageMapJson.Parse(File.ReadAllText(mapPath));
        var dataM = CoverageDataJson.Parse(File.ReadAllText(data));
        var mcdc = MCDCAnalyzer.Analyze(map, dataM, mode);
        var summary = CoverageSummary.Compute(map, dataM, mcdc);

        Directory.CreateDirectory(outDir);
        if (formats.Contains("html")) HtmlReportRenderer.Render(map, dataM, mcdc, summary, mode, outDir);
        if (formats.Contains("text"))
        {
            var text = TextReporter.Render(summary, mode);
            Console.WriteLine(text);
            File.WriteAllText(Path.Combine(outDir, "summary.txt"), text);
        }
        if (formats.Contains("json"))
            File.WriteAllText(Path.Combine(outDir, "summary.json"),
                $"{{\"statements\":{summary.StatementRatio:F4},\"decisions\":{summary.DecisionRatio:F4},\"conditions\":{summary.ConditionRatio:F4}}}");

        double? threshold = opts.TryGetValue("threshold", out var th) ? double.Parse(th[0]) / 100.0 : null;
        if (threshold.HasValue)
        {
            var minRatio = Math.Min(summary.StatementRatio, Math.Min(summary.DecisionRatio, summary.ConditionRatio));
            if (minRatio < threshold.Value)
            {
                Console.Error.WriteLine($"coverage {minRatio:P1} below threshold {threshold.Value:P1}");
                return 3;
            }
        }
        return 0;
    }

    private static int Analyze(string[] args)
    {
        var (opts, _) = Parse(args);
        RequireKnownFlags(opts, "analyze", AnalyzeFlags);
        string target = opts.GetValueOrDefault("project")?[0] ?? throw new ArgumentException("--project required");
        string? testProjectRel = opts.GetValueOrDefault("test-project")?[0];
        string driver = opts.GetValueOrDefault("driver")?[0] ?? "dotnet test --no-build";
        string outDir = opts.GetValueOrDefault("output")?[0] ?? "coverage-report";
        string mcdcStr = opts.GetValueOrDefault("mcdc")?[0] ?? "masking";

        var io = new InstrumentOptions { Target = target };
        if (opts.TryGetValue("runtime", out var r)) io.RuntimeAssemblyPath = r[0];
        if (opts.TryGetValue("source-root", out var sr)) io.SourceRoot = sr[0];
        if (opts.TryGetValue("include", out var inc)) io.Includes.AddRange(inc);
        if (opts.TryGetValue("include-from", out var incFiles))
            foreach (var f in incFiles) io.Includes.AddRange(ReadGlobList(f));
        if (opts.TryGetValue("exclude", out var exc)) io.Excludes.AddRange(exc);
        if (opts.TryGetValue("exclude-from", out var excFiles))
            foreach (var f in excFiles) io.Excludes.AddRange(ReadGlobList(f));

        var instr = ShadowProjectBuilder.Run(io);
        Console.WriteLine($"[1/3] Instrumented → {instr.ShadowRoot}");
        Console.WriteLine($"       Files: {instr.Map.Files.Count}  Stmts: {instr.Map.Statements.Count}  Dec: {instr.Map.Decisions.Count}  Cond: {instr.Map.Decisions.Sum(d => d.Conditions.Count)}");

        // Build the instrumented project(s). Consumers (tests projects) referenced via
        // ProjectReference will be pulled in transitively when we build the test project.
        foreach (var shadowProj in instr.ShadowProjects)
        {
            Console.WriteLine($"[2/3] Building {Path.GetFileName(shadowProj)}...");
            var rc = ShadowProjectBuilder.Build(shadowProj, verbose: true);
            if (rc != 0) { Console.Error.WriteLine("build failed"); return rc; }
        }

        // Figure out where the driver should run. If --test-project was passed, resolve it
        // inside the shadow and use its directory as the driver cwd.
        string driverCwd = Path.Combine(instr.ShadowRoot, "src");
        if (testProjectRel != null)
        {
            var shadowTestProj = Path.Combine(instr.ShadowRoot, "src", testProjectRel);
            if (File.Exists(shadowTestProj))
                driverCwd = Path.GetDirectoryName(shadowTestProj)!;
            else
                Console.Error.WriteLine($"warning: --test-project {shadowTestProj} not found in shadow; running driver from shadow root");
        }

        Console.WriteLine($"[3/3] Running driver in {driverCwd}: {driver}");
        var covOut = Path.Combine(Path.GetFullPath(instr.ShadowRoot), "coverage.json");
        var env = new Dictionary<string, string?> { ["COVERAGE_OUTPUT"] = covOut };
        var exit = RunShell(driver, driverCwd, env);
        Console.WriteLine($"driver exit={exit}");
        if (!File.Exists(covOut))
        {
            Console.Error.WriteLine($"no coverage.json produced at {covOut} (driver may not have invoked instrumented code, or flush failed)");
            return 4;
        }
        Console.WriteLine($"Coverage data: {covOut}");

        return Report(new[]
        {
            "--data", covOut,
            "--map", instr.MapPath,
            "--output", outDir,
            "--mcdc", mcdcStr
        });
    }

    // Read a list of glob patterns from a text file, one per line.
    // Blank lines and lines starting with '#' are ignored.
    private static IEnumerable<string> ReadGlobList(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"glob list file not found: {path}");
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            yield return line;
        }
    }

    private static int RunShell(string command, string workingDir, IDictionary<string, string?> env)
    {
        var psi = new ProcessStartInfo("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"")
        {
            UseShellExecute = false,
            WorkingDirectory = workingDir
        };
        foreach (var kv in env)
            if (kv.Value != null) psi.Environment[kv.Key] = kv.Value;
        var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }
}
