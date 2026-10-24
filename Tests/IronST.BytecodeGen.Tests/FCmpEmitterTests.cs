using IronST.BytecodeGen;
using IronST.IRBuilder.IR;
using IronST.Runtime.Debug;
using IronST.Runtime.Shared;

namespace IronST.Tests;

/// <summary>
/// FCmp → 字节码 → VM 执行，端到端测试。
/// 验证 FunctionEmitter.EmitFCmp 对每种 FCmpPredicate 的字节码生成是否正确。
///
/// 当前已知 Bug（修复前应有 6 个 Fail）：
///   - One:  使用 NOT(按位取反) 而非 XORI 1(布尔取反)，结果为 -1/-2
///   - Ogt:  FLT 未交换操作数，语义完全反转
///   - Oge:  落入 default 用 FEQ，仅相等时碰巧正确
///   - Ole:  FLT 无 NOT/XORI，相等时判 false
/// </summary>
public class FCmpEmitterTests
{
    /// <summary>
    /// 构建最小 IR 模块：一个 "main" 函数，执行 fcmp 后返回结果。
    /// 编译为字节码，用 DebugVM 执行，返回 A0 寄存器的值。
    /// </summary>
    private static BcValue RunFCmp(FCmpPredicate predicate, double left, double right)
    {
        // ---- 构建 IR ----
        var module = new Module();

        // Function 构造函数需要 name + FunctionType
        var funcType = new FunctionType(IrType.Int1, Array.Empty<IrType>());
        var func = new Function("main", funcType);
        module.AddFunction(func);

        var entry = new BasicBlock { Name = "entry" };
        func.AppendBasicBlock(entry);

        var lhs = new ConstantFP(IrType.Double, left);
        var rhs = new ConstantFP(IrType.Double, right);
        var cmp = new FCmpInst(predicate, lhs, rhs, "cmp");
        entry.AddInstruction(cmp);

        // FCmp 结果 (i1) 作为返回值，BytecodeGen 会 MOV 到 A0
        var ret = new ReturnInst(cmp);
        entry.AddInstruction(ret);

        // ---- 编译 ----
        var gen = new ByteCodeGenerator();
        var compileResult = gen.Generate(module);

        // ---- 执行 ----
        var session = new VMDebugSession();
        var vm = new DebugVM(compileResult.Module, new VMDevOptions
        {
            DebugSession = session
        });
        vm.Run();

        // ---- 读取返回值 ----
        return vm.Context.ReadReg(BytecodeBuilder.A0);
    }

    // ========================================================
    //  OEQ — 映射 FEQ，当前正确
    // ========================================================

    [Fact]
    public void Oeq_Equal_Returns1()
    {
        var r = RunFCmp(FCmpPredicate.Oeq, 5.0, 5.0);
        Assert.Equal(1L, r.AsInt64());
    }

    [Fact]
    public void Oeq_NotEqual_Returns0()
    {
        var r = RunFCmp(FCmpPredicate.Oeq, 5.0, 3.0);
        Assert.Equal(0L, r.AsInt64());
    }

    // ========================================================
    //  ONE — Bug: NOT(0)=-1 / NOT(1)=-2，期望 1/0
    // ========================================================

    [Fact]
    public void One_NotEqual_Returns1()
    {
        var r = RunFCmp(FCmpPredicate.One, 3.0, 5.0);
        Assert.Equal(1L, r.AsInt64());
    }

    [Fact]
    public void One_Equal_Returns0()
    {
        var r = RunFCmp(FCmpPredicate.One, 5.0, 5.0);
        Assert.Equal(0L, r.AsInt64());
    }

    // ========================================================
    //  OLT — 映射 FLT(s1, s2)，操作数顺序正确，当前正确
    // ========================================================

    [Fact]
    public void Olt_LeftLess_Returns1()
    {
        var r = RunFCmp(FCmpPredicate.Olt, 2.0, 3.0);
        Assert.Equal(1L, r.AsInt64());
    }

    [Fact]
    public void Olt_LeftGreater_Returns0()
    {
        var r = RunFCmp(FCmpPredicate.Olt, 3.0, 2.0);
        Assert.Equal(0L, r.AsInt64());
    }

    // ========================================================
    //  OGT — Bug: FLT(s1, s2) 应为 FLT(s2, s1)
    // ========================================================

    [Fact]
    public void Ogt_LeftGreater_Returns1()
    {
        var r = RunFCmp(FCmpPredicate.Ogt, 3.0, 2.0);
        Assert.Equal(1L, r.AsInt64());
    }

    [Fact]
    public void Ogt_LeftLess_Returns0()
    {
        var r = RunFCmp(FCmpPredicate.Ogt, 2.0, 3.0);
        Assert.Equal(0L, r.AsInt64());
    }

    [Fact]
    public void Ogt_Equal_Returns0()
    {
        var r = RunFCmp(FCmpPredicate.Ogt, 2.0, 2.0);
        Assert.Equal(0L, r.AsInt64());
    }

    // ========================================================
    //  OGE — Bug: 落入 default 用 FEQ
    // ========================================================

    [Fact]
    public void Oge_LeftGreater_Returns1()
    {
        var r = RunFCmp(FCmpPredicate.Oge, 3.0, 2.0);
        Assert.Equal(1L, r.AsInt64());
    }

    [Fact]
    public void Oge_Equal_Returns1()
    {
        var r = RunFCmp(FCmpPredicate.Oge, 2.0, 2.0);
        Assert.Equal(1L, r.AsInt64());
    }

    [Fact]
    public void Oge_LeftLess_Returns0()
    {
        var r = RunFCmp(FCmpPredicate.Oge, 2.0, 3.0);
        Assert.Equal(0L, r.AsInt64());
    }

    // ========================================================
    //  OLE — Bug: FLT(s1, s2) 无 XORI，相等时判 false
    // ========================================================

    [Fact]
    public void Ole_LeftLess_Returns1()
    {
        var r = RunFCmp(FCmpPredicate.Ole, 2.0, 3.0);
        Assert.Equal(1L, r.AsInt64());
    }

    [Fact]
    public void Ole_Equal_Returns1()
    {
        var r = RunFCmp(FCmpPredicate.Ole, 2.0, 2.0);
        Assert.Equal(1L, r.AsInt64());
    }

    [Fact]
    public void Ole_LeftGreater_Returns0()
    {
        var r = RunFCmp(FCmpPredicate.Ole, 3.0, 2.0);
        Assert.Equal(0L, r.AsInt64());
    }
}