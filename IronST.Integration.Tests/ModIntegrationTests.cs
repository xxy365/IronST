using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

/// <summary>
/// IEC 61131-3 <c>MOD</c> 走完整前端 → LLVM IR → 字节码 → VM 路径。
/// 后端把它降为 HCALL；这些用例防止“能编译、运行时 HCALL unregistered”回归。
/// </summary>
public class ModIntegrationTests
{
    [Fact]
    public void ModOperator_SignedOperands_UsesDividendSign()
    {
        var run = StRunner.Run("""
            FUNCTION Remainder : DINT
            VAR_INPUT
                dividend : DINT;
                divisor : DINT;
            END_VAR
                Remainder := dividend MOD divisor;
            END_FUNCTION

            PROGRAM Main
            VAR
                value : DINT;
            END_VAR
                value := Remainder(17, 5);
                Log(value);
                value := Remainder(-17, 5);
                Log(value);
            END_PROGRAM
            """, host => host.RegisterAction("Log", 1, _ => { }));

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
        Assert.Equal(
            new long[] { 2, -2 },
            run.Calls.Select(c => c.Arguments[0].IntValue));
    }

    [Fact]
    public void ModOperator_ZeroDivisor_ReportsDivideByZero()
    {
        var run = StRunner.RunExpectingError("""
            PROGRAM Main
            VAR
                dividend : DINT;
                divisor : DINT;
                value : DINT;
            END_VAR
                dividend := 17;
                divisor := 0;
                value := dividend MOD divisor;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.NotNull(run.Error);
        Assert.Equal(RuntimeErrorType.DivideByZero, run.Error!.ErrorType);
    }
}
