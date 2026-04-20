using CSharpCoverage.Core.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpCoverage.Core.Instrumentation;

public sealed class CoverageRewriter : CSharpSyntaxRewriter
{
    private const string Runtime = "global::CSharpCoverage.Runtime.CoverageRuntime";

    private readonly InstrumentationContext _ctx;
    private readonly int _fileId;

    public CoverageRewriter(InstrumentationContext ctx, int fileId) : base(visitIntoStructuredTrivia: false)
    {
        _ctx = ctx;
        _fileId = fileId;
    }

    // ---- statement wrapping ----

    public override SyntaxNode? VisitBlock(BlockSyntax node)
    {
        var visited = (BlockSyntax)base.VisitBlock(node)!;
        var list = new List<StatementSyntax>(visited.Statements.Count * 2);
        foreach (var s in visited.Statements)
        {
            if (ShouldProbe(s))
            {
                var id = _ctx.NextStmtId();
                RecordStatement(id, s);
                list.Add(StmtCall(id).WithLeadingTrivia(s.GetLeadingTrivia()));
                list.Add(s.WithLeadingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.ElasticMarker)));
            }
            else
            {
                list.Add(s);
            }
        }
        return visited.WithStatements(SyntaxFactory.List(list));
    }

    private static bool ShouldProbe(StatementSyntax s)
    {
        // skip local function declarations themselves (their body will be visited)
        return s is not LocalFunctionStatementSyntax
            && s is not LabeledStatementSyntax
            && s is not BlockSyntax;
    }

    // Normalize single-statement bodies → blocks so VisitBlock picks them up.
    public override SyntaxNode? VisitIfStatement(IfStatementSyntax node)
    {
        var n = node.WithStatement(Block(node.Statement));
        if (n.Else != null)
        {
            var elseStmt = n.Else.Statement is IfStatementSyntax ? n.Else.Statement : Block(n.Else.Statement);
            n = n.WithElse(n.Else.WithStatement(elseStmt));
        }
        // Record decision BEFORE visiting children (ID stability).
        int decId = _ctx.NextDecisionId();
        var decEntry = RecordDecision(decId, DecisionKind.If, n.Condition);
        var newCond = WrapDecision(n.Condition, decId, decEntry);
        n = n.WithCondition(newCond);
        return base.VisitIfStatement(n);
    }

    public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
    {
        var n = node.WithStatement(Block(node.Statement));
        int decId = _ctx.NextDecisionId();
        var decEntry = RecordDecision(decId, DecisionKind.While, n.Condition);
        n = n.WithCondition(WrapDecision(n.Condition, decId, decEntry));
        return base.VisitWhileStatement(n);
    }

    public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
    {
        var n = node.WithStatement(Block(node.Statement));
        int decId = _ctx.NextDecisionId();
        var decEntry = RecordDecision(decId, DecisionKind.DoWhile, n.Condition);
        n = n.WithCondition(WrapDecision(n.Condition, decId, decEntry));
        return base.VisitDoStatement(n);
    }

    public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
    {
        var n = node.WithStatement(Block(node.Statement));
        if (n.Condition != null)
        {
            int decId = _ctx.NextDecisionId();
            var decEntry = RecordDecision(decId, DecisionKind.For, n.Condition);
            n = n.WithCondition(WrapDecision(n.Condition, decId, decEntry));
        }
        return base.VisitForStatement(n);
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        var n = node.WithStatement(Block(node.Statement));
        return base.VisitForEachStatement(n);
    }

    public override SyntaxNode? VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        int decId = _ctx.NextDecisionId();
        var decEntry = RecordDecision(decId, DecisionKind.Ternary, node.Condition);
        var newCond = WrapDecision(node.Condition, decId, decEntry);
        var rewritten = node.WithCondition(newCond);
        return base.VisitConditionalExpression(rewritten);
    }

    public override SyntaxNode? VisitSwitchStatement(SwitchStatementSyntax node)
    {
        int decId = _ctx.NextDecisionId();
        var decEntry = RecordDecision(decId, DecisionKind.SwitchStmt, node.Expression);

        var newSections = new List<SwitchSectionSyntax>();
        int caseIdx = 0;
        foreach (var section in node.Sections)
        {
            foreach (var lbl in section.Labels)
            {
                decEntry.Cases.Add(new CaseEntry
                {
                    Index = caseIdx,
                    Line = lbl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    Label = lbl.ToString().Trim()
                });
            }
            var caseCall = SyntaxFactory.ExpressionStatement(CallRuntime("Case",
                SyntaxFactory.Literal(_fileId),
                SyntaxFactory.Literal(decId),
                SyntaxFactory.Literal(caseIdx)));
            var stmts = new List<StatementSyntax> { caseCall };
            stmts.AddRange(section.Statements);
            newSections.Add(section.WithStatements(SyntaxFactory.List(stmts)));
            caseIdx++;
        }
        var replaced = node.WithSections(SyntaxFactory.List(newSections));
        return base.VisitSwitchStatement(replaced);
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var n = ExpandExpressionBody(node);
        return base.VisitMethodDeclaration(n);
    }

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        var n = ExpandExpressionBody(node);
        return base.VisitLocalFunctionStatement(n);
    }

    public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
    {
        if (node.ExpressionBody != null && node.Body == null)
        {
            var ret = node.Kind() == SyntaxKind.GetAccessorDeclaration
                ? (StatementSyntax)MakeReturn(node.ExpressionBody.Expression)
                : SyntaxFactory.ExpressionStatement(node.ExpressionBody.Expression);
            var body = SyntaxFactory.Block(ret);
            node = node.WithExpressionBody(null).WithBody(body)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));
        }
        return base.VisitAccessorDeclaration(node);
    }

    public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        if (node.ExpressionBody != null && node.AccessorList == null)
        {
            var getter = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                .WithBody(SyntaxFactory.Block(MakeReturn(node.ExpressionBody.Expression)));
            var acc = SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(getter));
            node = node.WithExpressionBody(null).WithAccessorList(acc)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));
        }
        return base.VisitPropertyDeclaration(node);
    }

    // ---- helpers ----

    private static BlockSyntax Block(StatementSyntax s)
        => s is BlockSyntax b ? b : SyntaxFactory.Block(s);

    // SyntaxFactory.ReturnStatement(expr) emits the "return" keyword with no
    // trailing trivia. If the source expression had no leading trivia (common
    // for expanded expression-bodied members like `=> _x`), the result renders
    // as "return_x;". Give the expression a single-space leading trivia so the
    // output always has a proper separator.
    private static ReturnStatementSyntax MakeReturn(ExpressionSyntax expr)
        => SyntaxFactory.ReturnStatement(expr.WithLeadingTrivia(SyntaxFactory.Space));

    private static TDecl ExpandExpressionBody<TDecl>(TDecl node) where TDecl : SyntaxNode
    {
        if (node is MethodDeclarationSyntax m && m.ExpressionBody != null && m.Body == null)
        {
            var isVoid = m.ReturnType is PredefinedTypeSyntax p && p.Keyword.IsKind(SyntaxKind.VoidKeyword);
            var stmt = isVoid
                ? (StatementSyntax)SyntaxFactory.ExpressionStatement(m.ExpressionBody.Expression)
                : MakeReturn(m.ExpressionBody.Expression);
            return (TDecl)(object)m.WithExpressionBody(null).WithBody(SyntaxFactory.Block(stmt))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));
        }
        if (node is LocalFunctionStatementSyntax lf && lf.ExpressionBody != null && lf.Body == null)
        {
            var isVoid = lf.ReturnType is PredefinedTypeSyntax pp && pp.Keyword.IsKind(SyntaxKind.VoidKeyword);
            var stmt = isVoid
                ? (StatementSyntax)SyntaxFactory.ExpressionStatement(lf.ExpressionBody.Expression)
                : MakeReturn(lf.ExpressionBody.Expression);
            return (TDecl)(object)lf.WithExpressionBody(null).WithBody(SyntaxFactory.Block(stmt))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));
        }
        return node;
    }

    private void RecordStatement(int id, StatementSyntax s)
    {
        var line = s.GetLocation().GetLineSpan().StartLinePosition;
        _ctx.Map.Statements.Add(new StatementEntry
        {
            FileId = _fileId,
            Id = id,
            Line = line.Line + 1,
            Column = line.Character + 1,
            Text = Truncate(s.ToString(), 120)
        });
    }

    private DecisionEntry RecordDecision(int id, DecisionKind kind, ExpressionSyntax expr)
    {
        var line = expr.GetLocation().GetLineSpan().StartLinePosition;
        var e = new DecisionEntry
        {
            FileId = _fileId,
            Id = id,
            Line = line.Line + 1,
            Column = line.Character + 1,
            Kind = kind,
            Text = Truncate(expr.ToString(), 200)
        };
        _ctx.Map.Decisions.Add(e);
        return e;
    }

    private ExpressionSyntax WrapDecision(ExpressionSyntax expr, int decId, DecisionEntry entry)
    {
        // descend through &&/||/!/paren to find leaves
        int leafCount = 0;
        var ast = BuildAst(expr, entry, ref leafCount);
        entry.Ast = ast;

        // Rewrite the expression by substituting leaves with Cond(...) calls
        var rewriter = new AtomicConditionRewriter(_fileId, decId, entry);
        var rewritten = (ExpressionSyntax)rewriter.Visit(expr)!;

        return CallRuntime("Branch",
            SyntaxFactory.Literal(_fileId),
            SyntaxFactory.Literal(decId),
            rewritten);
    }

    private BoolNode BuildAst(ExpressionSyntax expr, DecisionEntry entry, ref int leafCount)
    {
        expr = Unwrap(expr);
        if (expr is BinaryExpressionSyntax b && (b.IsKind(SyntaxKind.LogicalAndExpression) || b.IsKind(SyntaxKind.LogicalOrExpression)))
        {
            var n = new BoolNode { Op = b.IsKind(SyntaxKind.LogicalAndExpression) ? BoolOp.And : BoolOp.Or };
            n.Kids.Add(BuildAst(b.Left, entry, ref leafCount));
            n.Kids.Add(BuildAst(b.Right, entry, ref leafCount));
            return n;
        }
        if (expr is PrefixUnaryExpressionSyntax u && u.IsKind(SyntaxKind.LogicalNotExpression))
        {
            var n = new BoolNode { Op = BoolOp.Not };
            n.Kids.Add(BuildAst(u.Operand, entry, ref leafCount));
            return n;
        }
        var leafIdx = leafCount++;
        var linePos = expr.GetLocation().GetLineSpan().StartLinePosition;
        entry.Conditions.Add(new ConditionEntry
        {
            Index = leafIdx,
            Line = linePos.Line + 1,
            Column = linePos.Character + 1,
            Text = Truncate(expr.ToString(), 80)
        });
        return new BoolNode { Op = BoolOp.Leaf, LeafIndex = leafIdx };
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax e)
    {
        while (e is ParenthesizedExpressionSyntax p) e = p.Expression;
        return e;
    }

    private sealed class AtomicConditionRewriter : CSharpSyntaxRewriter
    {
        private readonly int _fileId;
        private readonly int _decId;
        private int _nextLeaf = 0;

        public AtomicConditionRewriter(int fileId, int decId, DecisionEntry entry) : base(false)
        {
            _fileId = fileId;
            _decId = decId;
        }

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node == null) return null;
            if (node is ParenthesizedExpressionSyntax p)
            {
                var inner = (ExpressionSyntax)Visit(p.Expression)!;
                return p.WithExpression(inner);
            }
            if (node is BinaryExpressionSyntax b &&
                (b.IsKind(SyntaxKind.LogicalAndExpression) || b.IsKind(SyntaxKind.LogicalOrExpression)))
            {
                var l = (ExpressionSyntax)Visit(b.Left)!;
                var r = (ExpressionSyntax)Visit(b.Right)!;
                return b.WithLeft(l).WithRight(r);
            }
            if (node is PrefixUnaryExpressionSyntax u && u.IsKind(SyntaxKind.LogicalNotExpression))
            {
                var inner = (ExpressionSyntax)Visit(u.Operand)!;
                return u.WithOperand(inner);
            }
            // leaf boolean expression
            if (node is ExpressionSyntax expr)
            {
                int idx = _nextLeaf++;
                return CallRuntimeStatic("Cond",
                    SyntaxFactory.Literal(_fileId),
                    SyntaxFactory.Literal(_decId),
                    SyntaxFactory.Literal(idx),
                    SyntaxFactory.ParenthesizedExpression(expr));
            }
            return base.Visit(node);
        }
    }

    private static InvocationExpressionSyntax CallRuntimeStatic(string method, params object[] literals)
    {
        return CallRuntime(method, literals);
    }

    // ---- runtime call emitter ----

    private static InvocationExpressionSyntax CallRuntime(string method, params object[] literals)
    {
        var args = new List<ArgumentSyntax>();
        foreach (var a in literals)
        {
            ExpressionSyntax e = a switch
            {
                SyntaxToken t => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, t),
                ExpressionSyntax es => es,
                int i => SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(i)),
                _ => throw new InvalidOperationException($"Unsupported literal: {a.GetType()}")
            };
            args.Add(SyntaxFactory.Argument(e));
        }
        var access = SyntaxFactory.ParseExpression(Runtime + "." + method);
        return SyntaxFactory.InvocationExpression((ExpressionSyntax)access,
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args)));
    }

    private StatementSyntax StmtCall(int id)
    {
        return SyntaxFactory.ExpressionStatement(CallRuntime("Stmt",
            SyntaxFactory.Literal(_fileId),
            SyntaxFactory.Literal(id)));
    }

    private static string Truncate(string s, int max)
    {
        s = s.Replace("\r", "").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
