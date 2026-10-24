using IronST.Common.Diagnostics;
using IronST.Frontend;
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

/// <summary>
/// F8：重复声明必须可见。
///
/// <para>
/// 此前 <c>SymbolTable.TryDeclare</c> 的返回值在全部 10 个调用点被丢弃，重复的
/// 函数/类型/全局变量/局部变量声明会被静默吞掉 —— 第一个声明生效、后续同名声明
/// 全部消失且零诊断。这使增量编译契约 A2「重复定义导出符号」在整体编译路径下
/// 完全不可观测。
/// </para>
/// </summary>
public class DuplicateDeclarationTests
{
    // ═══════════════════════════════════════════
    //  用户声明之间的重复 → E2002
    // ═══════════════════════════════════════════

    [Fact]
    public void DuplicateFunction_EmitsE2002()
    {
        const string source = """
            FUNCTION Calc : INT
            VAR_INPUT a : INT; END_VAR
            Calc := a;
            END_FUNCTION

            FUNCTION Calc : INT
            VAR_INPUT a : INT; END_VAR
            Calc := a + 1;
            END_FUNCTION

            PROGRAM Main
            VAR r : INT; END_VAR
            r := Calc(1);
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        Assert.Contains(TestHelpers.GetErrors(result, ErrorCode.E2002),
            d => d.Message.Contains("'Calc'"));
    }

    [Fact]
    public void DuplicateType_EmitsE2002()
    {
        const string source = """
            TYPE Point : STRUCT
            x : INT;
            y : INT;
            END_STRUCT;
            END_TYPE

            TYPE Point : STRUCT
            x : INT;
            y : INT;
            END_STRUCT;
            END_TYPE

            PROGRAM Main
            VAR p : Point; END_VAR
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        Assert.Contains(TestHelpers.GetErrors(result, ErrorCode.E2002),
            d => d.Message.Contains("'Point'"));
    }

    [Fact]
    public void DuplicateFunctionBlock_EmitsE2002()
    {
        const string source = """
            FUNCTION_BLOCK Motor
            VAR speed : INT; END_VAR
            END_FUNCTION_BLOCK

            FUNCTION_BLOCK Motor
            VAR speed : INT; END_VAR
            END_FUNCTION_BLOCK

            PROGRAM Main
            VAR m : Motor; END_VAR
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        Assert.Contains(TestHelpers.GetErrors(result, ErrorCode.E2002),
            d => d.Message.Contains("'Motor'"));
    }

    [Fact]
    public void DuplicateProgram_EmitsE2002()
    {
        const string source = """
            PROGRAM Line1
            VAR a : INT; END_VAR
            END_PROGRAM

            PROGRAM Line1
            VAR a : INT; END_VAR
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        Assert.Contains(TestHelpers.GetErrors(result, ErrorCode.E2002),
            d => d.Message.Contains("'Line1'"));
    }

    [Fact]
    public void DuplicateLocalVariable_EmitsE2002()
    {
        const string source = """
            PROGRAM Main
            VAR
                counter : INT;
                counter : INT;
            END_VAR
            counter := 1;
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        Assert.Contains(TestHelpers.GetErrors(result, ErrorCode.E2002),
            d => d.Message.Contains("'counter'"));
    }

    [Fact]
    public void DuplicateDifferentCase_EmitsE2002()
    {
        // 符号表为 OrdinalIgnoreCase：MySpeed 与 myspeed 是同一符号
        const string source = """
            PROGRAM Main
            VAR
                MySpeed : INT;
                myspeed : INT;
            END_VAR
            MySpeed := 1;
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        Assert.Contains(TestHelpers.GetErrors(result, ErrorCode.E2002),
            d => d.Message.Contains("'myspeed'"));
    }

    // ═══════════════════════════════════════════
    //  诊断须引用先前声明位置
    // ═══════════════════════════════════════════

    [Fact]
    public void DuplicateDeclaration_ReferencesPreviousLocation()
    {
        const string source = """
            PROGRAM Main
            VAR
                shared : INT;
                shared : INT;
            END_VAR
            shared := 1;
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        var dup = TestHelpers.GetErrors(result, ErrorCode.E2002)
            .Single(d => d.Message.Contains("'shared'"));

        Assert.NotNull(dup.Span);

        // 诊断必须引用先前声明的位置，且该位置在当前声明之前
        // 位置格式为 `file:line:col` 或 `line:col`，行号取倒数第二个分段
        var prevText = dup.Message.Split("先前声明于")[1];
        var parts = prevText.TrimStart('（', ' ').Split(':');
        var prevLine = int.Parse(parts[^2]);
        Assert.True(
            prevLine < dup.Span!.Value.Line,
            $"先前声明行 {prevLine} 应小于当前行 {dup.Span!.Value.Line}，实际消息：{dup.Message}"
        );
    }

    // ═══════════════════════════════════════════
    //  重复不再被静默吞掉：保留首个，其余已报错
    // ═══════════════════════════════════════════

    [Fact]
    public void DuplicateFunction_KeepsFirstAndSkipsCollection()
    {
        const string source = """
            FUNCTION Calc : INT
            VAR_INPUT a : INT; END_VAR
            Calc := a;
            END_FUNCTION

            FUNCTION Calc : INT
            VAR_INPUT a : INT; END_VAR
            Calc := a + 1;
            END_FUNCTION

            PROGRAM Main
            VAR r : INT; END_VAR
            r := Calc(1);
            END_PROGRAM
            """;

        var result = TestHelpers.Analyze(source);

        // 重复的 POU 不得进入收集结果，否则下游会重复发射
        Assert.Single(result.POUs.Where(p => p.Name == "Calc"));
        Assert.Single(result.POUs.Where(p => p.Name == "Main"));
    }

    // ═══════════════════════════════════════════
    //  库符号可被用户声明覆盖（不报错）
    // ═══════════════════════════════════════════

    [Fact]
    public void UserFunction_OverridingStandardLibrary_DoesNotError()
    {
        // 注入的标准库已声明 Abs。此前用户的 Abs 被静默丢弃 —— 函数体仍在编译，
        // 调用却解析到库声明上。现在用户声明覆盖库符号。
        const string source = """
            FUNCTION Abs : DINT
            VAR_INPUT x : DINT; END_VAR
            Abs := x;
            END_FUNCTION

            PROGRAM Main
            VAR val : DINT; r : DINT; END_VAR
            r := Abs(-val);
            END_PROGRAM
            """;

        var result = FrontendPipeline.Analyze(
            source,
            "test.st",
            [IECStandardLibrary.Create()]
        );

        Assert.Empty(TestHelpers.GetErrors(result, ErrorCode.E2002));
        Assert.True(result.Success,
            string.Join("\n", TestHelpers.GetErrors(result).Select(d => d.Message)));

        // 用户定义保留为唯一 Calc/Abs POU
        Assert.Single(result.POUs.Where(p => p.Name == "Abs"));
    }

    [Fact]
    public void LibrarySymbol_DoesNotGetOverridden_TwiceByUser()
    {
        // 用户覆盖库符号之后，再有第二个用户同名声明仍属重复定义
        const string source = """
            FUNCTION Abs : DINT
            VAR_INPUT x : DINT; END_VAR
            Abs := x;
            END_FUNCTION

            FUNCTION Abs : DINT
            VAR_INPUT x : DINT; END_VAR
            Abs := x + 1;
            END_FUNCTION

            PROGRAM Main
            VAR val : DINT; r : DINT; END_VAR
            r := Abs(-val);
            END_PROGRAM
            """;

        var result = FrontendPipeline.Analyze(
            source,
            "test.st",
            [IECStandardLibrary.Create()]
        );

        Assert.Contains(TestHelpers.GetErrors(result, ErrorCode.E2002),
            d => d.Message.Contains("'Abs'"));
    }

    // ═══════════════════════════════════════════
    //  外部接口重复注入 → 幂等，不报错
    // ═══════════════════════════════════════════

    [Fact]
    public void SameLibraryInjectedTwice_IsIdempotent()
    {
        const string source = """
            PROGRAM Main
            VAR r : INT; END_VAR
            r := 1;
            END_PROGRAM
            """;

        var lib = IECStandardLibrary.Create();
        var result = FrontendPipeline.Analyze(source, "test.st", [lib, lib]);

        Assert.Empty(TestHelpers.GetErrors(result, ErrorCode.E2002));
        Assert.True(result.Success,
            string.Join("\n", TestHelpers.GetErrors(result).Select(d => d.Message)));

        // 重复注入不得产生重复的 POU / 类型条目
        var dupPOUs = result.POUs.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(dupPOUs);

        var dupTypes = result.CustomTypes.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.Empty(dupTypes);
    }

    // ═══════════════════════════════════════════
    //  VAR_CONFIG 是配置绑定，不是重复声明
    // ═══════════════════════════════════════════

    [Fact]
    public void VarConfig_SameNameAsDeclaredVar_DoesNotError()
    {
        const string source = """
            PROGRAM Main
                VAR
                    startBtn : BOOL;
                END_VAR
                VAR_CONFIG
                    startBtn AT %IX0.0 : BOOL;
                END_VAR
            END_PROGRAM
            """;

        var result = TestHelpers.AnalyzeExpectSuccess(source);

        Assert.Empty(TestHelpers.GetErrors(result, ErrorCode.E2002));

        // 配置项仍需被收集
        Assert.Contains(result.Variables,
            v => v.Name == "startBtn"
                && (v.VarBlockKind == "VAR_CONFIG" || v.VarBlockKind == "CONFIG"));
    }
}
