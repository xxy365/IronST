using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using IronST.Frontend.Generated;

namespace IronST.Frontend.Formatting;

/// <summary>
/// Structural information used by <see cref="StFormatter"/>.
/// </summary>
/// <remarks>
/// The plan maps the first token of every declaration, statement, block header,
/// block terminator, branch label, and case branch to its indentation level. It is
/// built from the ANTLR parse tree so the token formatter does not have to guess
/// block structure from keywords alone (for example, PROGRAM inside a CONFIGURATION
/// is a program binding, not a POU header).
/// </remarks>
internal sealed class FormattingPlan
{
    public Dictionary<int, int> LineIndent { get; } = new();

    /// <summary>Tokens that must not be preceded by a space (e.g. a CASE label colon).</summary>
    public HashSet<int> NoSpaceBefore { get; } = new();

    public static FormattingPlan Build(STParser.CompilationUnitContext root)
    {
        var plan = new FormattingPlan();
        plan.MarkCompilationUnit(root);
        return plan;
    }

    private void MarkCompilationUnit(STParser.CompilationUnitContext ctx)
    {
        foreach (var child in ctx.children)
        {
            switch (child)
            {
                case STParser.TypeDeclContext type:
                    MarkTypeDecl(type, 0);
                    break;
                case STParser.ProgramDeclContext program:
                    MarkProgram(program, 0);
                    break;
                case STParser.FunctionDeclContext function:
                    MarkFunction(function, 0);
                    break;
                case STParser.FunctionBlockDeclContext fb:
                    MarkFunctionBlock(fb, 0);
                    break;
                case STParser.ConfigurationDeclContext configuration:
                    MarkConfiguration(configuration, 0);
                    break;
            }
        }
    }

    private void MarkProgram(STParser.ProgramDeclContext ctx, int indent)
    {
        Mark(ctx.PROGRAM(), indent);

        foreach (var varBlock in ctx.varDeclBlock())
            MarkVarBlock(varBlock, indent);

        MarkStatementList(ctx.statementList(), indent + 1);
        Mark(ctx.END_PROGRAM(), indent);
    }

    private void MarkFunction(STParser.FunctionDeclContext ctx, int indent)
    {
        Mark(ctx.FUNCTION(), indent);

        foreach (var varBlock in ctx.varDeclBlock())
            MarkVarBlock(varBlock, indent);

        MarkStatementList(ctx.statementList(), indent + 1);
        Mark(ctx.END_FUNCTION(), indent);
    }

    private void MarkFunctionBlock(STParser.FunctionBlockDeclContext ctx, int indent)
    {
        Mark(ctx.FUNCTION_BLOCK(), indent);

        foreach (var varBlock in ctx.varDeclBlock())
            MarkVarBlock(varBlock, indent);

        foreach (var statement in ctx.statement())
        {
            Mark(statement.Start, indent + 1);
            MarkStatement(statement, indent + 1);
        }

        // Stray semicolons are legal in a function-block body.
        foreach (var semi in ctx.SEMI())
            Mark(semi.Symbol, indent + 1);

        foreach (var action in ctx.actionDecl())
            MarkAction(action, indent + 1);

        Mark(ctx.END_FUNCTION_BLOCK(), indent);
    }

    private void MarkAction(STParser.ActionDeclContext ctx, int indent)
    {
        Mark(ctx.ACTION(), indent);
        MarkStatementList(ctx.statementList(), indent + 1);
        Mark(ctx.END_ACTION(), indent);
    }

    private void MarkConfiguration(STParser.ConfigurationDeclContext ctx, int indent)
    {
        Mark(ctx.CONFIGURATION(), indent);

        foreach (var varBlock in ctx.varDeclBlock())
            MarkVarBlock(varBlock, indent);

        foreach (var resource in ctx.resourceDecl())
            MarkResource(resource, indent + 1);

        Mark(ctx.END_CONFIGURATION(), indent);
    }

    private void MarkResource(STParser.ResourceDeclContext ctx, int indent)
    {
        Mark(ctx.RESOURCE(), indent);

        foreach (var varBlock in ctx.varDeclBlock())
            MarkVarBlock(varBlock, indent);

        foreach (var task in ctx.taskDecl())
            Mark(task.Start, indent + 1);

        foreach (var program in ctx.programConfig())
            Mark(program.Start, indent + 1);

        Mark(ctx.END_RESOURCE(), indent);
    }

    private void MarkVarBlock(STParser.VarDeclBlockContext ctx, int indent)
    {
        // VAR, VAR_INPUT, VAR_CONFIG, ... and END_VAR stay at the enclosing
        // declaration's level; declarations are one level deeper.
        Mark(ctx.Start, indent);

        foreach (var item in ctx.varDeclItem())
            Mark(item.Start, indent + 1);

        foreach (var item in ctx.varConfigItem())
            Mark(item.Start, indent + 1);

        Mark(ctx.END_VAR(), indent);
    }

    private void MarkTypeDecl(STParser.TypeDeclContext ctx, int indent)
    {
        Mark(ctx.TYPE(), indent);

        var identifiers = ctx.IDENTIFIER();
        var definitions = ctx.typeDef();

        for (var i = 0; i < identifiers.Length && i < definitions.Length; i++)
        {
            Mark(identifiers[i].Symbol, indent + 1);

            if (definitions[i].structDef() is { } structDef)
                MarkStructDef(structDef, indent + 1);
        }

        Mark(ctx.END_TYPE(), indent);
    }

    private void MarkStructDef(STParser.StructDefContext ctx, int indent)
    {
        // STRUCT follows "Name :" on the same line. Members and END_STRUCT are
        // indented relative to that declaration line.
        foreach (var member in ctx.varDeclItem())
            Mark(member.Start, indent + 1);

        Mark(ctx.END_STRUCT(), indent);
    }

    private void MarkStatementList(STParser.StatementListContext? ctx, int indent)
    {
        if (ctx?.children == null)
            return;

        foreach (var child in ctx.children)
        {
            switch (child)
            {
                case STParser.StatementContext statement:
                    Mark(statement.Start, indent);
                    MarkStatement(statement, indent);
                    break;
                case ITerminalNode terminal when terminal.Symbol.Type == STParser.SEMI:
                    Mark(terminal.Symbol, indent);
                    break;
            }
        }
    }

    private void MarkStatement(STParser.StatementContext ctx, int indent)
    {
        if (ctx.ifStmt() is { } ifStmt)
        {
            MarkIf(ifStmt, indent);
            return;
        }

        if (ctx.forStmt() is { } forStmt)
        {
            Mark(forStmt.END_FOR(), indent);
            MarkStatementList(forStmt.statementList(), indent + 1);
            return;
        }

        if (ctx.whileStmt() is { } whileStmt)
        {
            Mark(whileStmt.END_WHILE(), indent);
            MarkStatementList(whileStmt.statementList(), indent + 1);
            return;
        }

        if (ctx.repeatStmt() is { } repeatStmt)
        {
            Mark(repeatStmt.UNTIL(), indent);
            Mark(repeatStmt.END_REPEAT(), indent);
            MarkStatementList(repeatStmt.statementList(), indent + 1);
            return;
        }

        if (ctx.caseStmt() is { } caseStmt)
            MarkCase(caseStmt, indent);
    }

    private void MarkIf(STParser.IfStmtContext ctx, int indent)
    {
        foreach (var elsif in ctx.ELSIF())
            Mark(elsif.Symbol, indent);

        if (ctx.ELSE() is { } elseToken)
            Mark(elseToken.Symbol, indent);

        Mark(ctx.END_IF(), indent);

        // statementList(0) is the THEN body. Every following list belongs to
        // either an ELSIF branch or the ELSE branch, and is one level deeper.
        var lists = ctx.statementList();
        foreach (var list in lists)
            MarkStatementList(list, indent + 1);
    }

    private void MarkCase(STParser.CaseStmtContext ctx, int indent)
    {
        foreach (var element in ctx.caseElement())
        {
            Mark(element.Start, indent + 1);

            // "1, 2:" - a case label colon binds to the labels.
            if (element.COLON() is { } colon)
                NoSpaceBefore.Add(colon.Symbol.TokenIndex);

            MarkStatementList(element.statementList(), indent + 2);
        }

        if (ctx.ELSE() is { } elseToken)
        {
            Mark(elseToken.Symbol, indent + 1);
            MarkStatementList(ctx.statementList(), indent + 2);
        }

        Mark(ctx.END_CASE(), indent);
    }

    private void Mark(ITerminalNode? node, int indent)
    {
        if (node != null)
            Mark(node.Symbol, indent);
    }

    private void Mark(IToken? token, int indent)
    {
        if (token == null)
            return;

        // A statement's Start token can also be its IF/FOR/CASE token. Keep the
        // first (outer) decision rather than letting a nested marker overwrite it.
        LineIndent.TryAdd(token.TokenIndex, indent);
    }
}
