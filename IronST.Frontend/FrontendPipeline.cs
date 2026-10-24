using Antlr4.Runtime;
using IronST.Common.Diagnostics;
using IronST.Frontend.Analysis;
using IronST.Frontend.AST;
using IronST.Frontend.Generated;
using IronST.Frontend.Parsing;

namespace IronST.Frontend;

public static class FrontendPipeline
{
    /// <summary>分析单个 ST 源文件。</summary>
    public static AnalysisResult Analyze(
        string source,
        string? fileName = null,
        IReadOnlyList<ExternalLibrary>? libraries = null
    )
    {
        var (ast, parseDiags) = ParseFile(source, fileName ?? "source.st");
        if (ast == null)
            return EmptyResult(new CompilationUnitNode(), parseDiags);

        AnalysisResult result;
        try
        {
            result = new SemanticAnalyzer().Analyze(ast, libraries);
        }
        catch (Exception ex)
        {
            var diags = new List<Diagnostic>(parseDiags);
            diags.Add(
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = $"语义分析失败: {ex.Message}",
                    Span = SourceSpan.Unknown,
                    Code = ErrorCode.E1000,
                }
            );
            return EmptyResult(ast, diags);
        }

        var allDiagnostics = new List<Diagnostic>(parseDiags);
        allDiagnostics.AddRange(result.Diagnostics);
        result.Diagnostics = allDiagnostics;
        return result;
    }

    /// <summary>
    /// 分析多个 ST 源文件（多文件编译，B4）。
    /// 每个文件独立做词法/语法/AST 构建，然后合并类型声明与 POU，
    /// 最后在合并后的 AST 上跑三遍语义分析（符号表跨文件共享，
    /// 因此文件 A 中定义的函数/类型可被文件 B 引用）。
    /// </summary>
    public static AnalysisResult AnalyzeFiles(
        IReadOnlyList<(string Source, string FileName)> files,
        IReadOnlyList<ExternalLibrary>? libraries = null
    )
    {
        var allDiagnostics = new List<Diagnostic>();
        var mergedAst = new CompilationUnitNode();
        var anyParsed = false;

        foreach (var (source, fileName) in files)
        {
            var (ast, parseDiags) = ParseFile(source, fileName);
            allDiagnostics.AddRange(parseDiags);
            if (ast == null)
                continue;

            anyParsed = true;
            mergedAst.TypeDeclarations.AddRange(ast.TypeDeclarations);
            mergedAst.POUs.AddRange(ast.POUs);
        }

        if (!anyParsed)
        {
            if (files.Count == 0)
                allDiagnostics.Add(
                    new Diagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Message = "没有输入文件",
                        Span = SourceSpan.Unknown,
                        Code = ErrorCode.E1000,
                    }
                );
            return EmptyResult(mergedAst, allDiagnostics);
        }

        AnalysisResult result;
        try
        {
            result = new SemanticAnalyzer().Analyze(mergedAst, libraries);
        }
        catch (Exception ex)
        {
            allDiagnostics.Add(
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = $"语义分析失败: {ex.Message}",
                    Span = SourceSpan.Unknown,
                    Code = ErrorCode.E1000,
                }
            );
            return EmptyResult(mergedAst, allDiagnostics);
        }

        allDiagnostics.AddRange(result.Diagnostics);
        result.Diagnostics = allDiagnostics;
        return result;
    }

    /// <summary>
    /// 词法 + 语法分析单个文件。
    /// 返回 AST（失败时返回 null）与词法/语法诊断。
    /// </summary>
    private static (CompilationUnitNode? AST, List<Diagnostic> Diagnostics) ParseFile(
        string source,
        string fileName
    )
    {
        var diagnostics = new List<Diagnostic>();

        try
        {
            // 1. 词法 + 语法分析
            var inputStream = CharStreams.fromString(source);
            var lexer = new STLexer(inputStream);

            var lexerDiagListener = new LexerDiagnosticListener(fileName);
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(lexerDiagListener);

            var tokenStream = new CommonTokenStream(lexer);
            var parser = new STParser(tokenStream);
            parser.RemoveErrorListeners();

            var diagListener = new SyntaxDiagnosticListener(fileName);
            diagListener.SetVocabulary(parser.Vocabulary);
            parser.AddErrorListener(diagListener);
            var parseTree = parser.compilationUnit();

            diagnostics.AddRange(lexerDiagListener.Diagnostics);
            diagnostics.AddRange(diagListener.Diagnostics);

            // 2. ParseTree -> AST
            var visitor = new ParseTreeToASTVisitor(fileName);
            var ast = (CompilationUnitNode)visitor.Visit(parseTree);
            return (ast, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(
                new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = $"AST 构建失败: {ex.Message}",
                    Span = SourceSpan.Unknown,
                    Code = ErrorCode.E1000,
                }
            );
            return (null, diagnostics);
        }
    }

    private static AnalysisResult EmptyResult(CompilationUnitNode ast, List<Diagnostic> diags) =>
        new()
        {
            AST = ast,
            Symbols = new SymbolTable(),
            Diagnostics = diags,
            Variables = [],
            POUs = [],
            CustomTypes = [],
            References = [],
            CallSites = [],
        };
}
