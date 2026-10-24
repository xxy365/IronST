// ═══════════════════════════════════════════════════════════════
//  MathStandardLibrary.cs — 语言内建数学 HCALL 的默认实现
//
//  只包含由后端直接发出的运算（当前是整数取余）。它们不是宿主扩展：
//  如果每个宿主都要自己注册，`MOD` 会在 CLI / IDE / STModInvoker /
//  测试宿主之间出现“能编译、不能运行”的差异。
//
//  放在 IronST.Runtime.Shared，由 VMBase 在构造时安装；宿主显式注册的
//  同 ID 实现优先（HostBridge.RegisterIfAbsent）。
//
//  ⚠ 边界（2026-10-22 用户裁定 · 缺陷登记簿 §3.8）：
//  本类**不得**加入 IEC 标准库函数（`SQRT` / `SIN` / `LN` / 浮点 `ABS`…）的
//  实现 —— 内置 VM 不提供任何 IEC 数学函数。那些函数走「用户自建宿主注入」
//  路径（由使用者自行向 `hostFunctions` 登记 extern 名并提供 handler）。
//  本类只装**语言内建运算符**（如整数 `MOD`）的实现。
//  守卫：`IronST.Runtime.Tests/VMBuiltinMathGuardTests.cs`
// ═══════════════════════════════════════════════════════════════
namespace IronST.Runtime.Shared;

/// <summary>整数取余 HCALL（<c>MATH_IMOD</c> / <c>MATH_UMOD</c>）的默认实现。</summary>
public static class MathStandardLibrary
{
    /// <summary>
    /// 安装语言内建数学 HCALL；已注册的 ID 不会被覆盖。
    /// </summary>
    public static void Register(HostBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        bridge.RegisterIfAbsent(HostCallIds.MATH_IMOD, SignedRemainder);
        bridge.RegisterIfAbsent(HostCallIds.MATH_UMOD, UnsignedRemainder);
    }

    /// <summary>LLVM <c>srem</c>：余数符号跟随被除数。</summary>
    private static BcValue SignedRemainder(VMContext ctx)
    {
        long dividend = ctx.ReadReg(BytecodeBuilder.A0).IntValue;
        long divisor = ctx.ReadReg(BytecodeBuilder.A1).IntValue;
        if (divisor == 0)
            throw DivideByZero();

        // 特判：C# 的 long.MinValue % -1 在求商时会溢出。
        // 数学上余数为 0，也与 LLVM srem 的常见结果一致。
        if (divisor == -1)
            return new BcValue(0L);

        return new BcValue(dividend % divisor);
    }

    /// <summary>LLVM <c>urem</c>：把 64 位位模式按无符号解释后取余。</summary>
    private static BcValue UnsignedRemainder(VMContext ctx)
    {
        ulong dividend = unchecked((ulong)ctx.ReadReg(BytecodeBuilder.A0).IntValue);
        ulong divisor = unchecked((ulong)ctx.ReadReg(BytecodeBuilder.A1).IntValue);
        if (divisor == 0)
            throw DivideByZero();

        return new BcValue(unchecked((long)(dividend % divisor)));
    }

    private static RuntimeErrorException DivideByZero()
        => new(RuntimeErrorType.DivideByZero, 0, "Divide by zero");
}
