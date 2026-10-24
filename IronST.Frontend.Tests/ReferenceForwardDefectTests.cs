// ReferenceForwardDefectTests.cs — ③ 缺陷登记：REFERENCE TO 前向引用烘焙占位符
using IronST.Common.Symbol;
using IronST.Common.Types;

namespace IronST.Frontend.Tests;

/// <summary>
/// <b>③ 缺陷记录（已修）</b>：<c>REFERENCE TO</c> 的前向引用会在目标类型仍处于
/// 占位符状态时被提前解析。
///
/// <para>
/// <b>成因</b>：F7 的 <c>PredeclareTypeDecl</c> 把待解析类型登记为 <c>Type = IECType.Void</c>；
/// 而 <c>IsLayoutReady</c> 对 <c>ReferenceDataTypeNode</c> 恒返回 true（引用边不阻塞，
/// 这是契约 M5 要求的正确行为）。两者叠加的结果是 —— 目标类型还没回填，含
/// <c>REFERENCE TO 目标</c> 的布局就已经就绪并被解析，占位符 <c>IECType.Void</c> 于是被
/// 永久烘焙进字段类型，得到 <c>REF_TO VOID</c>。
/// </para>
///
/// <para>
/// <b>修复</b>：不动点收敛后做一次<b>回填</b>（<c>SemanticAnalyzer.BackfillReferenceTargets</c>）。
/// 结构体走「就地改字段字典」，所以符号表与代码生成侧注册的类型表自动同步；
/// 互递归（<c>A.p : REFERENCE TO B</c> / <c>B.q : REFERENCE TO A</c>）因回填不递归进
/// 目标结构体内部，也不存在构造顺序依赖。
/// </para>
///
/// <para>
/// <b>F7 之前同样是错的</b>，只是错法不同：单趟解析下目标尚未声明，<c>ResolveTypeName</c>
/// 返回 null 后被 <c>?? IECType.DInt</c> 兜成 <c>REF_TO DInt</c>。两个版本都不等于
/// <c>REF_TO 目标类型</c>。
/// </para>
/// </summary>
public class ReferenceForwardDefectTests
{
    /// <summary>
    /// 复现：Holder 声明在 Target 之前，其 <c>REFERENCE TO Target</c> 字段。
    /// 修复前实际结果：<c>ReferencedType.Kind = VOID</c>、<c>Name = "VOID"</c>。
    /// </summary>
    [Fact]
    public void ReferenceToForwardTarget_ShouldBakeRealTargetType()
    {
        var result = FrontendTestHelper.Analyze(
            """
            TYPE Holder : STRUCT p : REFERENCE TO Target; END_STRUCT; END_TYPE
            TYPE Target : STRUCT v : INT; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR h : Holder; END_VAR
            h := h;
            END_PROGRAM
            """
        );

        var holder = result.Symbols.Resolve("Holder") as TypeSymbol;
        var fieldType = holder!.Type!.Fields!["p"];

        // 目标必须真的是 Target 的结构体布局，而不是占位符
        Assert.Equal(IECType.KindTag.REFERENCE, fieldType.Kind);
        Assert.Equal(IECType.KindTag.STRUCT, fieldType.ReferencedType!.Kind);
        Assert.Contains("v", fieldType.ReferencedType.Fields!.Keys);
    }

    /// <summary>
    /// 对照组：<b>反向</b>声明（Target 在前、Holder 在后）解析正确。
    /// 与上面那条一起，证明该缺陷是<b>声明顺序相关</b>的，而非普遍失效 ——
    /// 这也正是它此前不易被发现的原因。
    /// </summary>
    [Fact]
    public void ReferenceToBackwardTarget_BakesRealTargetType()
    {
        var result = FrontendTestHelper.Analyze(
            """
            TYPE Target : STRUCT v : INT; END_STRUCT; END_TYPE
            TYPE Holder : STRUCT p : REFERENCE TO Target; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR h : Holder; END_VAR
            h := h;
            END_PROGRAM
            """
        );

        var holder = result.Symbols.Resolve("Holder") as TypeSymbol;
        var fieldType = holder!.Type!.Fields!["p"];

        Assert.Equal(IECType.KindTag.REFERENCE, fieldType.Kind);
        Assert.Equal(IECType.KindTag.STRUCT, fieldType.ReferencedType!.Kind);
        Assert.Contains("v", fieldType.ReferencedType.Fields!.Keys);
    }

    /// <summary>
    /// 回填不能把已经正确的类型改坏：反向声明的目标类型实例必须保持不变。
    /// </summary>
    [Fact]
    public void ReferenceToBackwardTarget_KeepsSameTargetInstance()
    {
        var result = FrontendTestHelper.Analyze(
            """
            TYPE Target : STRUCT v : INT; END_STRUCT; END_TYPE
            TYPE Holder : STRUCT p : REFERENCE TO Target; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR h : Holder; END_VAR
            h := h;
            END_PROGRAM
            """
        );

        var target = (result.Symbols.Resolve("Target") as TypeSymbol)!.Type!;
        var holder = (result.Symbols.Resolve("Holder") as TypeSymbol)!.Type!;

        Assert.Same(target, holder.Fields!["p"].ReferencedType);
    }

    /// <summary>
    /// 互递归引用：<c>A.p : REFERENCE TO B</c> 与 <c>B.q : REFERENCE TO A</c>。
    /// 回填不递归进目标结构体内部，因此两个方向都应拿到对方的真实类型。
    /// </summary>
    [Fact]
    public void MutualForwardReferences_BakeBothTargets()
    {
        var result = FrontendTestHelper.Analyze(
            """
            TYPE NodeA : STRUCT other : REFERENCE TO NodeB; END_STRUCT; END_TYPE
            TYPE NodeB : STRUCT other : REFERENCE TO NodeA; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR a : NodeA; b : NodeB; END_VAR
            a := b;
            b := a;
            END_PROGRAM
            """
        );

        var a = (result.Symbols.Resolve("NodeA") as TypeSymbol)!.Type!;
        var b = (result.Symbols.Resolve("NodeB") as TypeSymbol)!.Type!;

        Assert.Same(b, a.Fields!["other"].ReferencedType);
        Assert.Same(a, b.Fields!["other"].ReferencedType);
    }

    /// <summary>
    /// 自引用递归类型：<c>TYPE Node : STRUCT next : REFERENCE TO Node; END_STRUCT</c>。
    /// 引用边不阻塞布局（契约 M5），目标必须回填成自身。
    /// </summary>
    [Fact]
    public void SelfForwardReference_BakesOwnType()
    {
        var result = FrontendTestHelper.Analyze(
            """
            TYPE Node : STRUCT next : REFERENCE TO Node; value : INT; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR n : Node; END_VAR
            n := n;
            END_PROGRAM
            """
        );

        var node = (result.Symbols.Resolve("Node") as TypeSymbol)!.Type!;

        Assert.Same(node, node.Fields!["next"].ReferencedType);
    }

    /// <summary>
    /// 引用藏在数组元素类型里：<c>ARRAY[0..1] OF REFERENCE TO Target</c>。
    /// 数组元素类型不可变，需重建数组类型再写回字段。
    /// </summary>
    [Fact]
    public void ArrayOfForwardReference_BakesRealTargetType()
    {
        var result = FrontendTestHelper.Analyze(
            """
            TYPE Holder : STRUCT items : ARRAY[0..1] OF REFERENCE TO Target; END_STRUCT; END_TYPE
            TYPE Target : STRUCT v : INT; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR h : Holder; END_VAR
            h := h;
            END_PROGRAM
            """
        );

        var holder = (result.Symbols.Resolve("Holder") as TypeSymbol)!.Type!;
        var element = holder.Fields!["items"].ElementType!;

        Assert.Equal(IECType.KindTag.REFERENCE, element.Kind);
        Assert.Equal(IECType.KindTag.STRUCT, element.ReferencedType!.Kind);
        Assert.Contains("v", element.ReferencedType.Fields!.Keys);
    }

    /// <summary>
    /// 嵌套：外层结构体按值内含内层结构体，内层持有前向引用。
    /// 回填走「就地改字段字典」，外层必须同步看到修正。
    /// </summary>
    [Fact]
    public void NestedStructWithForwardReference_BakesRealTargetType()
    {
        var result = FrontendTestHelper.Analyze(
            """
            TYPE Outer : STRUCT inner : Inner; END_STRUCT; END_TYPE
            TYPE Inner : STRUCT p : REFERENCE TO Target; END_STRUCT; END_TYPE
            TYPE Target : STRUCT v : INT; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR o : Outer; END_VAR
            o := o;
            END_PROGRAM
            """
        );

        var outer = (result.Symbols.Resolve("Outer") as TypeSymbol)!.Type!;
        var inner = outer.Fields!["inner"];
        var fieldType = inner.Fields!["p"];

        Assert.Equal(IECType.KindTag.STRUCT, fieldType.ReferencedType!.Kind);
        Assert.Contains("v", fieldType.ReferencedType.Fields!.Keys);
    }

    /// <summary>
    /// 回填必须让<b>表达式推导</b>也跟着对：经由前向引用访问目标字段
    /// 不应报「结构体没有字段」。
    /// </summary>
    [Fact]
    public void ForwardReference_FieldAccess_InfersRealTargetType()
    {
        var result = FrontendTestHelper.Analyze(
            """
            TYPE Holder : STRUCT p : REFERENCE TO Target; END_STRUCT; END_TYPE
            TYPE Target : STRUCT v : INT; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR h : Holder; r : INT; END_VAR
            r := h.p^.v;
            END_PROGRAM
            """
        );

        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Message.Contains("结构体没有字段")
        );
        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Message.Contains("解引用")
        );
    }

    /// <summary>
    /// 名字确实不存在时，仍然按 E2015 报错，且不得被回填“修好”。
    /// </summary>
    [Fact]
    public void UnknownReferenceTarget_StillReportsE2015_AndKeepsDIntFallback()
    {
        var result = FrontendTestHelper.Analyze(
            """
            TYPE Holder : STRUCT p : REFERENCE TO FAKE_TYPE; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR h : Holder; END_VAR
            h := h;
            END_PROGRAM
            """
        );

        Assert.Contains(
            result.Diagnostics,
            d => d.Code == IronST.Common.Diagnostics.ErrorCode.E2015
                && d.Message.Contains("FAKE_TYPE")
        );

        var holder = (result.Symbols.Resolve("Holder") as TypeSymbol)!.Type!;
        var fieldType = holder.Fields!["p"];

        Assert.Equal(IECType.KindTag.REFERENCE, fieldType.Kind);
        Assert.Equal(IECType.KindTag.DINT, fieldType.ReferencedType!.Kind);
    }
}
