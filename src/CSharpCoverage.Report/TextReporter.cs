using CSharpCoverage.Core.Analysis;
using CSharpCoverage.Core.Model;

namespace CSharpCoverage.Report;

public static class TextReporter
{
    public static string Render(CoverageSummary s, McdcMode mode)
    {
        var sw = new StringWriter();
        sw.WriteLine("C# Coverage Report");
        sw.WriteLine("==================");
        sw.WriteLine($"MCDC mode: {mode}");
        sw.WriteLine();
        sw.WriteLine($"Statements : {s.CoveredStatements,6} / {s.TotalStatements,-6}  {s.StatementRatio:P1}");
        sw.WriteLine($"Decisions  : {s.CoveredDecisions,6} / {s.TotalDecisions,-6}  {s.DecisionRatio:P1}");
        sw.WriteLine($"MCDC cond. : {s.CoveredConditions,6} / {s.TotalConditions,-6}  {s.ConditionRatio:P1}");
        sw.WriteLine();
        sw.WriteLine("Per-file:");
        foreach (var f in s.Files.Values.OrderBy(x => x.Path))
        {
            if (f.TotalStatements == 0 && f.TotalDecisions == 0) continue;
            sw.WriteLine($"  {f.Path}");
            sw.WriteLine($"    stmt {f.CoveredStatements}/{f.TotalStatements} ({f.StatementRatio:P0})  " +
                         $"dec {f.CoveredDecisions}/{f.TotalDecisions} ({f.DecisionRatio:P0})  " +
                         $"mcdc {f.CoveredConditions}/{f.TotalConditions} ({f.ConditionRatio:P0})");
        }
        return sw.ToString();
    }
}
