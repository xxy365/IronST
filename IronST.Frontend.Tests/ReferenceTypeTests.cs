// ReferenceTypeTests.cs — F1：REFERENCE TO 目标类型未知必须可见
using IronST.Common.Diagnostics;
using IronST.Common.Symbol;
using IronST.Common.Types;
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

/// <summary>
/// F1：此前 <c>ResolveTypeName(T) ?? IECType.DInt</c> 让 <c>REFERENCE TO 写错的名</c>
/// 静默变成 <c>REF_TO DInt</c> —— 编译器当成完全合法的引用类型接受，全程零诊断，
/// 「打错字」与「就是想引用 DInt」无法区分。
/// </summary>
public class ReferenceTypeTests
{
    private static List<Diagnostic> E2015s(AnalysisResult r) =>
        r.Diagnostics.Where(d => d.Code == ErrorCode.E2015).ToList();

    // ═══════════════════════════════════════════════════════════════
    //  该报错的场景
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ProgramVar_ReferenceToUnknown_ReportsE2015()
    {
        var result = FrontendTestHelper.Analyze(
            """
            PROGRAM Main
            VAR x : REFERENCE TO FAKE_TYPE; END_VAR
            x := x;
            END_PROGRAM
            """
        );

        var err = E2015s(result).FirstOrDefault();
        Assert.NotNull(err);
        Assert.Contains("FAKE_TYPE", err!.Message);
    }

    [Fact]
    public void FunctionParam_ReferenceToUnknown_ReportsE2015()
    {
        var result = FrontendTestHelper.Analyze(
            """
            FUNCTION F : INT
            VAR_INPUT
                p : REFERENCE TO GHOST;
            END_VAR
            F := 0;
            END_FUNCTION
            """
        );

        Assert.Contains(E2015s(result), d => d.Message.Contains("GHOST"));
    }

    [Fact]
    public void StructMember_ReferenceToUnknown_ReportsE2015()
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

        Assert.Contains(E2015s(result), d => d.Message.Contains("FAKE_TYPE"));
    }

    [Fact]
    public void TypeAlias_ReferenceToUnknown_ReportsE2015()
    {
        var result = FrontendTestHelper.Analyze(
            """
            TYPE Alias : REFERENCE TO NOPE; END_TYPE

            PROGRAM Main
            VAR a : Alias; END_VAR
            a := a;
            END_PROGRAM
            """
        );

        Assert.Contains(E2015s(result), d => d.Message.Contains("NOPE"));
    }

    // ═══════════════════════════════════════════════════════════════
    //  不该报错的场景
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ReferenceToBuiltin_NoError()
    {
        FrontendTestHelper.ExpectSuccess(
            """
            PROGRAM Main
            VAR r : REFERENCE TO INT; END_VAR
            r := r;
            END_PROGRAM
            """
        );
    }

    /// <summary>
    /// 占位符不得被误判成未知：F7 的预声明把未解析类型登记为 <c>IECType.Void</c>
    /// （非 null），所以 <c>ResolveTypeName</c> 只在名字确实不存在时才返回 null。
    /// </summary>
    [Fact]
    public void ReferenceToForwardDeclaredType_NoFalseE2015()
    {
        // Holder 引用的 Target 声明在它之后
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

        Assert.Empty(E2015s(result));
    }

    // ═══════════════════════════════════════════════════════════════
    //  错误恢复的形状
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 报错之后退回的类型必须<b>仍是 REFERENCE</b>，而不是整体降成 DInt ——
    /// 否则 F7 的布局分类（引用边不阻塞）与 <c>REF=</c> 的目标类型检查都会被
    /// 错误恢复带偏，一个错误会扩散成一串无关错误。
    /// </summary>
    [Fact]
    public void ErrorRecovery_KeepsReferenceShape()
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

        Assert.NotEmpty(E2015s(result));

        var holder = result.Symbols.Resolve("Holder") as TypeSymbol;
        Assert.Equal(
            IECType.KindTag.REFERENCE,
            holder!.Type!.Fields!["p"].Kind
        );
    }

    // ═══════════════════════════════════════════════════════════════
    //  解引用类型识别（①）与诊断文本（②）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// ① 此前 InferType 的解引用分支判的是 <c>ElementType</c>（ARRAY 的字段），
    /// 而 <c>MakeReference</c> 只写 <c>ReferencedType</c> —— 条件恒假，于是合法的
    /// <c>r^</c> 一律被误判成「无法对非引用类型进行解引用」。
    /// </summary>
    [Fact]
    public void Dereference_ValidReference_ProducesNoDiagnostic()
    {
        var result = FrontendTestHelper.Analyze(
            """
            PROGRAM Main
            VAR
                r : REFERENCE TO INT;
                i : INT;
            END_VAR
            r^ := 5;
            i := r^;
            END_PROGRAM
            """
        );

        Assert.Empty(
            result.Diagnostics.Where(d => d.Message.Contains("解引用")).ToList()
        );
    }

    /// <summary>非引用类型解引用仍必须报错（① 只放宽合法引用，不放掉非法用法）。</summary>
    [Fact]
    public void Dereference_OnNonReference_StillErrors()
    {
        var result = FrontendTestHelper.Analyze(
            """
            PROGRAM Main
            VAR x : INT; i : INT; END_VAR
            i := x^;
            END_PROGRAM
            """
        );

        Assert.Contains(
            result.Diagnostics,
            d => d.Code == ErrorCode.E2015 && d.Message.Contains("解引用")
        );
    }

    /// <summary>
    /// ② <c>GetIECTypeDisplayString</c> 的 REFERENCE 分支同样错读 <c>ElementType</c>，
    /// 导致诊断与调用签名里所有引用类型都显示成 <c>REF_TO UNKNOWN</c>。
    /// 这里经由 <see cref="CallArgumentInfo.ParameterTypeName"/> 这一公开出口断言。
    /// </summary>
    [Fact]
    public void TypeDisplay_Reference_ShowsRealTarget_NotUnknown()
    {
        var result = FrontendTestHelper.Analyze(
            """
            FUNCTION Take : INT
            VAR_INPUT p : REFERENCE TO INT; END_VAR
            Take := 0;
            END_FUNCTION

            PROGRAM Main
            VAR r : REFERENCE TO INT; END_VAR
            r := r;
            Take(r);
            END_PROGRAM
            """
        );

        var call = Assert.Single(result.CallSites);
        var arg = Assert.Single(call.Arguments);

        Assert.Equal("REF_TO INT", arg.ParameterTypeName);
        Assert.DoesNotContain("UNKNOWN", arg.ParameterTypeName);
    }
}
