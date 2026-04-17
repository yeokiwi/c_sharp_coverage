using System.Text.Json;

namespace CSharpCoverage.Core.Model;

public static class CoverageMapJson
{
    public static CoverageMap Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var map = new CoverageMap();

        foreach (var fp in root.GetProperty("files").EnumerateObject())
        {
            int id = int.Parse(fp.Name);
            map.Files[id] = new FileInfoEntry
            {
                Id = id,
                Path = fp.Value.GetProperty("path").GetString() ?? "",
                Absolute = fp.Value.GetProperty("absolute").GetString() ?? ""
            };
        }

        foreach (var fp in root.GetProperty("statements").EnumerateObject())
        {
            int fid = int.Parse(fp.Name);
            foreach (var sp in fp.Value.EnumerateObject())
            {
                map.Statements.Add(new StatementEntry
                {
                    FileId = fid,
                    Id = int.Parse(sp.Name),
                    Line = sp.Value.GetProperty("line").GetInt32(),
                    Column = sp.Value.GetProperty("col").GetInt32(),
                    Text = sp.Value.GetProperty("text").GetString() ?? ""
                });
            }
        }

        foreach (var fp in root.GetProperty("decisions").EnumerateObject())
        {
            int fid = int.Parse(fp.Name);
            foreach (var dp in fp.Value.EnumerateObject())
            {
                int did = int.Parse(dp.Name);
                var d = new DecisionEntry
                {
                    FileId = fid,
                    Id = did,
                    Line = dp.Value.GetProperty("line").GetInt32(),
                    Column = dp.Value.GetProperty("col").GetInt32(),
                    Kind = Enum.Parse<DecisionKind>(dp.Value.GetProperty("kind").GetString() ?? "If"),
                    Text = dp.Value.GetProperty("text").GetString() ?? "",
                };
                foreach (var c in dp.Value.GetProperty("conditions").EnumerateArray())
                    d.Conditions.Add(new ConditionEntry
                    {
                        Index = c.GetProperty("idx").GetInt32(),
                        Line = c.GetProperty("line").GetInt32(),
                        Column = c.GetProperty("col").GetInt32(),
                        Text = c.GetProperty("text").GetString() ?? ""
                    });
                foreach (var c in dp.Value.GetProperty("cases").EnumerateArray())
                    d.Cases.Add(new CaseEntry
                    {
                        Index = c.GetProperty("idx").GetInt32(),
                        Line = c.GetProperty("line").GetInt32(),
                        Label = c.GetProperty("label").GetString() ?? ""
                    });
                if (dp.Value.TryGetProperty("ast", out var ast) && ast.ValueKind != JsonValueKind.Null)
                    d.Ast = ParseAst(ast);
                map.Decisions.Add(d);
            }
        }

        return map;
    }

    private static BoolNode ParseAst(JsonElement el)
    {
        var n = new BoolNode { Op = Enum.Parse<BoolOp>(el.GetProperty("op").GetString() ?? "Leaf") };
        if (n.Op == BoolOp.Leaf) n.LeafIndex = el.GetProperty("leaf").GetInt32();
        if (el.TryGetProperty("kids", out var kids))
            foreach (var k in kids.EnumerateArray()) n.Kids.Add(ParseAst(k));
        return n;
    }
}
