using System.Text;
using CSharpCoverage.Core.Analysis;
using CSharpCoverage.Core.Model;

namespace CSharpCoverage.Report;

public static class HtmlReportRenderer
{
    public static void Render(
        CoverageMap map, CoverageData data, McdcReport mcdc, CoverageSummary summary,
        McdcMode mode, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(Path.Combine(outputDir, "files"));
        Directory.CreateDirectory(Path.Combine(outputDir, "decisions"));

        File.WriteAllText(Path.Combine(outputDir, "assets.css"), Css);

        // Index
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Coverage report</title><link rel=\"stylesheet\" href=\"assets.css\"></head><body>");
        sb.AppendLine($"<h1>C# Coverage Report</h1>");
        sb.AppendLine($"<p class=\"mode\">MCDC mode: <b>{mode}</b></p>");
        sb.AppendLine("<table class=\"summary\"><thead><tr><th>File</th><th>Statements</th><th>Decisions</th><th>MCDC</th></tr></thead><tbody>");
        sb.AppendLine($"<tr class=\"total\"><td><b>TOTAL</b></td>{Bar(summary.CoveredStatements, summary.TotalStatements)}{Bar(summary.CoveredDecisions, summary.TotalDecisions)}{Bar(summary.CoveredConditions, summary.TotalConditions)}</tr>");
        foreach (var fc in summary.Files.Values.OrderBy(f => f.Path))
        {
            if (fc.TotalStatements == 0 && fc.TotalDecisions == 0) continue;
            sb.AppendLine($"<tr><td><a href=\"files/{FileHtmlName(fc.FileId, fc.Path)}\">{Esc(fc.Path)}</a></td>{Bar(fc.CoveredStatements, fc.TotalStatements)}{Bar(fc.CoveredDecisions, fc.TotalDecisions)}{Bar(fc.CoveredConditions, fc.TotalConditions)}</tr>");
        }
        sb.AppendLine("</tbody></table></body></html>");
        File.WriteAllText(Path.Combine(outputDir, "index.html"), sb.ToString());

        // Per-file pages
        foreach (var f in map.Files.Values)
        {
            RenderFile(f, map, data, mcdc, outputDir);
        }

        // Per-decision pages
        foreach (var d in map.Decisions)
        {
            RenderDecision(d, map, data, mcdc, outputDir);
        }
    }

    private static string FileHtmlName(int fileId, string path)
    {
        var safe = path.Replace('/', '_').Replace('\\', '_');
        return $"f{fileId}_{safe}.html";
    }

    private static void RenderFile(FileInfoEntry f, CoverageMap map, CoverageData data, McdcReport mcdc, string outputDir)
    {
        if (!File.Exists(f.Absolute)) return;
        var source = File.ReadAllLines(f.Absolute);
        var stmtsByLine = map.Statements.Where(s => s.FileId == f.Id).GroupBy(s => s.Line).ToDictionary(g => g.Key, g => g.ToList());
        var decsByLine = map.Decisions.Where(d => d.FileId == f.Id).GroupBy(d => d.Line).ToDictionary(g => g.Key, g => g.ToList());
        var hitStmts = data.Statements.TryGetValue(f.Id, out var hs) ? hs : new HashSet<int>();
        data.Branches.TryGetValue(f.Id, out var brs);
        data.Cases.TryGetValue(f.Id, out var cases);

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>" + Esc(f.Path) + "</title><link rel=\"stylesheet\" href=\"../assets.css\"></head><body>");
        sb.AppendLine($"<p><a href=\"../index.html\">&larr; Back to index</a></p>");
        sb.AppendLine($"<h2>{Esc(f.Path)}</h2>");
        sb.AppendLine("<pre class=\"source\">");
        for (int i = 0; i < source.Length; i++)
        {
            int lineNo = i + 1;
            string cls = "plain";
            string annotation = "";
            bool anyStmt = stmtsByLine.TryGetValue(lineNo, out var ls);
            if (anyStmt)
            {
                bool allHit = ls!.All(s => hitStmts.Contains(s.Id));
                cls = allHit ? "stmt-hit" : "stmt-miss";
            }
            if (decsByLine.TryGetValue(lineNo, out var ld))
            {
                foreach (var d in ld)
                {
                    bool taken = brs != null && brs.TryGetValue(d.Id, out var bc) && bc.Taken > 0;
                    bool notTaken = brs != null && brs.TryGetValue(d.Id, out var bc2) && bc2.NotTaken > 0;
                    bool casesOk = true;
                    int casesHit = 0;
                    if (d.Cases.Count > 0 && cases != null && cases.TryGetValue(d.Id, out var cm))
                    {
                        foreach (var ce in d.Cases) if (cm.TryGetValue(ce.Index, out var h) && h > 0) casesHit++;
                        casesOk = casesHit == d.Cases.Count;
                    }
                    else if (d.Cases.Count > 0) { casesOk = false; }
                    bool full = d.Cases.Count > 0 ? casesOk : taken && notTaken;
                    string brCls = full ? "br-full" : (taken || notTaken || casesHit > 0 ? "br-partial" : "br-none");
                    cls = cls == "stmt-miss" ? "stmt-miss" : brCls;
                    var mcdcInfo = "";
                    if (d.Conditions.Count > 0 && mcdc.Decisions.TryGetValue((f.Id, d.Id), out var dc))
                    {
                        var pct = dc.ConditionCount == 0 ? 100 : (int)Math.Round(100.0 * dc.CoveredConditions / dc.ConditionCount);
                        mcdcInfo = $" <a class=\"mcdc-link\" href=\"../decisions/f{f.Id}_d{d.Id}.html\">[MCDC {dc.CoveredConditions}/{dc.ConditionCount} = {pct}%]</a>";
                    }
                    annotation += $" <span class=\"ann\">decision #{d.Id}: T={(taken?1:0)} F={(notTaken?1:0)}{mcdcInfo}</span>";
                }
            }
            sb.AppendLine($"<span class=\"line {cls}\"><span class=\"lno\">{lineNo,4}</span> {Esc(source[i])}{annotation}</span>");
        }
        sb.AppendLine("</pre></body></html>");
        File.WriteAllText(Path.Combine(outputDir, "files", FileHtmlName(f.Id, f.Path)), sb.ToString());
    }

    private static void RenderDecision(DecisionEntry d, CoverageMap map, CoverageData data, McdcReport mcdc, string outputDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Decision</title><link rel=\"stylesheet\" href=\"../assets.css\"></head><body>");
        sb.AppendLine($"<p><a href=\"../index.html\">&larr; Back to index</a></p>");
        var fileEntry = map.Files[d.FileId];
        sb.AppendLine($"<h2>Decision #{d.Id} in {Esc(fileEntry.Path)}</h2>");
        sb.AppendLine($"<p>Line {d.Line} — <code>{Esc(d.Text)}</code></p>");
        sb.AppendLine($"<p>Kind: {d.Kind}</p>");

        if (d.Conditions.Count == 0)
        {
            sb.AppendLine("<p>No boolean conditions (non-MCDC decision).</p>");
        }
        else
        {
            sb.AppendLine("<h3>Atomic conditions</h3><ol>");
            foreach (var c in d.Conditions)
                sb.AppendLine($"<li>c{c.Index}: <code>{Esc(c.Text)}</code> (line {c.Line})</li>");
            sb.AppendLine("</ol>");

            sb.AppendLine("<h3>Observations</h3>");
            sb.AppendLine("<table class=\"truth\"><thead><tr><th>#</th>");
            for (int i = 0; i < d.Conditions.Count; i++) sb.Append($"<th>c{i}</th>");
            sb.AppendLine("<th>outcome</th></tr></thead><tbody>");
            if (data.Observations.TryGetValue(d.FileId, out var fo) && fo.TryGetValue(d.Id, out var obs))
            {
                for (int r = 0; r < obs.Count; r++)
                {
                    sb.Append($"<tr><td>{r + 1}</td>");
                    for (int c = 0; c < d.Conditions.Count; c++)
                    {
                        var v = c < obs[r].Values.Length ? obs[r].Values[c] : (byte)0;
                        var s = v == 1 ? "T" : v == 2 ? "F" : "—";
                        var cls = v == 1 ? "t" : v == 2 ? "f" : "na";
                        sb.Append($"<td class=\"{cls}\">{s}</td>");
                    }
                    sb.Append($"<td>{(obs[r].Outcome ? "true" : "false")}</td></tr>");
                }
            }
            else sb.AppendLine("<tr><td colspan=\"99\"><i>no observations</i></td></tr>");
            sb.AppendLine("</tbody></table>");

            sb.AppendLine("<h3>MCDC coverage</h3><ul>");
            if (mcdc.Decisions.TryGetValue((d.FileId, d.Id), out var dc))
            {
                var provenMap = dc.Proofs.ToDictionary(p => p.condIdx);
                for (int i = 0; i < d.Conditions.Count; i++)
                {
                    if (provenMap.TryGetValue(i, out var p))
                        sb.AppendLine($"<li class=\"ok\">c{i} — proven by observations #{p.obsA + 1} vs #{p.obsB + 1}</li>");
                    else
                        sb.AppendLine($"<li class=\"miss\">c{i} — not proven (needs additional test)</li>");
                }
            }
            sb.AppendLine("</ul>");
        }
        sb.AppendLine("</body></html>");
        File.WriteAllText(Path.Combine(outputDir, "decisions", $"f{d.FileId}_d{d.Id}.html"), sb.ToString());
    }

    private static string Bar(int hit, int total)
    {
        if (total == 0) return "<td class=\"bar\"><span class=\"na\">n/a</span></td>";
        int pct = (int)Math.Round(100.0 * hit / total);
        string cls = pct == 100 ? "full" : pct >= 75 ? "ok" : pct >= 50 ? "warn" : "bad";
        return $"<td class=\"bar\"><span class=\"pct {cls}\" style=\"width:{pct}%\">{pct}%</span> <small>{hit}/{total}</small></td>";
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            switch (c)
            {
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '&': sb.Append("&amp;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private const string Css = """
body { font-family: -apple-system, "Segoe UI", sans-serif; margin: 1.5rem; color: #222; }
h1, h2 { color: #333; }
.mode { color: #666; }
table.summary { border-collapse: collapse; width: 100%; }
table.summary th, table.summary td { padding: 6px 10px; border-bottom: 1px solid #eee; text-align: left; }
table.summary tr.total { background: #f7f7f7; }
td.bar { width: 22%; min-width: 180px; }
.pct { display: inline-block; padding: 1px 6px; border-radius: 3px; color: white; font-weight: bold; }
.pct.full { background: #27ae60; }
.pct.ok { background: #3498db; }
.pct.warn { background: #e67e22; }
.pct.bad { background: #c0392b; }
.pct.na, .na { color: #999; }
pre.source { background: #fafafa; border: 1px solid #ddd; padding: 8px; overflow-x: auto; line-height: 1.35; }
.line { display: block; white-space: pre; }
.lno { display: inline-block; width: 4ch; color: #999; margin-right: 8px; }
.stmt-hit { background: rgba(39, 174, 96, 0.12); }
.stmt-miss { background: rgba(192, 57, 43, 0.15); }
.br-full { background: rgba(39, 174, 96, 0.12); }
.br-partial { background: rgba(241, 196, 15, 0.25); }
.br-none { background: rgba(192, 57, 43, 0.15); }
.ann { color: #555; font-size: 0.85em; }
.mcdc-link { color: #2980b9; text-decoration: none; }
.mcdc-link:hover { text-decoration: underline; }
table.truth { border-collapse: collapse; margin: 1em 0; }
table.truth th, table.truth td { border: 1px solid #ccc; padding: 3px 10px; text-align: center; font-family: monospace; }
table.truth td.t { background: #e1f5e1; }
table.truth td.f { background: #fde0e0; }
table.truth td.na { color: #999; }
li.ok { color: #27ae60; }
li.miss { color: #c0392b; }
""";
}
