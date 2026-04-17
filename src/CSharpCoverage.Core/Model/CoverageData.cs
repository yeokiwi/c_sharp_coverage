namespace CSharpCoverage.Core.Model;

public sealed class CoverageData
{
    public Dictionary<int, HashSet<int>> Statements { get; } = new();
    public Dictionary<int, Dictionary<int, BranchCount>> Branches { get; } = new();
    public Dictionary<int, Dictionary<int, Dictionary<int, int>>> Cases { get; } = new();
    public Dictionary<int, Dictionary<int, List<Observation>>> Observations { get; } = new();
}

public sealed class BranchCount
{
    public int Taken { get; set; }
    public int NotTaken { get; set; }
}

public sealed class Observation
{
    public byte[] Values { get; set; } = Array.Empty<byte>();
    public bool Outcome { get; set; }
}
