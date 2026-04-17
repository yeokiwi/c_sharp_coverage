using CSharpCoverage.Core.Model;

namespace CSharpCoverage.Core.Analysis;

public enum McdcMode { UniqueCause, Masking }

public sealed class DecisionCoverage
{
    public int DecisionId;
    public int FileId;
    public int ConditionCount;
    public int CoveredConditions;
    public List<(int condIdx, int obsA, int obsB)> Proofs { get; } = new();
    public bool DecisionTaken;
    public bool DecisionNotTaken;
    public bool FullyCovered => DecisionTaken && DecisionNotTaken && CoveredConditions == ConditionCount;
}

public sealed class McdcReport
{
    public Dictionary<(int file, int dec), DecisionCoverage> Decisions { get; } = new();
    public int TotalConditions => Decisions.Values.Sum(d => d.ConditionCount);
    public int CoveredConditions => Decisions.Values.Sum(d => d.CoveredConditions);
    public double Ratio => TotalConditions == 0 ? 1.0 : (double)CoveredConditions / TotalConditions;
}

public static class MCDCAnalyzer
{
    public static McdcReport Analyze(CoverageMap map, CoverageData data, McdcMode mode)
    {
        var r = new McdcReport();
        foreach (var d in map.Decisions)
        {
            if (d.Conditions.Count == 0) continue;
            var dc = new DecisionCoverage { FileId = d.FileId, DecisionId = d.Id, ConditionCount = d.Conditions.Count };

            if (data.Branches.TryGetValue(d.FileId, out var br) && br.TryGetValue(d.Id, out var bc))
            {
                dc.DecisionTaken = bc.Taken > 0;
                dc.DecisionNotTaken = bc.NotTaken > 0;
            }

            List<Observation> obs = new();
            if (data.Observations.TryGetValue(d.FileId, out var fo) && fo.TryGetValue(d.Id, out var list))
                obs = list;

            // pad observations so length == ConditionCount
            foreach (var o in obs)
            {
                if (o.Values.Length < d.Conditions.Count)
                {
                    var padded = new byte[d.Conditions.Count];
                    Array.Copy(o.Values, padded, o.Values.Length);
                    o.Values = padded;
                }
            }

            for (int i = 0; i < d.Conditions.Count; i++)
            {
                if (TryFindPair(obs, i, d.Conditions.Count, d.Ast, mode, out int a, out int b))
                {
                    dc.CoveredConditions++;
                    dc.Proofs.Add((i, a, b));
                }
            }

            r.Decisions[(d.FileId, d.Id)] = dc;
        }
        return r;
    }

    private static bool TryFindPair(
        List<Observation> obs, int target, int condCount, BoolNode? ast, McdcMode mode,
        out int aIdx, out int bIdx)
    {
        aIdx = bIdx = -1;
        for (int a = 0; a < obs.Count; a++)
        {
            var oa = obs[a];
            if (oa.Values.Length <= target || oa.Values[target] == 0) continue;
            for (int b = a + 1; b < obs.Count; b++)
            {
                var ob = obs[b];
                if (ob.Values.Length <= target || ob.Values[target] == 0) continue;
                if (oa.Values[target] == ob.Values[target]) continue;
                if (oa.Outcome == ob.Outcome) continue;

                if (mode == McdcMode.UniqueCause)
                {
                    bool ok = true;
                    for (int j = 0; j < condCount; j++)
                    {
                        if (j == target) continue;
                        if (oa.Values[j] != ob.Values[j]) { ok = false; break; }
                    }
                    if (ok) { aIdx = a; bIdx = b; return true; }
                }
                else // Masking
                {
                    if (ast == null)
                    {
                        // fallback: treat differing-but-not-evaluated as masked, else require equal
                        bool ok = true;
                        for (int j = 0; j < condCount; j++)
                        {
                            if (j == target) continue;
                            var va = oa.Values[j];
                            var vb = ob.Values[j];
                            if (va == 0 || vb == 0) continue; // one masked
                            if (va != vb) { ok = false; break; }
                        }
                        if (ok) { aIdx = a; bIdx = b; return true; }
                    }
                    else if (IsMaskingCompatible(ast, oa, ob, target))
                    {
                        aIdx = a; bIdx = b; return true;
                    }
                }
            }
        }
        return false;
    }

    private static bool IsMaskingCompatible(BoolNode ast, Observation a, Observation b, int target)
    {
        // Target condition must not be masked in either observation.
        if (IsMasked(ast, a.Values, target) || IsMasked(ast, b.Values, target)) return false;

        // For every other condition j that differs (both evaluated and different), it must
        // be masked in at least one observation.
        for (int j = 0; j < a.Values.Length; j++)
        {
            if (j == target) continue;
            var va = j < a.Values.Length ? a.Values[j] : (byte)0;
            var vb = j < b.Values.Length ? b.Values[j] : (byte)0;
            if (va == 0 || vb == 0) continue; // already masked somewhere
            if (va == vb) continue;
            if (!IsMasked(ast, a.Values, j) && !IsMasked(ast, b.Values, j)) return false;
        }
        return true;
    }

    // Walk AST with tri-state values; return true if target leaf is "masked"
    // (its value cannot affect the decision outcome because another operand short-circuited).
    private static bool IsMasked(BoolNode root, byte[] values, int targetLeaf)
    {
        return EvalMask(root, values, targetLeaf) == MaskResult.Masked;
    }

    private enum MaskResult { Contributes, Masked, NotPresent }

    private static MaskResult EvalMask(BoolNode n, byte[] values, int target)
    {
        if (n.Op == BoolOp.Leaf)
            return n.LeafIndex == target ? MaskResult.Contributes : MaskResult.NotPresent;
        if (n.Op == BoolOp.Not)
            return EvalMask(n.Kids[0], values, target);
        if (n.Op == BoolOp.And)
        {
            var left = n.Kids[0]; var right = n.Kids[1];
            var lContains = Contains(left, target); var rContains = Contains(right, target);
            if (!lContains && !rContains) return MaskResult.NotPresent;
            var lVal = Eval(left, values);
            if (lContains)
            {
                var l = EvalMask(left, values, target);
                if (l == MaskResult.Masked) return MaskResult.Masked;
                // left contributes → it must propagate: need right to be short-circuited (left=false masks target only if target is on right side)
                if (lVal == 2 /* false */) return MaskResult.Contributes;
                // left=true: right is evaluated; target is on left → contributes iff right != false makes it observable → actually for AND, left contributes only when right is true
                var rVal = Eval(right, values);
                return rVal == 1 ? MaskResult.Contributes : MaskResult.Masked;
            }
            // target on right only
            if (lVal == 2) return MaskResult.Masked; // left false short-circuits → right masked
            return EvalMask(right, values, target);
        }
        if (n.Op == BoolOp.Or)
        {
            var left = n.Kids[0]; var right = n.Kids[1];
            var lContains = Contains(left, target); var rContains = Contains(right, target);
            if (!lContains && !rContains) return MaskResult.NotPresent;
            var lVal = Eval(left, values);
            if (lContains)
            {
                var l = EvalMask(left, values, target);
                if (l == MaskResult.Masked) return MaskResult.Masked;
                if (lVal == 1) return MaskResult.Contributes; // OR true short-circuits right; left propagates
                var rVal = Eval(right, values);
                return rVal == 2 ? MaskResult.Contributes : MaskResult.Masked;
            }
            if (lVal == 1) return MaskResult.Masked; // left true short-circuits → right masked
            return EvalMask(right, values, target);
        }
        return MaskResult.NotPresent;
    }

    private static bool Contains(BoolNode n, int target)
    {
        if (n.Op == BoolOp.Leaf) return n.LeafIndex == target;
        foreach (var k in n.Kids) if (Contains(k, target)) return true;
        return false;
    }

    // Evaluate node with tri-state: returns 0 (unknown), 1 (true), 2 (false)
    private static byte Eval(BoolNode n, byte[] values)
    {
        if (n.Op == BoolOp.Leaf)
            return n.LeafIndex >= 0 && n.LeafIndex < values.Length ? values[n.LeafIndex] : (byte)0;
        if (n.Op == BoolOp.Not)
        {
            var v = Eval(n.Kids[0], values);
            return v == 1 ? (byte)2 : v == 2 ? (byte)1 : (byte)0;
        }
        if (n.Op == BoolOp.And)
        {
            var l = Eval(n.Kids[0], values);
            if (l == 2) return 2;
            var r = Eval(n.Kids[1], values);
            if (r == 2) return 2;
            if (l == 1 && r == 1) return 1;
            return 0;
        }
        if (n.Op == BoolOp.Or)
        {
            var l = Eval(n.Kids[0], values);
            if (l == 1) return 1;
            var r = Eval(n.Kids[1], values);
            if (r == 1) return 1;
            if (l == 2 && r == 2) return 2;
            return 0;
        }
        return 0;
    }
}
