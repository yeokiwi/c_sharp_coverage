using CSharpCoverage.Core.Model;

namespace CSharpCoverage.Core.Analysis;

public sealed class CoverageSummary
{
    public int TotalStatements;
    public int CoveredStatements;
    public int TotalDecisions;
    public int CoveredDecisions;
    public int TotalConditions;
    public int CoveredConditions;

    public double StatementRatio => TotalStatements == 0 ? 1.0 : (double)CoveredStatements / TotalStatements;
    public double DecisionRatio => TotalDecisions == 0 ? 1.0 : (double)CoveredDecisions / TotalDecisions;
    public double ConditionRatio => TotalConditions == 0 ? 1.0 : (double)CoveredConditions / TotalConditions;

    public Dictionary<int, FileCoverage> Files { get; } = new();

    public static CoverageSummary Compute(CoverageMap map, CoverageData data, McdcReport mcdc)
    {
        var s = new CoverageSummary();
        foreach (var f in map.Files.Values)
            s.Files[f.Id] = new FileCoverage { FileId = f.Id, Path = f.Path };

        foreach (var st in map.Statements)
        {
            s.TotalStatements++;
            s.Files[st.FileId].TotalStatements++;
            if (data.Statements.TryGetValue(st.FileId, out var set) && set.Contains(st.Id))
            {
                s.CoveredStatements++;
                s.Files[st.FileId].CoveredStatements++;
            }
        }

        foreach (var d in map.Decisions)
        {
            s.TotalDecisions++;
            s.Files[d.FileId].TotalDecisions++;

            bool taken = false, notTaken = false;
            bool casesCovered = d.Cases.Count == 0; // if no cases (non-switch), n/a
            if (data.Branches.TryGetValue(d.FileId, out var br) && br.TryGetValue(d.Id, out var bc))
            {
                taken = bc.Taken > 0;
                notTaken = bc.NotTaken > 0;
            }
            if (d.Cases.Count > 0)
            {
                int hit = 0;
                if (data.Cases.TryGetValue(d.FileId, out var fc) && fc.TryGetValue(d.Id, out var cm))
                    foreach (var ce in d.Cases)
                        if (cm.TryGetValue(ce.Index, out var h) && h > 0) hit++;
                casesCovered = hit == d.Cases.Count;
            }
            bool full = (d.Cases.Count > 0) ? casesCovered : (taken && notTaken);
            if (full)
            {
                s.CoveredDecisions++;
                s.Files[d.FileId].CoveredDecisions++;
            }

            s.TotalConditions += d.Conditions.Count;
            s.Files[d.FileId].TotalConditions += d.Conditions.Count;
            if (mcdc.Decisions.TryGetValue((d.FileId, d.Id), out var dc))
            {
                s.CoveredConditions += dc.CoveredConditions;
                s.Files[d.FileId].CoveredConditions += dc.CoveredConditions;
            }
        }
        return s;
    }
}

public sealed class FileCoverage
{
    public int FileId;
    public string Path = "";
    public int TotalStatements, CoveredStatements;
    public int TotalDecisions, CoveredDecisions;
    public int TotalConditions, CoveredConditions;
    public double StatementRatio => TotalStatements == 0 ? 1.0 : (double)CoveredStatements / TotalStatements;
    public double DecisionRatio => TotalDecisions == 0 ? 1.0 : (double)CoveredDecisions / TotalDecisions;
    public double ConditionRatio => TotalConditions == 0 ? 1.0 : (double)CoveredConditions / TotalConditions;
}
