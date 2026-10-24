// NumericConversionEndToEndTests.cs — 源⑥：实数 ↔ 整数 跨界转换缺失
// （缺陷登记簿 §3.9 · 契约 §7 L5/L7）
//
// 语义分析通过 ≠ 值算对。这里把 ST 源码整条跑完：
//   FrontendPipeline（HostBuiltins）→ IRGenerator → ByteCodeGenerator
//   （hostFunctions = { ["Print"] = 0 }）→ 发布 VM（HostBridge 捕获 A0）。
//
// 观测通道：宿主 Print 读 A0 的 BcValue，断言 Tag（Int64 / Float64）+ 数值。
//
// 修复前实测（2026-10-22 探针）：
//   • `avg := sum / 2.0`（sum:INT · avg:REAL）—— 实数分支未把整数操作数提升为实数，
//     IR 出 `fdiv i16 %sum.ld, 2`，结果类型随左操作数退化为 i16 ⇒ 存进 float 槽后
//     读出 1.4E-322 之类的垃圾值。
//   • `d := r`（d:DINT · r:REAL）—— 赋值不按目标类型转换 ⇒ 得错值。
//   • 整数内部、实数内部的路径**都是对的** ⇒ 缺陷只在「整数 ↔ 实数」这个边界上。
using IronST.Frontend;
using IronST.Frontend.Analysis;
using IronST.LLVMCodeGen.CodeGen;
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.BytecodeGen.Tests;

public class NumericConversionEndToEndTests
{
    /// <summary>编译并运行，返回宿主 <c>Print</c> 每次调用读到的 <c>A0</c>（按调用顺序）。</summary>
    private static List<BcValue> CompileAndRun(string source)
    {
        var analysis = FrontendPipeline.Analyze(source, "test.st", [HostBuiltins.Create()]);
        var errors = analysis.Diagnostics
            .Where(d => d.Severity == Common.Diagnostics.DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            analysis.Success,
            $"frontend failed with {errors.Count} error(s):\n"
                + string.Join("\n", errors.Select(e => $"  {e}")));

        var module = new IRGenerator().Generate(analysis);
        var result = new ByteCodeGenerator(
            "test.st",
            hostFunctions: new Dictionary<string, ushort>(StringComparer.Ordinal) { ["Print"] = 0 }
        ).Generate(module);

        var printed = new List<BcValue>();
        var bridge = new HostBridge();
        bridge.Register(0, ctx =>
        {
            printed.Add(ctx.ReadReg(BytecodeBuilder.A0));
            return BcValue.Void;
        });

        var vm = new VM(result.Module, new VMOptions
        {
            StackSize = 8192,
            HostBridge = bridge,
            StructTypes = result.StructRegistrations,
        });
        vm.Run();

        Assert.Equal(VMState.Halted, vm.State);
        Assert.Null(vm.LastError);
        return printed;
    }

    private static void AssertInt(long expected, BcValue actual)
    {
        Assert.Equal(ValueTag.Int64, actual.Tag);
        Assert.Equal(expected, actual.IntValue);
    }

    private static void AssertReal(double expected, BcValue actual)
    {
        Assert.Equal(ValueTag.Float64, actual.Tag);
        Assert.Equal(expected, actual.FloatValue, 6);
    }

    // ═══════════════════════════════════════════════════════════════
    //  运算侧：整数操作数未提升为实数
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>INT / REAL</c> 必须按实数算：55 / 2.0 = 27.5。
    /// 修复前 <c>fdiv</c> 的左操作数是 i16，结果类型随之退化 ⇒ 值错。
    /// </summary>
    [Fact]
    public void IntDivReal_ComputesInRealArithmetic()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                sum : INT;
                avg : REAL;
            END_VAR
                sum := 55;
                avg := sum / 2.0;
                Print(avg);
            END_PROGRAM
            """);

        AssertReal(27.5, Assert.Single(printed));
    }

    /// <summary>
    /// <c>REAL / INT</c> 方向同样要提升（右操作数是整数时）。
    /// </summary>
    [Fact]
    public void RealDivInt_ComputesInRealArithmetic()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                n : INT;
                avg : REAL;
            END_VAR
                n := 2;
                avg := 55.0 / n;
                Print(avg);
            END_PROGRAM
            """);

        AssertReal(27.5, Assert.Single(printed));
    }

    /// <summary>
    /// 整数与实数混合的加减乘同样走实数运算。
    /// </summary>
    [Fact]
    public void MixedArithmetic_StaysInRealDomain()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                n : INT;
                r : REAL;
            END_VAR
                n := 4;
                r := n * 1.5 + 2.0;
                Print(r);
            END_PROGRAM
            """);

        AssertReal(8.0, Assert.Single(printed));
    }

    // ═══════════════════════════════════════════════════════════════
    //  赋值侧：按目标变量类型转换
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 实数赋给整数必须截断（向零取整）：55.7 → 55。
    /// 修复前赋值不按目标类型转换 ⇒ 得错值。
    /// </summary>
    [Fact]
    public void RealAssignedToInt_IsTruncated()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                r : REAL;
                d : DINT;
            END_VAR
                r := 55.7;
                d := r;
                Print(d);
            END_PROGRAM
            """);

        AssertInt(55, Assert.Single(printed));
    }

    /// <summary>
    /// 实数赋给整数：负数向零取整（-55.7 → -55，不是 -56）。
    /// </summary>
    [Fact]
    public void NegativeRealAssignedToInt_TruncatesTowardZero()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                r : REAL;
                d : DINT;
            END_VAR
                r := -55.7;
                d := r;
                Print(d);
            END_PROGRAM
            """);

        AssertInt(-55, Assert.Single(printed));
    }

    // ═══════════════════════════════════════════════════════════════
    //  比较运算：INT 与 REAL 比较必须先统一到实数域
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>INT = REAL</c> 必须按实数比较：<c>5 = 5.0</c> 为 TRUE。
    ///
    /// <para>
    /// 修复前 <c>EmitComparison</c> 只看**左**操作数的类型决定发 <c>icmp</c> 还是
    /// <c>fcmp</c>，于是 <c>i = r</c>（左 INT · 右 REAL）生成
    /// <c>icmp eq i16 %i.ld, %r.ld</c> —— 操作数类型不一致的非法 IR，
    /// 后端把整数位模式当实数位模式比，5 ≠ 5.0 ⇒ 得 FALSE。
    /// </para>
    /// </summary>
    [Fact]
    public void IntEqualsReal_ComparesInRealDomain()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                i : INT;
                r : REAL;
                b : BOOL;
            END_VAR
                i := 5;
                r := 5.0;
                b := i = r;
                Print(b);
            END_PROGRAM
            """);

        AssertInt(1, Assert.Single(printed));
    }

    /// <summary><c>INT &lt;&gt; REAL</c>：5 ≠ 6.0 为 TRUE。</summary>
    [Fact]
    public void IntNotEqualsReal_ComparesInRealDomain()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                i : INT;
                r : REAL;
                b : BOOL;
            END_VAR
                i := 5;
                r := 6.0;
                b := i <> r;
                Print(b);
            END_PROGRAM
            """);

        AssertInt(1, Assert.Single(printed));
    }

    /// <summary>
    /// <c>INT &lt; REAL</c>：5 &lt; 100.5 为 TRUE；5 &lt; 4.5 为 FALSE。
    /// 两例一起锁住「整数位模式当实数比」的反向错误（真值恰好相反）。
    /// </summary>
    [Theory]
    [InlineData(100.5, 1)]
    [InlineData(4.5, 0)]
    public void IntLessThanReal_ComparesInRealDomain(double realValue, long expected)
    {
        var printed = CompileAndRun($$"""
            PROGRAM Main
            VAR
                i : INT;
                r : REAL;
                b : BOOL;
            END_VAR
                i := 5;
                r := {{realValue}};
                b := i < r;
                Print(b);
            END_PROGRAM
            """);

        AssertInt(expected, Assert.Single(printed));
    }

    /// <summary><c>REAL &gt; INT</c>（左实数 · 右整数）：4.5 &gt; 5 为 FALSE。</summary>
    [Fact]
    public void RealGreaterThanInt_ComparesInRealDomain()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                i : INT;
                r : REAL;
                b : BOOL;
            END_VAR
                i := 5;
                r := 4.5;
                b := r > i;
                Print(b);
            END_PROGRAM
            """);

        AssertInt(0, Assert.Single(printed));
    }

    /// <summary><c>INT &gt;= REAL</c>：5 &gt;= 5.0 为 TRUE。</summary>
    [Fact]
    public void IntGreaterOrEqualReal_ComparesInRealDomain()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                i : INT;
                r : REAL;
                b : BOOL;
            END_VAR
                i := 5;
                r := 5.0;
                b := i >= r;
                Print(b);
            END_PROGRAM
            """);

        AssertInt(1, Assert.Single(printed));
    }

    /// <summary>
    /// 反方向：<c>REAL = INT</c>（左实数 · 右整数）同样按实数域比较：5.0 = 5 为 TRUE。
    /// </summary>
    /// <remarks>
    /// 修复前 <c>EmitComparison</c> 只看**左**操作数的类型发指令，
    /// 这里会发 <c>fcmp</c> —— 而右侧的整数在 FloatValue 字段上是 0 ⇒ 得 FALSE。
    /// </remarks>
    [Fact]
    public void RealEqualsInt_ComparesInRealDomain()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                i : INT;
                r : REAL;
                b : BOOL;
            END_VAR
                i := 5;
                r := 5.0;
                b := r = i;
                Print(b);
            END_PROGRAM
            """);

        AssertInt(1, Assert.Single(printed));
    }

    /// <summary>反方向：<c>REAL &lt;&gt; INT</c>：5.0 &lt;&gt; 5 为 FALSE。</summary>
    [Fact]
    public void RealNotEqualsInt_ComparesInRealDomain()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                i : INT;
                r : REAL;
                b : BOOL;
            END_VAR
                i := 5;
                r := 5.0;
                b := r <> i;
                Print(b);
            END_PROGRAM
            """);

        AssertInt(0, Assert.Single(printed));
    }

    // ═══════════════════════════════════════════════════════════════
    //  未穷举边界（§3.9 末尾那条边界）：这些路径必须同样正确
    // ═══════════════════════════════════════════════════════════════

    /// <summary>数组元素赋值：实数赋给 <c>ARRAY OF DINT</c> 的元素要截断。</summary>
    [Fact]
    public void ArrayElement_RealAssignedToInt_IsTruncated()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                arr : ARRAY[0..2] OF DINT;
                r : REAL;
            END_VAR
                r := 55.7;
                arr[1] := r;
                Print(arr[1]);
            END_PROGRAM
            """);

        AssertInt(55, Assert.Single(printed));
    }

    /// <summary>结构体成员赋值：实数赋给 <c>DINT</c> 成员要截断。</summary>
    [Fact]
    public void StructMember_RealAssignedToInt_IsTruncated()
    {
        var printed = CompileAndRun("""
            TYPE Point : STRUCT
            x : DINT;
            END_STRUCT; END_TYPE;

            PROGRAM Main
            VAR
                p : Point;
                r : REAL;
            END_VAR
                r := 55.7;
                p.x := r;
                Print(p.x);
            END_PROGRAM
            """);

        AssertInt(55, Assert.Single(printed));
    }

    /// <summary>函数返回值：<c>REAL</c> 返回值赋给 <c>DINT</c> 要截断。</summary>
    [Fact]
    public void FunctionReturn_RealAssignedToInt_IsTruncated()
    {
        var printed = CompileAndRun("""
            FUNCTION Half : REAL
            VAR_INPUT
                x : REAL;
            END_VAR
                Half := x / 2.0;
            END_FUNCTION

            PROGRAM Main
            VAR
                d : DINT;
            END_VAR
                d := Half(55.0);
                Print(d);
            END_PROGRAM
            """);

        AssertInt(27, Assert.Single(printed));
    }

    /// <summary>整数赋给实数变量：55 要变成 55.0（值 + tag 都对）。</summary>
    [Fact]
    public void IntAssignedToReal_BecomesRealValue()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                i : INT;
                r : REAL;
            END_VAR
                i := 55;
                r := i;
                Print(r);
            END_PROGRAM
            """);

        AssertReal(55.0, Assert.Single(printed));
    }

    /// <summary>无符号整数提升：<c>UINT / REAL</c> 走 <c>UIToFP</c>。</summary>
    [Fact]
    public void UIntDivReal_ComputesInRealArithmetic()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                u : UINT;
                r : REAL;
            END_VAR
                u := 55;
                r := u / 2.0;
                Print(r);
            END_PROGRAM
            """);

        AssertReal(27.5, Assert.Single(printed));
    }

    /// <summary>实数赋给无符号整数：走 <c>FPToUI</c>。</summary>
    [Fact]
    public void RealAssignedToUInt_IsTruncated()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                r : REAL;
                u : UINT;
            END_VAR
                r := 55.7;
                u := r;
                Print(u);
            END_PROGRAM
            """);

        AssertInt(55, Assert.Single(printed));
    }

    /// <summary>FOR 上界是实数：<c>FOR i := 1 TO lim</c>（<c>lim: REAL</c>）要按整数比较。</summary>
    [Fact]
    public void ForBoundReal_TruncatesToCounterRange()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                i : INT;
                n : INT;
                lim : REAL;
            END_VAR
                lim := 3.0;
                n := 0;
                FOR i := 1 TO lim DO
                    n := n + 1;
                END_FOR
                Print(n);
            END_PROGRAM
            """);

        AssertInt(3, Assert.Single(printed));
    }

    // ═══════════════════════════════════════════════════════════════
    //  对照组：这些路径修复前就是对的，不得回归
    // ═══════════════════════════════════════════════════════════════

    /// <summary>整数宽化到 DINT：值不变。</summary>
    [Fact]
    public void IntWidenedToDint_KeepsValue()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                i : INT;
                d : DINT;
            END_VAR
                i := 7;
                d := i;
                Print(d);
            END_PROGRAM
            """);

        AssertInt(7, Assert.Single(printed));
    }

    /// <summary>整数窄化到 INT：值不变。</summary>
    [Fact]
    public void DintNarrowedToInt_KeepsValue()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                d : DINT;
                i : INT;
            END_VAR
                d := 7;
                i := d;
                Print(i);
            END_PROGRAM
            """);

        AssertInt(7, Assert.Single(printed));
    }

    /// <summary>实数宽化到 LREAL：值不变。</summary>
    [Fact]
    public void RealWidenedToLreal_KeepsValue()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                r : REAL;
                lr : LREAL;
            END_VAR
                r := 2.5;
                lr := r;
                Print(lr);
            END_PROGRAM
            """);

        AssertReal(2.5, Assert.Single(printed));
    }

    /// <summary>REAL 字面量赋值（修复前就是对的）。</summary>
    [Fact]
    public void RealLiteral_AssignsRealValue()
    {
        var printed = CompileAndRun("""
            PROGRAM Main
            VAR
                r : REAL;
            END_VAR
                r := 1.5;
                Print(r);
            END_PROGRAM
            """);

        AssertReal(1.5, Assert.Single(printed));
    }
}
