using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CSharpCoverage.Core.Model;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpCoverage.Core.Instrumentation;

public sealed class InstrumentOptions
{
    public string Target { get; set; } = "";
    public string Output { get; set; } = "";
    public string? RuntimeAssemblyPath { get; set; }
    /// <summary>
    /// If set, mirrors this directory tree into the shadow instead of the target's
    /// immediate parent. Rewriting is still restricted to files under the target project.
    /// </summary>
    public string? SourceRoot { get; set; }
    /// <summary>
    /// Allow-list of glob patterns. When non-empty, only .cs files matching at
    /// least one include glob are instrumented; all others are mirrored verbatim.
    /// Globs are matched against both the absolute path and the path relative to
    /// the project directory (forward-slashed). When empty, every eligible .cs
    /// file under the target project is instrumented.
    /// </summary>
    public List<string> Includes { get; } = new();
    public List<string> Excludes { get; } = new();
    public bool Verbose { get; set; }
}

public sealed class InstrumentResult
{
    public string ShadowRoot { get; set; } = "";
    public string MapPath { get; set; } = "";
    public List<string> ShadowProjects { get; } = new();
    public CoverageMap Map { get; set; } = new();
}

public static class ShadowProjectBuilder
{
    public static InstrumentResult Run(InstrumentOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.Target) || !File.Exists(opts.Target) && !Directory.Exists(opts.Target))
            throw new FileNotFoundException($"Target not found: {opts.Target}");

        var ext = Path.GetExtension(opts.Target).ToLowerInvariant();
        string shadowRoot = opts.Output;
        if (string.IsNullOrEmpty(shadowRoot))
        {
            var hash = ShortHash(Path.GetFullPath(opts.Target));
            shadowRoot = Path.Combine(Directory.GetCurrentDirectory(), "_coverage_shadow", hash);
        }
        shadowRoot = Path.GetFullPath(shadowRoot);
        if (Directory.Exists(shadowRoot)) Directory.Delete(shadowRoot, true);
        Directory.CreateDirectory(shadowRoot);

        var ctx = new InstrumentationContext();
        var result = new InstrumentResult { ShadowRoot = shadowRoot };

        List<string> projects;
        string sourceRoot;
        if (ext == ".sln")
        {
            projects = ParseSolutionProjects(opts.Target);
            sourceRoot = opts.SourceRoot ?? Path.GetDirectoryName(Path.GetFullPath(opts.Target))!;
        }
        else if (ext == ".csproj")
        {
            var abs = Path.GetFullPath(opts.Target);
            projects = new List<string> { abs };
            sourceRoot = opts.SourceRoot ?? FindEnclosingSolutionDir(abs) ?? Path.GetDirectoryName(abs)!;
        }
        else if (ext == ".cs")
        {
            // synthesize a simple project wrapper
            sourceRoot = opts.SourceRoot ?? Path.GetDirectoryName(Path.GetFullPath(opts.Target))!;
            var wrapperPath = Path.Combine(sourceRoot, "_CoverageTarget.csproj");
            if (!File.Exists(wrapperPath))
                File.WriteAllText(wrapperPath, SyntheticCsproj(opts.Target));
            projects = new List<string> { wrapperPath };
        }
        else throw new ArgumentException($"Unsupported target type: {ext}");

        // Copy full tree. shadowRoot is passed so the mirror can skip any path
        // inside it — otherwise, when shadowRoot lives under sourceRoot (common
        // when the user runs from the project dir), the lazy enumeration will
        // keep discovering dirs we just created and recurse into them.
        MirrorDirectory(sourceRoot, Path.Combine(shadowRoot, "src"), shadowRoot, opts);

        // Rewrite .cs files — only under the target projects' directories
        int instrumented = 0, skipped = 0;
        foreach (var proj in projects)
        {
            var rel = Path.GetRelativePath(sourceRoot, proj);
            var shadowProj = Path.Combine(shadowRoot, "src", rel);
            result.ShadowProjects.Add(shadowProj);

            var projDir = Path.GetDirectoryName(shadowProj)!;
            foreach (var cs in Directory.EnumerateFiles(projDir, "*.cs", SearchOption.AllDirectories))
            {
                if (!ShouldRewrite(cs)) continue;
                var relToProj = Path.GetRelativePath(projDir, cs).Replace('\\', '/');
                if (opts.Includes.Count > 0 && !MatchesGlob(cs, relToProj, opts.Includes))
                {
                    if (opts.Verbose) Console.WriteLine($"  skip (not included): {relToProj}");
                    skipped++;
                    continue;
                }
                if (MatchesGlob(cs, relToProj, opts.Excludes))
                {
                    if (opts.Verbose) Console.WriteLine($"  skip (excluded):     {relToProj}");
                    skipped++;
                    continue;
                }

                var original = File.ReadAllText(cs);
                if (IsAutoGenerated(original)) continue;

                var relToRoot = Path.GetRelativePath(Path.Combine(shadowRoot, "src"), cs).Replace('\\', '/');

                // Stash the un-instrumented source under <shadowRoot>/originals/<rel>
                // and point the map at that copy. The HTML renderer reads the path
                // we record here, so without this it would show the probe-injected
                // code instead of the original.
                var originalCopy = Path.Combine(shadowRoot, "originals", relToRoot.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(originalCopy)!);
                File.WriteAllText(originalCopy, original);

                int fileId = ctx.AllocateFileId(relToRoot, originalCopy);

                var tree = CSharpSyntaxTree.ParseText(original, path: cs);
                var rewriter = new CoverageRewriter(ctx, fileId);
                var newRoot = rewriter.Rewrite(tree.GetRoot());
                File.WriteAllText(cs, newRoot.ToFullString());
                if (opts.Verbose) Console.WriteLine($"  rewrite:             {relToProj}");
                instrumented++;
            }
        }
        if (opts.Includes.Count > 0 || opts.Excludes.Count > 0)
            Console.WriteLine($"  Filter: instrumented {instrumented}, skipped {skipped} .cs file(s)");

        // Drop Directory.Build.props that references runtime assembly
        var runtimeDll = opts.RuntimeAssemblyPath ?? LocateRuntimeDll();
        var runtimeDest = Path.Combine(shadowRoot, "CSharpCoverage.Runtime.dll");
        File.Copy(runtimeDll, runtimeDest, true);
        var runtimePdb = Path.ChangeExtension(runtimeDll, ".pdb");
        if (File.Exists(runtimePdb))
            File.Copy(runtimePdb, Path.ChangeExtension(runtimeDest, ".pdb"), true);

        // Directory.Build.props applies to all projects under shadow/src
        File.WriteAllText(
            Path.Combine(shadowRoot, "src", "Directory.Build.props"),
            DirectoryBuildProps(runtimeDest));

        // Persist map
        result.Map = ctx.Map;
        result.MapPath = Path.Combine(shadowRoot, "coverage.map.json");
        File.WriteAllText(result.MapPath, MapJson.Serialize(ctx.Map));

        return result;
    }

    public static int Build(string projectOrSolution, bool verbose = false)
    {
        var psi = new ProcessStartInfo("dotnet", $"build \"{projectOrSolution}\" -c Debug -nologo")
        {
            RedirectStandardOutput = !verbose,
            RedirectStandardError = !verbose,
            UseShellExecute = false
        };
        var p = Process.Start(psi)!;
        if (!verbose)
        {
            var outp = p.StandardOutput.ReadToEnd();
            var errp = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                Console.Error.WriteLine(outp);
                Console.Error.WriteLine(errp);
            }
        }
        else p.WaitForExit();
        return p.ExitCode;
    }

    // ---- helpers ----

    private static bool ShouldRewrite(string path)
    {
        if (path.Contains("/obj/") || path.Contains("\\obj\\")) return false;
        if (path.Contains("/bin/") || path.Contains("\\bin\\")) return false;
        var name = Path.GetFileName(path);
        if (name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.StartsWith("TemporaryGeneratedFile_", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    // A glob matches if it matches any of: the absolute path, the
    // project-relative path, or the file's basename. A pattern with no
    // wildcards behaves like an exact basename or relative-path match,
    // so users can pass plain filenames such as "MainWindow.cs".
    private static bool MatchesGlob(string absPath, string relToProj, IEnumerable<string> globs)
    {
        var abs = absPath.Replace('\\', '/');
        var name = Path.GetFileName(absPath);
        foreach (var g in globs)
        {
            var rx = GlobToRegex(g);
            if (System.Text.RegularExpressions.Regex.IsMatch(abs, rx)) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(relToProj, rx)) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(name, rx)) return true;
        }
        return false;
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        foreach (var c in glob)
        {
            if (c == '*') sb.Append(".*");
            else if (c == '?') sb.Append('.');
            else if ("+()^$.{}|\\".IndexOf(c) >= 0) sb.Append('\\').Append(c);
            else sb.Append(c);
        }
        sb.Append('$');
        return sb.ToString();
    }

    private static bool IsAutoGenerated(string src)
    {
        var head = src.Length > 500 ? src.Substring(0, 500) : src;
        return head.Contains("<auto-generated>") || head.Contains("<autogenerated>");
    }

    private static void MirrorDirectory(string sourceDir, string destDir, string shadowRoot, InstrumentOptions opts)
    {
        var shadowAbs = Path.GetFullPath(shadowRoot);
        var sourceAbs = Path.GetFullPath(sourceDir);

        // Materialize the enumeration up front. Directory.EnumerateDirectories
        // is lazy; without this, dirs we create under destDir (when destDir is
        // nested inside sourceDir) would be visited and recursed into.
        var dirs = SafeEnumerate(sourceAbs, shadowAbs, recurseDirs: true);
        foreach (var dir in dirs)
        {
            var rel = Path.GetRelativePath(sourceAbs, dir);
            if (IsSkippedDir(rel)) continue;
            Directory.CreateDirectory(Path.Combine(destDir, rel));
        }
        Directory.CreateDirectory(destDir);

        var files = SafeEnumerate(sourceAbs, shadowAbs, recurseDirs: false);
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(sourceAbs, file);
            if (IsSkippedDir(rel)) continue;
            var dst = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file, dst, true);
        }
    }

    // Iterative walk that prunes any subtree equal to or under shadowAbs, plus
    // well-known noise (bin/obj, .git, node_modules). recurseDirs=true yields
    // directories; recurseDirs=false yields files.
    private static List<string> SafeEnumerate(string rootAbs, string shadowAbs, bool recurseDirs)
    {
        var results = new List<string>();
        var stack = new Stack<string>();
        stack.Push(rootAbs);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> subdirs;
            try { subdirs = Directory.EnumerateDirectories(current); }
            catch { continue; }
            foreach (var sub in subdirs)
            {
                if (IsUnderOrEqual(sub, shadowAbs)) continue;
                var name = Path.GetFileName(sub);
                if (name == ".git" || name == "node_modules" || name == "bin" || name == "obj"
                    || name == "_coverage_shadow") continue;
                if (recurseDirs) results.Add(sub);
                stack.Push(sub);
            }
            if (!recurseDirs)
            {
                IEnumerable<string> localFiles;
                try { localFiles = Directory.EnumerateFiles(current); }
                catch { continue; }
                results.AddRange(localFiles);
            }
        }
        return results;
    }

    private static bool IsUnderOrEqual(string path, string ancestorAbs)
    {
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var a = ancestorAbs.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(p, a, StringComparison.OrdinalIgnoreCase)) return true;
        return p.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(a + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSkippedDir(string rel)
    {
        rel = rel.Replace('\\', '/');
        return rel.StartsWith("bin/") || rel.StartsWith("obj/")
            || rel.Contains("/bin/") || rel.Contains("/obj/")
            || rel.StartsWith("_coverage_shadow/") || rel.Contains("/_coverage_shadow/")
            || rel.StartsWith(".git/") || rel.Contains("/.git/");
    }

    private static string? FindEnclosingSolutionDir(string startPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(startPath));
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.EnumerateFiles(dir, "*.sln").Any()) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static List<string> ParseSolutionProjects(string slnPath)
    {
        var list = new List<string>();
        var slnDir = Path.GetDirectoryName(Path.GetFullPath(slnPath))!;
        foreach (var line in File.ReadAllLines(slnPath))
        {
            if (!line.StartsWith("Project(")) continue;
            // Project("{GUID}") = "Name", "path\proj.csproj", "{GUID}"
            var parts = line.Split(',');
            if (parts.Length < 2) continue;
            var rel = parts[1].Trim().Trim('"');
            if (!rel.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(Path.GetFullPath(Path.Combine(slnDir, rel)));
        }
        return list;
    }

    private static string LocateRuntimeDll()
    {
        var baseDir = AppContext.BaseDirectory;
        var dll = Path.Combine(baseDir, "CSharpCoverage.Runtime.dll");
        if (File.Exists(dll)) return dll;
        // Try relative hop to src/CSharpCoverage.Runtime/bin/Debug/netstandard2.0
        var probe = Path.Combine(baseDir, "..", "..", "..", "..", "CSharpCoverage.Runtime", "bin", "Debug", "netstandard2.0", "CSharpCoverage.Runtime.dll");
        if (File.Exists(probe)) return Path.GetFullPath(probe);
        throw new FileNotFoundException("Could not locate CSharpCoverage.Runtime.dll. Build it first.");
    }

    private static string DirectoryBuildProps(string runtimeDll)
    {
        var abs = runtimeDll.Replace('\\', '/');
        return $@"<Project>
  <ItemGroup>
    <Reference Include=""CSharpCoverage.Runtime"">
      <HintPath>{abs}</HintPath>
      <Private>true</Private>
    </Reference>
  </ItemGroup>
  <PropertyGroup>
    <NoWarn>$(NoWarn);CS8602;CS8604;CS8619;CS0162;CS0168;CS0219;CS8600;CS0472;NETSDK1138</NoWarn>
    <RollForward>Major</RollForward>
  </PropertyGroup>
</Project>
";
    }

    private static string SyntheticCsproj(string csPath) => $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <RollForward>Major</RollForward>
    <OutputType>Library</OutputType>
    <EnableDefaultItems>false</EnableDefaultItems>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""{Path.GetFileName(csPath)}"" />
  </ItemGroup>
</Project>
";

    private static string ShortHash(string s)
    {
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(16);
        for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }
}
