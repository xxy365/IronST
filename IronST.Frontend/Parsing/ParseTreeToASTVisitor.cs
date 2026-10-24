using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using IronST.Common.Diagnostics;
using IronST.Common.Symbol;
using IronST.Frontend.AST;
using IronST.Frontend.Generated;

namespace IronST.Frontend.Parsing;

internal class ParseTreeToASTVisitor : STParserBaseVisitor<ASTNode>
{
    private readonly string? _fileName;

    public ParseTreeToASTVisitor(string? fileName = null) => _fileName = fileName;

    // ── 工具 ──

    private SourceSpan Span(ParserRuleContext ctx) =>
        new(
            ctx.Start.Line,
            ctx.Start.Column + 1,
            ctx.Stop?.Line ?? ctx.Start.Line,
            (ctx.Stop?.Column ?? ctx.Start.Column) + (ctx.Stop?.Text?.Length ?? 1),
            _fileName
        );

    private T SetSpan<T>(T node, ParserRuleContext ctx)
        where T : ASTNode
    {
        node.Span = Span(ctx);
        return node;
    }

    private static List<int> CollectOperatorTypes(ParserRuleContext ctx, params int[] tokenTypes)
    {
        var set = new HashSet<int>(tokenTypes);
        var result = new List<int>();
        foreach (var child in ctx.children)
            if (child is ITerminalNode t && set.Contains(t.Symbol.Type))
                result.Add(t.Symbol.Type);
        return result;
    }

    private static long ParseBasedInteger(string text)
    {
        var parts = text.Split('#', 2);
        int baseVal = int.Parse(parts[0]);
        string digits = parts[1].Replace("_", "");
        return Convert.ToInt64(digits, baseVal);
    }

    // ══════════════════════════════════════════
    //  顶层
    // ══════════════════════════════════════════

    public override ASTNode VisitCompilationUnit(STParser.CompilationUnitContext ctx)
    {
        var node = SetSpan(new CompilationUnitNode(), ctx);

        foreach (var child in ctx.children)
        {
            switch (child)
            {
                case STParser.TypeDeclContext td:
                    var typeIds = td.IDENTIFIER();
                    var typeDefs = td.typeDef();
                    for (int i = 0; i < typeIds.Length; i++)
                    {
                        var typeNode = ASTFactory.TypeDecl(
                            Span(td),
                            typeIds[i].GetText(),
                            (TypeDefNode)Visit(typeDefs[i])
                        );
                        node.TypeDeclarations.Add(typeNode);
                    }
                    break;
                case STParser.ProgramDeclContext pd:
                    node.POUs.Add((ProgramNode)Visit(pd));
                    break;
                case STParser.FunctionDeclContext fd:
                    node.POUs.Add((FunctionNode)Visit(fd));
                    break;
                case STParser.FunctionBlockDeclContext fbd:
                    node.POUs.Add((FunctionBlockNode)Visit(fbd));
                    break;
                case STParser.ConfigurationDeclContext cd:
                    node.Configurations.Add((ConfigurationNode)Visit(cd));
                    break;
            }
        }

        return node;
    }

    public override ASTNode VisitFunctionDecl(STParser.FunctionDeclContext ctx)
    {
        var node = SetSpan(new FunctionNode(), ctx);
        node.Name = ctx.IDENTIFIER().GetText();
        node.ReturnTypeName = ctx.typeName().GetText();
        foreach (var vb in ctx.varDeclBlock())
            node.VarBlocks.Add((VarDeclBlockNode)Visit(vb));
        if (ctx.statementList() != null)
            foreach (var s in ctx.statementList().statement())
                node.Body.Add((StatementNode)Visit(s));
        return node;
    }

    public override ASTNode VisitProgramDecl(STParser.ProgramDeclContext ctx)
    {
        var node = SetSpan(new ProgramNode(), ctx);
        node.Name = ctx.IDENTIFIER().GetText();
        foreach (var vb in ctx.varDeclBlock())
            node.VarBlocks.Add((VarDeclBlockNode)Visit(vb));
        if (ctx.statementList() != null)
            foreach (var s in ctx.statementList().statement())
                node.Body.Add((StatementNode)Visit(s));
        return node;
    }

    public override ASTNode VisitFunctionBlockDecl(STParser.FunctionBlockDeclContext ctx)
    {
        var node = SetSpan(new FunctionBlockNode(), ctx);
        node.Name = ctx.IDENTIFIER().GetText();
        foreach (var vb in ctx.varDeclBlock())
            node.VarBlocks.Add((VarDeclBlockNode)Visit(vb));

        foreach (var child in ctx.children)
        {
            switch (child)
            {
                case STParser.StatementContext sc:
                    node.BodyItems.Add(new FBStatementItemNode((StatementNode)Visit(sc)));
                    break;
                case STParser.ActionDeclContext ac:
                    node.BodyItems.Add(new FBActionItemNode((ActionNode)Visit(ac)));
                    break;
            }
        }
        return node;
    }

    public override ASTNode VisitActionDecl(STParser.ActionDeclContext ctx)
    {
        var node = SetSpan(new ActionNode(), ctx);
        node.Name = ctx.IDENTIFIER().GetText();
        if (ctx.statementList() != null)
            foreach (var s in ctx.statementList().statement())
                node.Body.Add((StatementNode)Visit(s));
        return node;
    }

    // ══════════════════════════════════════════
    //  Configuration 层
    // ══════════════════════════════════════════

    public override ASTNode VisitConfigurationDecl(STParser.ConfigurationDeclContext ctx)
    {
        var node = SetSpan(new ConfigurationNode(), ctx);
        node.Name = ctx.IDENTIFIER().GetText();
        foreach (var vb in ctx.varDeclBlock())
            node.VarBlocks.Add((VarDeclBlockNode)Visit(vb));
        foreach (var rd in ctx.resourceDecl())
            node.Resources.Add((ResourceNode)Visit(rd));
        return node;
    }

    public override ASTNode VisitResourceDecl(STParser.ResourceDeclContext ctx)
    {
        var node = SetSpan(new ResourceNode(), ctx);
        node.Name = ctx.GetToken(STParser.IDENTIFIER, 0).GetText();
        node.TaskControllerName = ctx.GetToken(STParser.IDENTIFIER, 1).GetText();
        foreach (var vb in ctx.varDeclBlock())
            node.VarBlocks.Add((VarDeclBlockNode)Visit(vb));
        foreach (var td in ctx.taskDecl())
            node.Tasks.Add((TaskNode)Visit(td));
        foreach (var pc in ctx.programConfig())
            node.ProgramConfigs.Add((ProgramConfigNode)Visit(pc));
        return node;
    }

    public override ASTNode VisitTaskDecl(STParser.TaskDeclContext ctx)
    {
        var node = SetSpan(new TaskNode(), ctx);
        node.Name = ctx.IDENTIFIER().GetText();
        foreach (var tp in ctx.taskParam())
        {
            var param = ASTFactory.Param(Span(tp), (ExpressionNode)Visit(tp.expression()));
            param.Name = tp.IDENTIFIER().GetText();
            node.Parameters.Add(param);
        }
        return node;
    }

    public override ASTNode VisitProgramConfig(STParser.ProgramConfigContext ctx)
    {
        var node = SetSpan(new ProgramConfigNode(), ctx);
        int idx = 0;
        node.ConfigName = ctx.GetToken(STParser.IDENTIFIER, idx++).GetText();
        if (ctx.WITH() != null)
            node.TaskBinding = ctx.GetToken(STParser.IDENTIFIER, idx++).GetText();
        node.ProgramTypeName = ctx.GetToken(STParser.IDENTIFIER, idx).GetText();
        foreach (var pcp in ctx.programConfigParam())
        {
            var param = ASTFactory.Param(Span(pcp), (ExpressionNode)Visit(pcp.expression()));
            param.Name = pcp.IDENTIFIER().GetText();
            node.Parameters.Add(param);
        }
        return node;
    }

    // ══════════════════════════════════════════
    //  变量声明
    // ══════════════════════════════════════════

    public override ASTNode VisitVarDeclBlock(STParser.VarDeclBlockContext ctx)
    {
        var node = SetSpan(new VarDeclBlockNode(), ctx);
        if (ctx.varScope() != null)
        {
            node.Scope = ctx.varScope().GetText().ToUpperInvariant() switch
            {
                "VAR" => VarScope.Local,
                "VAR_INPUT" => VarScope.Input,
                "VAR_OUTPUT" => VarScope.Output,
                "VAR_IN_OUT" => VarScope.InOut,
                "VAR_GLOBAL" => VarScope.Global,
                "VAR_EXTERNAL" => VarScope.External,
                "VAR_TEMP" => VarScope.Temp,
                "VAR_ACCESS" => VarScope.Access,
                _ => VarScope.Local,
            };
        }
        if (ctx.varModifier() != null)
        {
            node.Modifier = ctx.varModifier().GetText().ToUpperInvariant() switch
            {
                "CONSTANT" => VarModifier.Constant,
                "RETAIN" => VarModifier.Retain,
                "NON_RETAIN" => VarModifier.NonRetain,
                _ => VarModifier.None,
            };
        }

        // VAR_CONFIG 块使用 varConfigItem 语法，与普通 varDeclItem 不同
        if (ctx.varConfigItem().Length > 0)
        {
            node.IsConfigBlock = true;
            foreach (var ci in ctx.varConfigItem())
                node.ConfigItems.Add((VarConfigItemNode)Visit(ci));
        }
        else
        {
            foreach (var item in ctx.varDeclItem())
                node.Items.Add((VarDeclItemNode)Visit(item));
        }
        return node;
    }

    public override ASTNode VisitVarDeclItem(STParser.VarDeclItemContext ctx)
    {
        var node = ASTFactory.VarDeclItem(Span(ctx), (DataTypeNode)Visit(ctx.dataType()));
        foreach (var vn in ctx.varName())
            node.Names.Add((VarNameNode)Visit(vn));
        if (ctx.initialValue() != null)
            // 初始值可能是表达式、结构体初始化、数组初始化中的任意一种，
            // 保持 ASTNode? 类型，不强转为 ExpressionNode
            node.InitialValue = Visit(ctx.initialValue());
        return node;
    }

    public override ASTNode VisitVarName(STParser.VarNameContext ctx)
    {
        var node = SetSpan(new VarNameNode(), ctx);
        node.Name = ctx.IDENTIFIER().GetText();
        if (ctx.DIRECT_VARIABLE() != null)
            node.DirectAddress = ctx.DIRECT_VARIABLE().GetText();
        return node;
    }

    public override ASTNode VisitVarConfigItem(STParser.VarConfigItemContext ctx)
    {
        if (ctx.varDeclItem() != null)
        {
            var varDecl = (VarDeclItemNode)Visit(ctx.varDeclItem());
            var node = SetSpan(new VarConfigItemNode(), ctx);
            node.Name = varDecl.Names.FirstOrDefault()?.Name ?? "";
            node.TypeName = varDecl.TypeName;
            node.InitialValue = varDecl.InitialValue as ExpressionNode;
            node.DirectAddress = varDecl.Names.FirstOrDefault()?.DirectAddress;
            return node;
        }

        var configNode = SetSpan(new VarConfigItemNode(), ctx);
        var idTokens = ctx.IDENTIFIER();
        var parts = new string[idTokens.Length];
        for (int i = 0; i < idTokens.Length; i++)
            parts[i] = idTokens[i].GetText();
        configNode.Name = string.Join(".", parts);
        if (ctx.initialValue() != null)
            configNode.InitialValue = (ExpressionNode)Visit(ctx.initialValue());
        return configNode;
    }

    public override ASTNode VisitInitialValue(STParser.InitialValueContext ctx)
    {
        if (ctx.expression() != null)
            return Visit(ctx.expression());
        if (ctx.structInitValue() != null)
            return Visit(ctx.structInitValue());
        if (ctx.arrayInitValue() != null)
            return Visit(ctx.arrayInitValue());
        return ASTFactory.Int(Span(ctx), "0", 0);
    }

    // ══════════════════════════════════════════
    //  类型声明 & 类型引用
    // ══════════════════════════════════════════

    public override ASTNode VisitStructInitValue(STParser.StructInitValueContext ctx)
    {
        var node = SetSpan(new StructInitValueNode(), ctx);
        var names = ctx.IDENTIFIER();
        var values = ctx.initialValue();
        for (int i = 0; i < names.Length && i < values.Length; i++)
        {
            var value = Visit(values[i]);
            node.Fields.Add((names[i].GetText(), value));
        }
        return node;
    }

    public override ASTNode VisitArrayInitValue(STParser.ArrayInitValueContext ctx)
    {
        var node = SetSpan(new ArrayInitValueNode(), ctx);
        foreach (var valCtx in ctx.initialValue())
            node.Values.Add(Visit(valCtx));
        return node;
    }

    public override ASTNode VisitTypeDecl(STParser.TypeDeclContext ctx)
    {
        var ids = ctx.IDENTIFIER();
        var defs = ctx.typeDef();
        return ASTFactory.TypeDecl(Span(ctx), ids[0].GetText(), (TypeDefNode)Visit(defs[0]));
    }

    public override ASTNode VisitTypeDef(STParser.TypeDefContext ctx)
    {
        if (ctx.structDef() != null)
            return Visit(ctx.structDef());
        if (ctx.enumDef() != null)
            return Visit(ctx.enumDef());
        if (ctx.subrangeDef() != null)
            return Visit(ctx.subrangeDef());
        if (ctx.dataType() != null)
        {
            var node = ASTFactory.SimpleTypeDef((DataTypeNode)Visit(ctx.dataType()));
            node.Span = Span(ctx);
            return node;
        }
        throw new InvalidOperationException("Unknown type definition");
    }

    public override ASTNode VisitStructDef(STParser.StructDefContext ctx)
    {
        var node = SetSpan(new StructDefNode(), ctx);
        foreach (var item in ctx.varDeclItem())
            node.Members.Add((VarDeclItemNode)Visit(item));
        return node;
    }

    public override ASTNode VisitEnumDef(STParser.EnumDefContext ctx)
    {
        var node = SetSpan(new EnumDefNode(), ctx);
        foreach (var itemCtx in ctx.enumItem())
        {
            var item = SetSpan(new EnumItemNode(), itemCtx);
            item.Name = itemCtx.IDENTIFIER().GetText();
            if (itemCtx.expression() != null)
                item.ExplicitValue = (ExpressionNode)Visit(itemCtx.expression());
            node.Items.Add(item);
        }
        return node;
    }

    public override ASTNode VisitSubrangeDef(STParser.SubrangeDefContext ctx)
    {
        var node = SetSpan(new SubrangeDefNode(), ctx);
        node.BaseTypeName = ctx.typeName().GetText();
        var bounds = ctx.subrangeBound();
        node.LowerBound = ParseSubrangeBound(bounds[0]);
        node.UpperBound = ParseSubrangeBound(bounds[1]);
        return node;
    }

    /// <summary>
    /// 解析子范围边界值为 long。
    /// 使用 long.Parse 避免 int 溢出，与 AST 节点中 long 类型字段保持一致。
    /// </summary>
    private static long ParseSubrangeBound(STParser.SubrangeBoundContext ctx)
    {
        var text = ctx.INTEGER_LITERAL()?.GetText() ?? ctx.BASED_INTEGER()?.GetText() ?? "0";
        int sign = ctx.MINUS() != null ? -1 : 1;
        return sign * long.Parse(text.Replace("_", ""));
    }

    public override ASTNode VisitDataType(STParser.DataTypeContext ctx)
    {
        if (ctx.ARRAY() != null)
        {
            var node = ASTFactory.ArrayDataType(Span(ctx), (DataTypeNode)Visit(ctx.dataType()));
            foreach (var sr in ctx.subrange())
            {
                var bounds = sr.subrangeBound();
                var lower = (ExpressionNode)Visit(bounds[0]);
                var upper = (ExpressionNode)Visit(bounds[1]);
                node.Dimensions.Add((lower, upper));
            }
            return node;
        }

        if (ctx.REFERENCE() != null)
        {
            var node = SetSpan(new ReferenceDataTypeNode(), ctx);
            node.ReferencedTypeName = ctx.typeName().GetText();
            return node;
        }

        {
            var node = SetSpan(new SimpleDataTypeNode(), ctx);
            node.TypeName = ctx.typeName().GetText();
            if (ctx.expression() != null)
                node.SizeParam = (ExpressionNode)Visit(ctx.expression());
            return node;
        }
    }

    public override ASTNode VisitSubrangeBound(STParser.SubrangeBoundContext ctx)
    {
        string raw;
        long val;
        if (ctx.INTEGER_LITERAL() != null)
        {
            raw = ctx.INTEGER_LITERAL().GetText();
            if (!long.TryParse(raw.Replace("_", ""), out val))
                val = 0;
        }
        else if (ctx.BASED_INTEGER() != null)
        {
            raw = ctx.BASED_INTEGER().GetText();
            try
            {
                val = ParseBasedInteger(raw);
            }
            catch (OverflowException)
            {
                val = 0;
            }
        }
        else
        {
            raw = "0";
            val = 0;
        }
        if (ctx.MINUS() != null)
        {
            val = -val;
            raw = "-" + raw;
        }
        return ASTFactory.Int(Span(ctx), raw, val);
    }

    // ══════════════════════════════════════════
    //  语句
    // ══════════════════════════════════════════

    public override ASTNode VisitStatement(STParser.StatementContext ctx)
    {
        if (ctx.assignmentStmt() != null)
            return Visit(ctx.assignmentStmt());
        if (ctx.refAssignStmt() != null)
            return Visit(ctx.refAssignStmt());
        if (ctx.callStmt() != null)
            return Visit(ctx.callStmt());
        if (ctx.returnStmt() != null)
            return Visit(ctx.returnStmt());
        if (ctx.exitStmt() != null)
            return Visit(ctx.exitStmt());
        if (ctx.ifStmt() != null)
            return Visit(ctx.ifStmt());
        if (ctx.forStmt() != null)
            return Visit(ctx.forStmt());
        if (ctx.whileStmt() != null)
            return Visit(ctx.whileStmt());
        if (ctx.repeatStmt() != null)
            return Visit(ctx.repeatStmt());
        if (ctx.caseStmt() != null)
            return Visit(ctx.caseStmt());
        throw new InvalidOperationException("Unknown statement type");
    }

    public override ASTNode VisitAssignmentStmt(STParser.AssignmentStmtContext ctx) =>
        ASTFactory.Assignment(
            Span(ctx),
            (ExpressionNode)Visit(ctx.variable()),
            (ExpressionNode)Visit(ctx.expression())
        );

    public override ASTNode VisitRefAssignStmt(STParser.RefAssignStmtContext ctx) =>
        ASTFactory.RefAssignment(
            Span(ctx),
            (ExpressionNode)Visit(ctx.variable()),
            (ExpressionNode)Visit(ctx.expression())
        );

    public override ASTNode VisitCallStmt(STParser.CallStmtContext ctx)
    {
        var node = SetSpan(new CallStatementNode(), ctx);
        if (ctx.builtinType() != null)
            node.Name = ctx.builtinType().GetText();
        else
            node.Name = ctx.variable().GetText();
        if (ctx.paramList() != null)
            foreach (var p in ctx.paramList().paramAssignment())
                node.Arguments.Add((CallParamNode)Visit(p));
        return node;
    }

    public override ASTNode VisitReturnStmt(STParser.ReturnStmtContext ctx) =>
        ASTFactory.Return(Span(ctx));

    public override ASTNode VisitExitStmt(STParser.ExitStmtContext ctx) =>
        ASTFactory.Exit(Span(ctx));

    public override ASTNode VisitIfStmt(STParser.IfStmtContext ctx)
    {
        var exprs = ctx.expression();
        var stmtLists = ctx.statementList();

        var node = ASTFactory.If(Span(ctx), (ExpressionNode)Visit(exprs[0]));

        foreach (var s in stmtLists[0].statement())
            node.ThenBody.Add((StatementNode)Visit(s));

        int elsifCount = exprs.Length - 1;
        for (int i = 0; i < elsifCount; i++)
        {
            var cond = (ExpressionNode)Visit(exprs[i + 1]);
            var body = new List<StatementNode>();
            foreach (var s in stmtLists[i + 1].statement())
                body.Add((StatementNode)Visit(s));
            node.ElsIfBranches.Add((cond, body));
        }

        if (stmtLists.Length > exprs.Length)
        {
            node.ElseBody = new List<StatementNode>();
            foreach (var s in stmtLists[^1].statement())
                node.ElseBody.Add((StatementNode)Visit(s));
        }
        return node;
    }

    public override ASTNode VisitForStmt(STParser.ForStmtContext ctx)
    {
        var exprs = ctx.expression();
        var node = ASTFactory.For(
            Span(ctx),
            ctx.IDENTIFIER().GetText(),
            (ExpressionNode)Visit(exprs[0]),
            (ExpressionNode)Visit(exprs[1])
        );
        if (exprs.Length > 2)
            node.By = (ExpressionNode)Visit(exprs[2]);
        foreach (var s in ctx.statementList().statement())
            node.Body.Add((StatementNode)Visit(s));
        return node;
    }

    public override ASTNode VisitWhileStmt(STParser.WhileStmtContext ctx)
    {
        var node = ASTFactory.While(Span(ctx), (ExpressionNode)Visit(ctx.expression()));
        foreach (var s in ctx.statementList().statement())
            node.Body.Add((StatementNode)Visit(s));
        return node;
    }

    public override ASTNode VisitRepeatStmt(STParser.RepeatStmtContext ctx)
    {
        var node = SetSpan(new RepeatNode(), ctx);
        foreach (var s in ctx.statementList().statement())
            node.Body.Add((StatementNode)Visit(s));
        node.UntilCondition = (ExpressionNode)Visit(ctx.expression());
        return node;
    }

    public override ASTNode VisitCaseStmt(STParser.CaseStmtContext ctx)
    {
        var node = ASTFactory.Case(Span(ctx), (ExpressionNode)Visit(ctx.expression()));
        foreach (var elem in ctx.caseElement())
            node.Branches.Add((CaseBranchNode)Visit(elem));
        if (ctx.statementList() != null)
        {
            node.ElseBody = new List<StatementNode>();
            foreach (var s in ctx.statementList().statement())
                node.ElseBody.Add((StatementNode)Visit(s));
        }
        return node;
    }

    public override ASTNode VisitCaseElement(STParser.CaseElementContext ctx)
    {
        var node = SetSpan(new CaseBranchNode(), ctx);
        foreach (var lbl in ctx.caseLabel())
            node.Labels.Add((CaseLabelNode)Visit(lbl));
        foreach (var s in ctx.statementList().statement())
            node.Body.Add((StatementNode)Visit(s));
        return node;
    }

    public override ASTNode VisitCaseLabel(STParser.CaseLabelContext ctx)
    {
        var node = SetSpan(new CaseLabelNode(), ctx);
        var bounds = ctx.subrangeBound();
        if (bounds.Length > 0)
        {
            if (bounds.Length == 1)
                node.SingleValue = ParseBoundValue(bounds[0]);
            else
            {
                node.RangeStart = ParseBoundValue(bounds[0]);
                node.RangeEnd = ParseBoundValue(bounds[1]);
            }
            return node;
        }

        var idTokens = ctx.IDENTIFIER();
        var parts = new string[idTokens.Length];
        for (int i = 0; i < idTokens.Length; i++)
            parts[i] = idTokens[i].GetText();
        node.EnumValue = string.Join(".", parts);
        return node;
    }

    private static long ParseBoundValue(STParser.SubrangeBoundContext ctx)
    {
        long val;
        if (ctx.INTEGER_LITERAL() != null)
        {
            if (!long.TryParse(ctx.INTEGER_LITERAL().GetText().Replace("_", ""), out val))
                val = 0;
        }
        else if (ctx.BASED_INTEGER() != null)
        {
            try
            {
                val = ParseBasedInteger(ctx.BASED_INTEGER().GetText());
            }
            catch (OverflowException)
            {
                val = 0;
            }
        }
        else
            val = 0;
        return ctx.MINUS() != null ? -val : val;
    }

    public override ASTNode VisitParamAssignment(STParser.ParamAssignmentContext ctx)
    {
        if (ctx.RARROW() != null)
        {
            var node = ASTFactory.Param(Span(ctx), (ExpressionNode)Visit(ctx.variable()));
            node.Name = ctx.IDENTIFIER().GetText();
            node.IsOutputBinding = true;
            return node;
        }
        if (ctx.ASSIGN() != null)
        {
            var node = ASTFactory.Param(Span(ctx), (ExpressionNode)Visit(ctx.expression()));
            node.Name = ctx.IDENTIFIER().GetText();
            return node;
        }
        return ASTFactory.Param(Span(ctx), (ExpressionNode)Visit(ctx.expression()));
    }

    // ══════════════════════════════════════════
    //  表达式
    // ══════════════════════════════════════════

    public override ASTNode VisitExpression(STParser.ExpressionContext ctx)
    {
        var operands = ctx.xorExpr();
        if (operands.Length == 1)
            return Visit(operands[0]);
        var left = (ExpressionNode)Visit(operands[0]);
        for (int i = 1; i < operands.Length; i++)
        {
            var right = (ExpressionNode)Visit(operands[i]);
            left = ASTFactory.Binary(BinaryOp.Or, left, right);
        }
        return left;
    }

    public override ASTNode VisitXorExpr(STParser.XorExprContext ctx)
    {
        var operands = ctx.andExpr();
        if (operands.Length == 1)
            return Visit(operands[0]);
        var left = (ExpressionNode)Visit(operands[0]);
        for (int i = 1; i < operands.Length; i++)
        {
            var right = (ExpressionNode)Visit(operands[i]);
            left = ASTFactory.Binary(BinaryOp.Xor, left, right);
        }
        return left;
    }

    public override ASTNode VisitAndExpr(STParser.AndExprContext ctx)
    {
        var operands = ctx.comparisonExpr();
        if (operands.Length == 1)
            return Visit(operands[0]);
        var left = (ExpressionNode)Visit(operands[0]);
        for (int i = 1; i < operands.Length; i++)
        {
            var right = (ExpressionNode)Visit(operands[i]);
            left = ASTFactory.Binary(BinaryOp.And, left, right);
        }
        return left;
    }

    public override ASTNode VisitComparisonExpr(STParser.ComparisonExprContext ctx)
    {
        var operands = ctx.addExpr();
        if (operands.Length == 1 || ctx.compOp() == null)
            return Visit(operands[0]);
        var op = ctx.compOp().GetText().ToUpperInvariant() switch
        {
            "=" => BinaryOp.Eq,
            "<>" => BinaryOp.Neq,
            "<" => BinaryOp.Lt,
            ">" => BinaryOp.Gt,
            "<=" => BinaryOp.Lte,
            ">=" => BinaryOp.Gte,
            _ => BinaryOp.Eq,
        };
        return ASTFactory.Binary(
            op,
            (ExpressionNode)Visit(operands[0]),
            (ExpressionNode)Visit(operands[1])
        );
    }

    public override ASTNode VisitAddExpr(STParser.AddExprContext ctx)
    {
        var operands = ctx.mulExpr();
        if (operands.Length == 1)
            return Visit(operands[0]);
        var ops = CollectOperatorTypes(ctx, STParser.PLUS, STParser.MINUS);
        var left = (ExpressionNode)Visit(operands[0]);
        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i] == STParser.PLUS ? BinaryOp.Add : BinaryOp.Sub;
            var right = (ExpressionNode)Visit(operands[i + 1]);
            left = ASTFactory.Binary(op, left, right);
        }
        return left;
    }

    public override ASTNode VisitMulExpr(STParser.MulExprContext ctx)
    {
        var operands = ctx.unaryExpr();
        if (operands.Length == 1)
            return Visit(operands[0]);
        var ops = CollectOperatorTypes(ctx, STParser.STAR, STParser.SLASH, STParser.MOD);
        var left = (ExpressionNode)Visit(operands[0]);
        for (int i = 0; i < ops.Count; i++)
        {
            var op = ops[i] switch
            {
                STParser.STAR => BinaryOp.Mul,
                STParser.SLASH => BinaryOp.Div,
                _ => BinaryOp.Mod,
            };
            var right = (ExpressionNode)Visit(operands[i + 1]);
            left = ASTFactory.Binary(op, left, right);
        }
        return left;
    }

    public override ASTNode VisitPowerExpr(STParser.PowerExprContext ctx)
    {
        var postfix = ctx.postfixExpr();
        if (ctx.POWER() == null)
            return Visit(postfix);
        return ASTFactory.Binary(
            BinaryOp.Power,
            (ExpressionNode)Visit(postfix),
            (ExpressionNode)Visit(ctx.unaryExpr())
        );
    }

    public override ASTNode VisitUnaryExpr(STParser.UnaryExprContext ctx)
    {
        if (ctx.powerExpr() != null)
            return Visit(ctx.powerExpr());
        var operand = (ExpressionNode)Visit(ctx.unaryExpr());
        var op = ctx.NOT() != null ? UnaryOp.Not : UnaryOp.Neg;
        return ASTFactory.Unary(Span(ctx), op, operand);
    }

    public override ASTNode VisitPostfixExpr(STParser.PostfixExprContext ctx)
    {
        if (ctx.builtinType() != null)
        {
            var node = SetSpan(new FunctionCallExprNode(), ctx);
            node.Name = ctx.builtinType().GetText();
            if (ctx.paramList().Length > 0)
            {
                foreach (var p in ctx.paramList()[0].paramAssignment())
                    node.Arguments.Add((CallParamNode)Visit(p));
            }
            return node;
        }

        ExpressionNode primary = (ExpressionNode)Visit(ctx.primaryExpr());

        int paramIdx = 0;
        int exprIdx = 0;
        for (int ci = 1; ci < ctx.ChildCount; ci++)
        {
            var child = ctx.GetChild(ci);
            if (child is ITerminalNode term)
            {
                if (term.Symbol.Type == STParser.LPAREN)
                {
                    var call = SetSpan(new FunctionCallExprNode(), ctx);
                    call.Name = primary is VariableExprNode vn
                        ? vn.Segments.FirstOrDefault()?.Name ?? ""
                        : primary.ToString() ?? "";

                    if (paramIdx < ctx.paramList().Length)
                    {
                        foreach (var p in ctx.paramList()[paramIdx].paramAssignment())
                            call.Arguments.Add((CallParamNode)Visit(p));
                        paramIdx++;
                    }
                    primary = call;
                }
                else if (term.Symbol.Type == STParser.LBRACK)
                {
                    if (exprIdx < ctx.expressionList().Length && primary is VariableExprNode vn)
                    {
                        var lastSegment = vn.Segments.LastOrDefault();
                        if (lastSegment != null)
                        {
                            var suffix = new VariableSuffixNode
                            {
                                Subscripts = new List<ExpressionNode>(),
                            };
                            foreach (var e in ctx.expressionList()[exprIdx].expression())
                                suffix.Subscripts.Add((ExpressionNode)Visit(e));
                            lastSegment.Suffixes.Add(suffix);
                        }
                        exprIdx++;
                    }
                }
            }
        }

        return primary;
    }

    public override ASTNode VisitPrimaryExpr(STParser.PrimaryExprContext ctx)
    {
        if (ctx.literal() != null)
            return Visit(ctx.literal());
        if (ctx.typedLiteral() != null)
            return Visit(ctx.typedLiteral());
        if (ctx.variable() != null)
            return Visit(ctx.variable());
        if (ctx.expression() != null)
            return Visit(ctx.expression());
        throw new InvalidOperationException("Unknown primary expression");
    }

    public override ASTNode VisitVariable(STParser.VariableContext ctx)
    {
        var node = SetSpan(new VariableExprNode(), ctx);

        int exprListIdx = 0;
        VariableSegmentNode? currentSegment = null;

        foreach (var child in ctx.children)
        {
            if (child is ITerminalNode term)
            {
                switch (term.Symbol.Type)
                {
                    case STParser.IDENTIFIER:
                        currentSegment = new VariableSegmentNode
                        {
                            Name = term.GetText(),
                            Span = new SourceSpan(
                                term.Symbol.Line,
                                term.Symbol.Column + 1,
                                term.Symbol.Line,
                                term.Symbol.Column + term.Symbol.Text.Length,
                                _fileName
                            ),
                        };
                        node.Segments.Add(currentSegment);
                        break;

                    case STParser.CARET:
                        currentSegment?.Suffixes.Add(
                            new VariableSuffixNode { IsDereference = true }
                        );
                        break;

                    case STParser.LBRACK:
                        if (exprListIdx < ctx.expressionList().Length)
                        {
                            var suffix = new VariableSuffixNode
                            {
                                Subscripts = new List<ExpressionNode>(),
                            };
                            foreach (var e in ctx.expressionList()[exprListIdx].expression())
                                suffix.Subscripts.Add((ExpressionNode)Visit(e));
                            currentSegment?.Suffixes.Add(suffix);
                            exprListIdx++;
                        }
                        break;
                }
            }
        }
        return node;
    }

    public override ASTNode VisitLiteral(STParser.LiteralContext ctx)
    {
        var s = Span(ctx);
        if (ctx.INTEGER_LITERAL() != null)
        {
            var raw = ctx.INTEGER_LITERAL().GetText();
            if (long.TryParse(raw.Replace("_", ""), out var val))
                return ASTFactory.Int(s, raw, val);
            // 溢出时降级为 0，避免未处理异常导致整个管线崩溃
            return ASTFactory.Int(s, raw, 0);
        }
        if (ctx.BASED_INTEGER() != null)
        {
            var raw = ctx.BASED_INTEGER().GetText();
            try
            {
                return ASTFactory.BasedInt(s, raw, ParseBasedInteger(raw));
            }
            catch (OverflowException)
            {
                return ASTFactory.BasedInt(s, raw, 0);
            }
        }
        if (ctx.REAL_LITERAL() != null)
        {
            var raw = ctx.REAL_LITERAL().GetText();
            if (
                double.TryParse(
                    raw.Replace("_", ""),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var val
                )
            )
                return ASTFactory.Real(s, raw, val);
            return ASTFactory.Real(s, raw, 0.0);
        }
        if (ctx.TRUE() != null)
            return ASTFactory.Bool(s, true);
        if (ctx.FALSE() != null)
            return ASTFactory.Bool(s, false);
        if (ctx.STRING_LITERAL() != null)
        {
            var raw = ctx.STRING_LITERAL().GetText();
            return ASTFactory.Str(s, raw, raw[1..^1].Replace("''", "'").Replace("\"\"", "\""));
        }
        if (ctx.TIME_LITERAL() != null)
            return ASTFactory.Time(s, ctx.TIME_LITERAL().GetText());
        if (ctx.DATE_LITERAL() != null)
            return ASTFactory.Date(s, ctx.DATE_LITERAL().GetText());
        if (ctx.TOD_LITERAL() != null)
            return ASTFactory.Tod(s, ctx.TOD_LITERAL().GetText());
        if (ctx.DT_LITERAL() != null)
            return ASTFactory.Dt(s, ctx.DT_LITERAL().GetText());
        throw new InvalidOperationException("Unknown literal type");
    }

    public override ASTNode VisitTypedLiteral(STParser.TypedLiteralContext ctx) =>
        ASTFactory.TypedLiteral(
            Span(ctx),
            ctx.typeName().GetText(),
            ctx.typedLiteralValue().GetText()
        );
}
