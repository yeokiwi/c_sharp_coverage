using CSharpCoverage.Core.Model;

namespace CSharpCoverage.Core.Instrumentation;

public sealed class InstrumentationContext
{
    public CoverageMap Map { get; } = new();
    private int _nextFileId = 1;
    private int _nextStmtId = 1;
    private int _nextDecisionId = 1;

    public int AllocateFileId(string relativePath, string absolutePath)
    {
        int id = _nextFileId++;
        Map.Files[id] = new FileInfoEntry { Id = id, Path = relativePath, Absolute = absolutePath };
        return id;
    }

    public int NextStmtId() => _nextStmtId++;
    public int NextDecisionId() => _nextDecisionId++;
}
