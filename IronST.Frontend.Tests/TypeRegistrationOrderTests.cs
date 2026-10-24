using IronST.Common.Diagnostics;
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

/// <summary>
/// F7：两趟类型注册。
///
/// <para>
/// 此前 <c>RegisterTypeDecl</c> 在声明处立即解析字段类型，而 <c>ResolveTypeName</c>
/// 只查已注册符号 —— 于是类型解析结果取决于<b>声明顺序</b>。跨文件合并编译时
/// 跨文件的 STRUCT 字段类型必然踩到，误报 E2015。
/// </para>
/// <para>
/// 现改为：第一趟登记全部类型名，第二趟迭代解析布局到不动点。只形成值布局环的
/// 类型才卡住并报 E2016；引用边（REFERENCE TO）不阻塞。
/// </para>
/// </summary>
public class TypeRegistrationOrderTests
{
    private static AnalysisResult Analyze(string source) => TestHelpers.Analyze(source);

    private static ErrorCode[] ErrorCodes(AnalysisResult r) =>
        r.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Code)
            .ToArray();

    // ═══════════════════════════════════════════
    //  前向引用必须合法
    // ═══════════════════════════════════════════

    [Fact]
    public void ForwardTypeReference_ShouldSucceed()
    {
        // A 引用 B，但 B 声明在 A 之后 —— 按值布局，必须等 B 就绪
        const string source = """
            TYPE Container : STRUCT
                inner : Item;
            END_STRUCT;
            END_TYPE

            TYPE Item : STRUCT
                value : INT;
            END_STRUCT;
            END_TYPE

            PROGRAM Main
            VAR c : Container; r : INT; END_VAR
            r := c.inner.value;
            END_PROGRAM
            """;

        var result = Analyze(source);

        Assert.Empty(ErrorCodes(result));
        Assert.Contains(result.CustomTypes, t => t.Name == "Container");
        Assert.Contains(result.CustomTypes, t => t.Name == "Item");
    }

    [Fact]
    public void ForwardReference_LayoutIsFullyResolved()
    {
        // 不只是"不报错"：前向引用的字段必须真的解析成目标布局，
        // 否则嵌套字段访问 a.inner.value 会失败
        const string source = """
            TYPE Container : STRUCT
                inner : Item;
                tag : INT;
            END_STRUCT;
            END_TYPE

            TYPE Item : STRUCT
                value : INT;
            END_STRUCT;
            END_TYPE

            PROGRAM Main
            VAR c : Container; r : INT; END_VAR
            c.tag := 1;
            r := c.inner.value;
            END_PROGRAM
            """;

        var result = Analyze(source);

        Assert.Empty(ErrorCodes(result));

        // Container 两个字段、Item 一个字段，均须保留
        var container = result.CustomTypes.Single(t => t.Name == "Container");
        Assert.Equal(2, container.Fields!.Count);
        Assert.Contains(container.Fields, f => f.Name == "inner");
        Assert.Contains(container.Fields, f => f.Name == "tag");
    }

    [Fact]
    public void ChainedForwardReference_ShouldSucceed()
    {
        // A → B → C，声明顺序 C, B, A：每环都要等下一环就绪
        const string source = """
            TYPE A : STRUCT b : B; END_STRUCT; END_TYPE
            TYPE B : STRUCT c : C; END_STRUCT; END_TYPE
            TYPE C : STRUCT x : INT; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR a : A; r : INT; END_VAR
            r := a.b.c.x;
            END_PROGRAM
            """;

        var result = Analyze(source);

        Assert.Empty(ErrorCodes(result));
    }

    [Fact]
    public void ArrayElementForwardReference_ShouldSucceed()
    {
        // 数组元素是值布局边，同样要等元素类型就绪
        const string source = """
            TYPE Holder : STRUCT
                items : ARRAY[0..3] OF Item;
            END_STRUCT;
            END_TYPE

            TYPE Item : STRUCT
                value : INT;
            END_STRUCT;
            END_TYPE

            PROGRAM Main
            VAR h : Holder; r : INT; END_VAR
            r := h.items[0].value;
            END_PROGRAM
            """;

        var result = Analyze(source);

        Assert.Empty(ErrorCodes(result));
    }

    [Fact]
    public void ForwardReference_AcrossFiles_ShouldSucceed()
    {
        // Main.st 在前、types.st 在后：字段类型来自另一个文件且声明更晚
        var result = FrontendPipeline.AnalyzeFiles(
            [
                (
                    "PROGRAM Main\nVAR p : Point; r : DINT; END_VAR\nr := p.x;\nEND_PROGRAM",
                    "main.st"
                ),
                (
                    "TYPE Point : STRUCT\nx : INT;\ny : INT;\nEND_STRUCT;\nEND_TYPE",
                    "types.st"
                ),
            ],
            [IECStandardLibrary.Create()]
        );

        Assert.Empty(ErrorCodes(result));
        Assert.Contains(result.CustomTypes, t => t.Name == "Point");
    }

    // ═══════════════════════════════════════════
    //  引用型循环合法，值布局循环非法
    // ═══════════════════════════════════════════

    [Fact]
    public void ReferenceCycleViaRefTo_ShouldSucceed()
    {
        // REFERENCE TO 是引用边，不要求目标布局就绪 —— 契约 M5：引用型类型循环允许
        const string source = """
            TYPE NodeA : STRUCT
                other : REFERENCE TO NodeB;
            END_STRUCT;
            END_TYPE

            TYPE NodeB : STRUCT
                other : REFERENCE TO NodeA;
            END_STRUCT;
            END_TYPE

            PROGRAM Main
            VAR a : NodeA; b : NodeB; END_VAR
            a := b;
            b := a;
            END_PROGRAM
            """;

        var result = Analyze(source);

        Assert.Empty(ErrorCodes(result));
    }

    [Fact]
    public void MutuallyRecursiveValueLayout_ShouldReportE2016()
    {
        // A 内含 B、B 内含 A，且都是完整内嵌值布局 —— 无法确定内存布局
        const string source = """
            TYPE LoopA : STRUCT
                b : LoopB;
            END_STRUCT;
            END_TYPE

            TYPE LoopB : STRUCT
                a : LoopA;
            END_STRUCT;
            END_TYPE

            PROGRAM Main
            VAR x : INT; END_VAR
            x := 1;
            END_PROGRAM
            """;

        var result = Analyze(source);

        Assert.Contains(ErrorCodes(result), c => c == ErrorCode.E2016);
    }

    [Fact]
    public void SelfRecursiveStruct_ShouldReportE2016()
    {
        const string source = """
            TYPE TreeNode : STRUCT
                value : INT;
                next : TreeNode;
            END_STRUCT;
            END_TYPE

            PROGRAM Main
            VAR x : INT; END_VAR
            x := 1;
            END_PROGRAM
            """;

        var result = Analyze(source);

        Assert.Contains(ErrorCodes(result), c => c == ErrorCode.E2016);
    }

    [Fact]
    public void RecursiveValueLayout_DiagnosticNamesTheType()
    {
        const string source = """
            TYPE LoopA : STRUCT b : LoopB; END_STRUCT; END_TYPE
            TYPE LoopB : STRUCT a : LoopA; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR x : INT; END_VAR
            x := 1;
            END_PROGRAM
            """;

        var result = Analyze(source);

        var diags = result.Diagnostics.Where(d => d.Code == ErrorCode.E2016).ToList();

        // 环上两个类型都无布局可定，都应报错
        Assert.Equal(2, diags.Count);
        Assert.Contains(diags, d => d.Message.Contains("'LoopA'"));
        Assert.Contains(diags, d => d.Message.Contains("'LoopB'"));
        Assert.All(diags, d => Assert.Contains("递归布局", d.Message));
    }

    // ═══════════════════════════════════════════
    //  两趟注册不得吞掉真正未知的类型
    // ═══════════════════════════════════════════

    [Fact]
    public void UnknownFieldType_StillReportsE2015()
    {
        const string source = """
            TYPE Broken : STRUCT
                field : NoSuchType;
            END_STRUCT;
            END_TYPE

            PROGRAM Main
            VAR x : INT; END_VAR
            x := 1;
            END_PROGRAM
            """;

        var result = Analyze(source);

        Assert.Contains(ErrorCodes(result), c => c == ErrorCode.E2015);
        Assert.DoesNotContain(ErrorCodes(result), c => c == ErrorCode.E2016);
    }

    [Fact]
    public void SimpleTypeAlias_ForwardReference_ShouldSucceed()
    {
        // 别名指向后声明的类型同样受两趟注册保护
        const string source = """
            TYPE Alias : Target;
            END_TYPE
            TYPE Target : STRUCT value : INT; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR a : Alias; r : INT; END_VAR
            r := a.value;
            END_PROGRAM
            """;

        var result = Analyze(source);

        Assert.Empty(ErrorCodes(result));
        Assert.Contains(result.CustomTypes, t => t.Name == "Alias");
        Assert.Contains(result.CustomTypes, t => t.Name == "Target");
    }

    // ═══════════════════════════════════════════
    //  与 F8 的交互：重复声明仍须报 E2002
    // ═══════════════════════════════════════════

    [Fact]
    public void DuplicateTypeDecl_StillReportsE2002_AfterTwoPass()
    {
        const string source = """
            TYPE Point : STRUCT x : INT; END_STRUCT; END_TYPE
            TYPE Point : STRUCT y : INT; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR p : Point; END_VAR
            END_PROGRAM
            """;

        var result = Analyze(source);

        Assert.Contains(ErrorCodes(result), c => c == ErrorCode.E2002);
        // 重名只取首个，不得因两趟解析而重复解析成两条
        Assert.Single(result.CustomTypes.Where(t => t.Name == "Point"));
    }
}
