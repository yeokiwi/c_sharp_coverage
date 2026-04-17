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
        Console.WriteLine("""
coverage — C# statement/decision/MCDC coverage

Usage:
  coverage instrument <target> [--output <dir>] [--runtime <path>] [--exclude <glob>]...
  coverage report     --data <json> --map <json> [--output <dir>]
                      [--mcdc unique-cause|masking] [--format html|text|json]...
  coverage analyze    --project <csproj|sln> --driver "<shell>"
                      [--output <dir>] [--mcdc unique-cause|masking]

Notes:
  instrument rewrites a shadow copy of <target> (file/csproj/sln) with runtime probes.
  report     consumes coverage.json + coverage.map.json and renders HTML/text.
  analyze    = instrument + dotnet build + driver + report (one-shot).
""");
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

    private static int Instrument(string[] args)
    {
        var (opts, pos) = Parse(args);
        if (pos.Count < 1) { Console.Error.WriteLine("instrument requires a target"); return 1; }

        var io = new InstrumentOptions
        {
            Target = pos[0],
            Output = opts.TryGetValue("output", out var o) ? o[0] : "",
            RuntimeAssemblyPath = opts.TryGetValue("runtime", out var r) ? r[0] : null,
            Verbose = opts.ContainsKey("verbose")
        };
        if (opts.TryGetValue("exclude", out var ex)) io.Excludes.AddRange(ex);

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
        string target = opts.GetValueOrDefault("project")?[0] ?? throw new ArgumentException("--project required");
        string? testProjectRel = opts.GetValueOrDefault("test-project")?[0];
        string driver = opts.GetValueOrDefault("driver")?[0] ?? "dotnet test --no-build";
        string outDir = opts.GetValueOrDefault("output")?[0] ?? "coverage-report";
        string mcdcStr = opts.GetValueOrDefault("mcdc")?[0] ?? "masking";

        var io = new InstrumentOptions { Target = target };
        if (opts.TryGetValue("runtime", out var r)) io.RuntimeAssemblyPath = r[0];
        if (opts.TryGetValue("source-root", out var sr)) io.SourceRoot = sr[0];

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
