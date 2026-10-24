// tests/IronST.Frontend.Tests/CollectionUnitTests.cs
using IronST.Common.Symbol;
using IronST.Common.Types;
using IronST.Frontend.Analysis;
using IronST.Frontend.AST;

namespace IronST.Frontend.Tests;

/// <summary>
/// 测试 SemanticAnalyzer 的集合收集功能
/// </summary>
public class CollectionUnitTests
{
    // ★ 不再 new SemanticAnalyzer，统一走 FrontendTestHelper
    private static AnalysisResult Analyze(CompilationUnitNode cu)
    {
        // 直接调用 Pipeline，避免类型冲突
        var pipeline = new SemanticAnalyzer();
        return pipeline.Analyze(cu);
    }

    // ── 辅助工厂 ──

    private static SimpleDataTypeNode SimpleType(string name) => new() { TypeName = name };

    private static VarDeclItemNode VarItem(string name, string typeName)
    {
        var node = new VarDeclItemNode(SimpleType(typeName));
        node.Names.Add(new VarNameNode { Name = name });
        return node;
    }

    private static VarDeclBlockNode VarBlock(
        string varName, string typeName,
        VarScope scope = VarScope.Local)
    {
        var block = new VarDeclBlockNode { Scope = scope };
        block.Items.Add(VarItem(varName, typeName));
        return block;
    }

    private static LiteralExprNode IntLit(long value) =>
        new(LiteralKind.Integer, value.ToString(), value);

    private static VariableExprNode Var(string name)
    {
        var node = new VariableExprNode();
        node.Segments.Add(new VariableSegmentNode { Name = name });
        return node;
    }

    private static CompilationUnitNode MakeProgram(
        string progName,
        VarDeclBlockNode? varBlock = null,
        IEnumerable<StatementNode>? body = null)
    {
        var cu = new CompilationUnitNode();
        var prog = new ProgramNode { Name = progName };
        if (varBlock != null) prog.VarBlocks.Add(varBlock);
        if (body != null) prog.Body.AddRange(body);
        cu.POUs.Add(prog);
        return cu;
    }

    private static CompilationUnitNode MakeFunction(
        string funcName, string returnTypeName,
        IEnumerable<VarDeclBlockNode>? varBlocks = null,
        IEnumerable<StatementNode>? body = null)
    {
        var cu = new CompilationUnitNode();
        var fn = new FunctionNode { Name = funcName, ReturnTypeName = returnTypeName };
        if (varBlocks != null) fn.VarBlocks.AddRange(varBlocks);
        if (body != null) fn.Body.AddRange(body);
        cu.POUs.Add(fn);
        return cu;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Variables
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Variables_SimpleProgram_CollectsOne()
    {
        var block = new VarDeclBlockNode { Scope = VarScope.Local };
        block.Items.Add(VarItem("x", "INT"));

        var cu = MakeProgram("P", varBlock: block,
            body: [new AssignmentNode(Var("x"), IntLit(1))]);

        var result = Analyze(cu);

        Assert.Single(result.Variables);
        Assert.Equal("x", result.Variables[0].Name);
        Assert.Equal("INT", result.Variables[0].TypeName);
        Assert.Equal("P", result.Variables[0].POUName);
        Assert.Equal("VAR", result.Variables[0].VarBlockKind);
    }

    [Fact]
    public void Variables_InputBlock_CorrectKind()
    {
        var inputBlock = new VarDeclBlockNode { Scope = VarScope.Input };
        inputBlock.Items.Add(VarItem("a", "DINT"));

        var cu = MakeFunction("F", "DINT",
            varBlocks: [inputBlock],
            body: [new AssignmentNode(Var("F"), Var("a"))]);

        var result = Analyze(cu);

        Assert.Single(result.Variables);
        Assert.Equal("VAR_INPUT", result.Variables[0].VarBlockKind);
        Assert.Equal("F", result.Variables[0].POUName);
    }

    [Fact]
    public void Variables_MultipleInSameItem_AllCollected()
    {
        var block = new VarDeclBlockNode { Scope = VarScope.Local };
        var item = new VarDeclItemNode(SimpleType("INT"));
        item.Names.Add(new VarNameNode { Name = "a" });
        item.Names.Add(new VarNameNode { Name = "b" });
        item.Names.Add(new VarNameNode { Name = "c" });
        block.Items.Add(item);

        var cu = MakeProgram("P", varBlock: block,
            body: [new AssignmentNode(Var("a"), IntLit(1))]);

        var result = Analyze(cu);

        Assert.Equal(3, result.Variables.Count);
        Assert.Contains(result.Variables, v => v.Name == "a");
        Assert.Contains(result.Variables, v => v.Name == "b");
        Assert.Contains(result.Variables, v => v.Name == "c");
    }

    // ═══════════════════════════════════════════════════════════════
    //  POUs
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void POUs_Program_CollectsAsProgram()
    {
        var cu = MakeProgram("Main");
        var result = Analyze(cu);

        Assert.Single(result.POUs);
        Assert.Equal("Main", result.POUs[0].Name);
        Assert.Equal(POUKind.Program, result.POUs[0].Kind);
        Assert.Empty(result.POUs[0].Parameters);
    }

    [Fact]
    public void POUs_Function_CollectsReturnTypeAndParams()
    {
        var inputBlock = new VarDeclBlockNode { Scope = VarScope.Input };
        inputBlock.Items.Add(VarItem("a", "INT"));
        inputBlock.Items.Add(VarItem("b", "INT"));

        var cu = MakeFunction("Add", "INT",
            varBlocks: [inputBlock],
            body: [new AssignmentNode(Var("Add"),
                new BinaryExprNode(BinaryOp.Add, Var("a"), Var("b")))]);

        var result = Analyze(cu);

        Assert.Single(result.POUs);
        var pou = result.POUs[0];
        Assert.Equal(POUKind.Function, pou.Kind);
        Assert.Equal("INT", pou.ReturnTypeName);
        Assert.Equal(2, pou.Parameters.Count);
    }

    [Fact]
    public void POUs_FunctionBlock_CollectsMembersAsParameters()
    {
        var cu = new CompilationUnitNode();
        var fb = new FunctionBlockNode { Name = "FB1" };

        var input = new VarDeclBlockNode { Scope = VarScope.Input };
        input.Items.Add(VarItem("in1", "INT"));
        fb.VarBlocks.Add(input);

        var local = new VarDeclBlockNode { Scope = VarScope.Local };
        local.Items.Add(VarItem("local1", "BOOL"));
        fb.VarBlocks.Add(local);

        cu.POUs.Add(fb);

        var result = Analyze(cu);

        Assert.Single(result.POUs);
        var pou = result.POUs[0];
        Assert.Equal(POUKind.FunctionBlock, pou.Kind);
        Assert.Equal(2, pou.Parameters.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    //  CustomTypes
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CustomTypes_Struct_CollectsFields()
    {
        var structDef = new StructDefNode();
        structDef.Members.Add(VarItem("X", "INT"));
        structDef.Members.Add(VarItem("Y", "REAL"));

        var cu = new CompilationUnitNode();
        cu.TypeDeclarations.Add(new TypeDeclNode("Point", structDef));

        var result = Analyze(cu);

        Assert.Single(result.CustomTypes);
        var t = result.CustomTypes[0];
        Assert.Equal("Point", t.Name);
        Assert.Equal(TypeKind.Struct, t.Kind);
        Assert.Equal(2, t.Fields!.Count);
    }

    [Fact]
    public void CustomTypes_Enum_CollectsMembers()
    {
        var enumDef = new EnumDefNode();
        enumDef.Items.Add(new EnumItemNode { Name = "Red" });
        enumDef.Items.Add(new EnumItemNode { Name = "Green" });

        var cu = new CompilationUnitNode();
        cu.TypeDeclarations.Add(new TypeDeclNode("Color", enumDef));

        var result = Analyze(cu);

        Assert.Single(result.CustomTypes);
        Assert.Equal("Color", result.CustomTypes[0].Name);
        Assert.Equal(TypeKind.Enum, result.CustomTypes[0].Kind);
        Assert.Equal(new[] { "Red", "Green" }, result.CustomTypes[0].EnumMembers);
    }

    [Fact]
    public void CustomTypes_SimpleAlias_CollectsUnderlyingType()
    {
        var cu = new CompilationUnitNode();
        cu.TypeDeclarations.Add(
            new TypeDeclNode("MyDINT", new SimpleTypeDefNode(SimpleType("DINT"))));

        var result = Analyze(cu);

        Assert.Single(result.CustomTypes);
        Assert.Equal("MyDINT", result.CustomTypes[0].Name);
        Assert.Equal(TypeKind.SimpleType, result.CustomTypes[0].Kind);
        Assert.Equal("DINT", result.CustomTypes[0].UnderlyingType);
    }

    // ═══════════════════════════════════════════════════════════════
    //  References
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void References_VariableDecl_HasDeclaration()
    {
        var block = new VarDeclBlockNode { Scope = VarScope.Local };
        block.Items.Add(VarItem("x", "INT"));

        var cu = MakeProgram("P", varBlock: block,
            body: [new AssignmentNode(Var("x"), IntLit(1))]);

        var result = Analyze(cu);

        var decl = result.References.FirstOrDefault(
            r => r.SymbolName == "x" && r.Usage == ReferenceUsage.Declaration);
        Assert.NotNull(decl);
        Assert.Equal(SymbolKind.Variable, decl!.Kind);
    }

    [Fact]
    public void References_AssignmentTarget_HasWriteUsage()
    {
        var block = new VarDeclBlockNode { Scope = VarScope.Local };
        block.Items.Add(VarItem("x", "INT"));

        var cu = MakeProgram("P", varBlock: block,
            body: [new AssignmentNode(Var("x"), IntLit(1))]);

        var result = Analyze(cu);

        Assert.Contains(result.References, r =>
            r.SymbolName == "x" && r.Usage == ReferenceUsage.Write);
    }

    [Fact]
    public void References_AssignmentValue_HasReadUsage()
    {
        var block = new VarDeclBlockNode { Scope = VarScope.Local };
        block.Items.Add(VarItem("x", "INT"));
        block.Items.Add(VarItem("y", "INT"));

        var cu = MakeProgram("P", varBlock: block,
            body: [new AssignmentNode(Var("y"), Var("x"))]);

        var result = Analyze(cu);

        var xRead = result.References.FirstOrDefault(
            r => r.SymbolName == "x" && r.Usage == ReferenceUsage.Read);
        Assert.NotNull(xRead);
    }

    [Fact]
    public void References_FunctionDeclaration_HasDeclaration()
    {
        var inputBlock = new VarDeclBlockNode { Scope = VarScope.Input };
        inputBlock.Items.Add(VarItem("x", "INT"));

        var cu = MakeFunction("Calc", "INT",
            varBlocks: [inputBlock],
            body: [new AssignmentNode(Var("Calc"), Var("x"))]);

        var result = Analyze(cu);

        var decl = result.References.FirstOrDefault(
            r => r.SymbolName == "Calc" && r.Usage == ReferenceUsage.Declaration);
        Assert.NotNull(decl);
        Assert.Equal(SymbolKind.Function, decl!.Kind);
    }

    // ═══════════════════════════════════════════════════════════════
    //  CallSites
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void CallSites_CallStatement_RecordsCalleeAndCaller()
    {
        var fn = new FunctionNode { Name = "Add", ReturnTypeName = "INT" };
        var fnInput = new VarDeclBlockNode { Scope = VarScope.Input };
        fnInput.Items.Add(VarItem("a", "INT"));
        fn.VarBlocks.Add(fnInput);
        fn.Body.Add(new AssignmentNode(Var("Add"), Var("a")));

        var callStmt = new CallStatementNode { Name = "Add" };
        callStmt.Arguments.Add(new CallParamNode(IntLit(1)));

        var prog = new ProgramNode { Name = "Main" };
        prog.Body.Add(callStmt);

        var cu = new CompilationUnitNode();
        cu.POUs.Add(fn);
        cu.POUs.Add(prog);

        var result = Analyze(cu);

        Assert.Single(result.CallSites);
        Assert.Equal("Main", result.CallSites[0].CallerName);
        Assert.Equal("Add", result.CallSites[0].CalleeName);
    }

    [Fact]
    public void CallSites_FunctionCallExpr_RecordsCorrectly()
    {
        var fn = new FunctionNode { Name = "Double", ReturnTypeName = "INT" };
        var fnInput = new VarDeclBlockNode { Scope = VarScope.Input };
        fnInput.Items.Add(VarItem("x", "INT"));
        fn.VarBlocks.Add(fnInput);
        fn.Body.Add(new AssignmentNode(Var("Double"), Var("x")));

        var progBlock = new VarDeclBlockNode { Scope = VarScope.Local };
        progBlock.Items.Add(VarItem("r", "INT"));

        var callExpr = new FunctionCallExprNode { Name = "Double" };
        callExpr.Arguments.Add(new CallParamNode(IntLit(5)));

        var prog = new ProgramNode { Name = "Main" };
        prog.VarBlocks.Add(progBlock);
        prog.Body.Add(new AssignmentNode(Var("r"), callExpr));

        var cu = new CompilationUnitNode();
        cu.POUs.Add(fn);
        cu.POUs.Add(prog);

        var result = Analyze(cu);

        Assert.Contains(result.CallSites, c =>
            c.CalleeName == "Double" && c.CallerName == "Main");
    }

    [Fact]
    public void CallSites_NoCalls_ReturnsEmpty()
    {
        var block = new VarDeclBlockNode { Scope = VarScope.Local };
        block.Items.Add(VarItem("x", "INT"));

        var cu = MakeProgram("P", varBlock: block,
            body: [new AssignmentNode(Var("x"), IntLit(1)),
                   new AssignmentNode(Var("x"), IntLit(2))]);

        var result = Analyze(cu);

        Assert.Empty(result.CallSites);
    }

    // ═══════════════════════════════════════════════════════════════
    //  空 CU
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyCompilationUnit_AllCollectionsEmpty()
    {
        var result = Analyze(new CompilationUnitNode());

        Assert.Empty(result.Variables);
        Assert.Empty(result.POUs);
        Assert.Empty(result.CustomTypes);
        Assert.Empty(result.References);
        Assert.Empty(result.CallSites);
    }
}
