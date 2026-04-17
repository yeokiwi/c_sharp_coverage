using System.Text;

namespace CSharpCoverage.Core.Model;

public static class MapJson
{
    public static string Serialize(CoverageMap map)
    {
        var sb = new StringBuilder();
        sb.Append('{');

        sb.Append("\"files\":{");
        var firstF = true;
        foreach (var f in map.Files.Values.OrderBy(x => x.Id))
        {
            if (!firstF) sb.Append(','); firstF = false;
            sb.Append('"').Append(f.Id).Append("\":{");
            sb.Append("\"path\":\"").Append(Esc(f.Path)).Append("\",");
            sb.Append("\"absolute\":\"").Append(Esc(f.Absolute)).Append("\"}");
        }
        sb.Append("},");

        sb.Append("\"statements\":{");
        var byFileSt = map.Statements.GroupBy(s => s.FileId).OrderBy(g => g.Key);
        firstF = true;
        foreach (var g in byFileSt)
        {
            if (!firstF) sb.Append(','); firstF = false;
            sb.Append('"').Append(g.Key).Append("\":{");
            var firstS = true;
            foreach (var s in g.OrderBy(x => x.Id))
            {
                if (!firstS) sb.Append(','); firstS = false;
                sb.Append('"').Append(s.Id).Append("\":{");
                sb.Append("\"line\":").Append(s.Line).Append(',');
                sb.Append("\"col\":").Append(s.Column).Append(',');
                sb.Append("\"text\":\"").Append(Esc(s.Text)).Append("\"}");
            }
            sb.Append('}');
        }
        sb.Append("},");

        sb.Append("\"decisions\":{");
        var byFileD = map.Decisions.GroupBy(d => d.FileId).OrderBy(g => g.Key);
        firstF = true;
        foreach (var g in byFileD)
        {
            if (!firstF) sb.Append(','); firstF = false;
            sb.Append('"').Append(g.Key).Append("\":{");
            var firstD = true;
            foreach (var d in g.OrderBy(x => x.Id))
            {
                if (!firstD) sb.Append(','); firstD = false;
                sb.Append('"').Append(d.Id).Append("\":{");
                sb.Append("\"line\":").Append(d.Line).Append(',');
                sb.Append("\"col\":").Append(d.Column).Append(',');
                sb.Append("\"kind\":\"").Append(d.Kind).Append("\",");
                sb.Append("\"text\":\"").Append(Esc(d.Text)).Append("\",");
                sb.Append("\"conditions\":[");
                var firstC = true;
                foreach (var c in d.Conditions.OrderBy(x => x.Index))
                {
                    if (!firstC) sb.Append(','); firstC = false;
                    sb.Append("{\"idx\":").Append(c.Index)
                      .Append(",\"line\":").Append(c.Line)
                      .Append(",\"col\":").Append(c.Column)
                      .Append(",\"text\":\"").Append(Esc(c.Text)).Append("\"}");
                }
                sb.Append("],\"cases\":[");
                var firstCase = true;
                foreach (var ce in d.Cases.OrderBy(x => x.Index))
                {
                    if (!firstCase) sb.Append(','); firstCase = false;
                    sb.Append("{\"idx\":").Append(ce.Index)
                      .Append(",\"line\":").Append(ce.Line)
                      .Append(",\"label\":\"").Append(Esc(ce.Label)).Append("\"}");
                }
                sb.Append("],\"ast\":");
                SerAst(sb, d.Ast);
                sb.Append('}');
            }
            sb.Append('}');
        }
        sb.Append("}");

        sb.Append('}');
        return sb.ToString();
    }

    private static void SerAst(StringBuilder sb, BoolNode? n)
    {
        if (n == null) { sb.Append("null"); return; }
        sb.Append("{\"op\":\"").Append(n.Op).Append('"');
        if (n.Op == BoolOp.Leaf) sb.Append(",\"leaf\":").Append(n.LeafIndex);
        if (n.Kids.Count > 0)
        {
            sb.Append(",\"kids\":[");
            for (int i = 0; i < n.Kids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerAst(sb, n.Kids[i]);
            }
            sb.Append(']');
        }
        sb.Append('}');
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length + 4);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
