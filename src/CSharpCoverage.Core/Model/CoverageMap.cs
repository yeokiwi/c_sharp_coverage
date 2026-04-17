namespace CSharpCoverage.Core.Model;

public sealed class CoverageMap
{
    public Dictionary<int, FileInfoEntry> Files { get; } = new();
    public List<StatementEntry> Statements { get; } = new();
    public List<DecisionEntry> Decisions { get; } = new();
}

public sealed class FileInfoEntry
{
    public int Id { get; set; }
    public string Path { get; set; } = "";
    public string Absolute { get; set; } = "";
}

public sealed class StatementEntry
{
    public int FileId { get; set; }
    public int Id { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string Text { get; set; } = "";
}

public enum DecisionKind
{
    If, While, DoWhile, For, Ternary, SwitchStmt, SwitchExpr, CaseWhen, NullCoalescing, IsPattern
}

public sealed class DecisionEntry
{
    public int FileId { get; set; }
    public int Id { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public DecisionKind Kind { get; set; }
    public string Text { get; set; } = "";
    public List<ConditionEntry> Conditions { get; } = new();
    public List<CaseEntry> Cases { get; } = new();
    public BoolNode? Ast { get; set; }
}

public sealed class ConditionEntry
{
    public int Index { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string Text { get; set; } = "";
}

public sealed class CaseEntry
{
    public int Index { get; set; }
    public int Line { get; set; }
    public string Label { get; set; } = "";
}

public enum BoolOp { Leaf, And, Or, Not }

public sealed class BoolNode
{
    public BoolOp Op { get; set; }
    public int LeafIndex { get; set; } = -1;
    public List<BoolNode> Kids { get; } = new();
}
