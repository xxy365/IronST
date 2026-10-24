// DuplicateDiagnosticTests.cs — 未知类型诊断的唯一性（④）
using IronST.Common.Diagnostics;
using IronST.Frontend.Analysis;

namespace IronST.Frontend.Tests;

/// <summary>
/// ④ 同一个 <c>DataTypeNode</c> 会被两趟各遍历一次：<c>RegisterPOU</c> 走
/// <c>CollectParameters</c>（函数签名需要参数类型），<c>AnalyzePOU</c> 走自身 VarBlocks
/// （要把变量声明进作用域）。两趟都调 <c>ResolveDataTypeNode</c>，于是函数参数的未知
/// 类型此前报 <b>2 次</b>（实测基线 SIMPLE_PARAM=2）。
///
/// <para>
/// 唯一性按<b>声明节点实例</b>判定，<b>不是</b>按诊断文案去重 —— 否则不同源码位置上
/// 的同名错误会被一起吞掉。第二条用例正是守住这条边界。
/// </para>
/// </summary>
public class DuplicateDiagnosticTests
{
    private static int E2015Count(AnalysisResult r) =>
        r.Diagnostics.Count(d => d.Code == ErrorCode.E2015);

    /// <summary>同一处参数声明，未知类型只报一次。</summary>
    [Fact]
    public void FunctionParam_UnknownType_ReportedOnce()
    {
        var result = FrontendTestHelper.Analyze(
            """
            FUNCTION F : INT
            VAR_INPUT
                x : FAKE_TYPE;
            END_VAR
            F := 0;
            END_FUNCTION
            """
        );

        Assert.Equal(1, E2015Count(result));
    }

    /// <summary>函数输出参数同样只报一次。</summary>
    [Fact]
    public void FunctionOutputParam_UnknownType_ReportedOnce()
    {
        var result = FrontendTestHelper.Analyze(
            """
            FUNCTION F : INT
            VAR_OUTPUT
                y : GHOST;
            END_VAR
            F := 0;
            END_FUNCTION
            """
        );

        Assert.Equal(1, E2015Count(result));
    }

    /// <summary>
    /// 关键边界：两个<b>不同</b> POU 各自声明了同名未知类型 —— 文案完全相同，
    /// 但源码位置不同，必须各报一次，不得被「按文案去重」一起吞掉。
    /// </summary>
    [Fact]
    public void SameUnknownName_DifferentDeclarations_BothReport()
    {
        var result = FrontendTestHelper.Analyze(
            """
            FUNCTION F1 : INT
            VAR_INPUT
                x : FAKE;
            END_VAR
            F1 := 0;
            END_FUNCTION

            FUNCTION F2 : INT
            VAR_INPUT
                y : FAKE;
            END_VAR
            F2 := 0;
            END_FUNCTION
            """
        );

        Assert.Equal(2, E2015Count(result));
    }

    /// <summary>程序变量只被 AnalyzePOU 遍历一次，本来就该是 1 —— ④ 不得误伤它。</summary>
    [Fact]
    public void ProgramVar_UnknownType_ReportedOnce()
    {
        var result = FrontendTestHelper.Analyze(
            """
            PROGRAM Main
            VAR a : FAKE1; b : FAKE2; END_VAR
            a := 1;
            END_PROGRAM
            """
        );

        Assert.Equal(2, E2015Count(result));
    }
}
