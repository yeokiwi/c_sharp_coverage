using System.Text;
using System.Text.Json;

namespace CSharpCoverage.Core.Model;

public static class CoverageDataJson
{
    public static CoverageData Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var data = new CoverageData();

        if (root.TryGetProperty("statements", out var stmts))
        {
            foreach (var fileProp in stmts.EnumerateObject())
            {
                int fid = int.Parse(fileProp.Name);
                var set = data.Statements[fid] = new HashSet<int>();
                foreach (var sp in fileProp.Value.EnumerateObject())
                {
                    if (sp.Value.GetInt32() > 0) set.Add(int.Parse(sp.Name));
                }
            }
        }

        if (root.TryGetProperty("branches", out var br))
        {
            foreach (var fp in br.EnumerateObject())
            {
                int fid = int.Parse(fp.Name);
                var inner = data.Branches[fid] = new Dictionary<int, BranchCount>();
                foreach (var dp in fp.Value.EnumerateObject())
                {
                    int did = int.Parse(dp.Name);
                    int t = dp.Value.GetProperty("taken").GetInt32();
                    int n = dp.Value.GetProperty("notTaken").GetInt32();
                    inner[did] = new BranchCount { Taken = t, NotTaken = n };
                }
            }
        }

        if (root.TryGetProperty("cases", out var cs))
        {
            foreach (var fp in cs.EnumerateObject())
            {
                int fid = int.Parse(fp.Name);
                var inner = data.Cases[fid] = new Dictionary<int, Dictionary<int, int>>();
                foreach (var dp in fp.Value.EnumerateObject())
                {
                    int did = int.Parse(dp.Name);
                    var m = inner[did] = new Dictionary<int, int>();
                    foreach (var cp in dp.Value.EnumerateObject())
                        m[int.Parse(cp.Name)] = cp.Value.GetInt32();
                }
            }
        }

        if (root.TryGetProperty("observations", out var obs))
        {
            foreach (var fp in obs.EnumerateObject())
            {
                int fid = int.Parse(fp.Name);
                var inner = data.Observations[fid] = new Dictionary<int, List<Observation>>();
                foreach (var dp in fp.Value.EnumerateObject())
                {
                    int did = int.Parse(dp.Name);
                    var list = inner[did] = new List<Observation>();
                    foreach (var o in dp.Value.EnumerateArray())
                    {
                        var outcome = o.GetProperty("o").GetBoolean();
                        var arr = o.GetProperty("c").EnumerateArray().Select(x => (byte)x.GetInt32()).ToArray();
                        list.Add(new Observation { Outcome = outcome, Values = arr });
                    }
                }
            }
        }

        return data;
    }
}
