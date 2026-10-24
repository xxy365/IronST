// filename: IronST.LLVMCodeGen.Tests/ControlFlowCodeGenTests.cs
using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

public class ControlFlowCodeGenTests
{
    [Fact]
    public void IfThen_ShouldGenerateConditionalBranch()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR x : DINT; y : DINT; END_VAR
                x := 1;
                IF x > 0 THEN
                    y := 1;
                END_IF;
            END_PROGRAM
            """
        );

        // 条件比较：x > 0
        Assert.Contains("icmp sgt i32", ir);
        // 条件分支
        Assert.Contains("br i1", ir);
        // THEN 块（NameGenerator 带数字后缀）
        Assert.Contains("if.then1:", ir);
        // THEN 块中的赋值
        Assert.Contains("store i32 1, ptr %y", ir);
        // 合并块
        Assert.Contains("if.end1:", ir);
    }

    [Fact]
    public void IfThenElse_ShouldGenerateBothBlocks()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR x : DINT; y : DINT; END_VAR
                x := 1;
                IF x > 0 THEN
                    y := 1;
                ELSE
                    y := 0;
                END_IF;
            END_PROGRAM
            """
        );

        // THEN 块和 ELSE 块
        Assert.Contains("if.then1:", ir);
        Assert.Contains("if.else1:", ir);
        // 合并块
        Assert.Contains("if.end1:", ir);
        // THEN 分支赋值
        Assert.Contains("store i32 1, ptr %y", ir);
        // ELSE 分支赋值
        Assert.Contains("store i32 0, ptr %y", ir);
        // THEN 块无条件跳转到 end
        Assert.Contains("br label %if.end1", ir);
    }

    [Fact]
    public void IfWithElsif_ShouldGenerateChain()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR x : DINT; y : DINT; END_VAR
                x := 2;
                IF x = 1 THEN
                    y := 10;
                ELSIF x = 2 THEN
                    y := 20;
                ELSIF x = 3 THEN
                    y := 30;
                ELSE
                    y := 0;
                END_IF;
            END_PROGRAM
            """
        );

        // THEN 块
        Assert.Contains("if.then1:", ir);
        // 两个 ELSIF 条件块（带序号）
        Assert.Contains("elsif.cond1:", ir);
        Assert.Contains("elsif.cond2:", ir);
        // 两个 ELSIF 体块
        Assert.Contains("elsif.then1:", ir);
        Assert.Contains("elsif.then2:", ir);
        // ELSE 块
        Assert.Contains("if.else1:", ir);
        // 合并块
        Assert.Contains("if.end1:", ir);
        // 各分支赋值
        Assert.Contains("store i32 10, ptr %y", ir);
        Assert.Contains("store i32 20, ptr %y", ir);
        Assert.Contains("store i32 30, ptr %y", ir);
        Assert.Contains("store i32 0, ptr %y", ir);
    }

    [Fact]
    public void ForLoop_ShouldGenerateCondBodyIncBlocks()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR i : DINT; sum : DINT; END_VAR
                sum := 0;
                FOR i := 1 TO 10 BY 1 DO
                    sum := sum + i;
                END_FOR;
            END_PROGRAM
            """
        );

        // 四个基本块
        Assert.Contains("for.cond1:", ir);
        Assert.Contains("for.body1:", ir);
        Assert.Contains("for.inc1:", ir);
        Assert.Contains("for.end1:", ir);
        // 初始值 i := 1
        Assert.Contains("store i32 1, ptr %i", ir);
        // 条件判断 i <= 10
        Assert.Contains("icmp sle i32", ir);
        // 循环体有加法
        Assert.Contains("add i32", ir);
        // 条件分支
        Assert.Contains("br i1", ir);
    }

    [Fact]
    public void ForLoopWithNegativeStep_ShouldUseStepValue()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR i : DINT; END_VAR
                FOR i := 10 TO 0 BY -1 DO
                    i := i;
                END_FOR;
            END_PROGRAM
            """
        );

        // 四个基本块
        Assert.Contains("for.cond1:", ir);
        Assert.Contains("for.body1:", ir);
        Assert.Contains("for.inc1:", ir);
        Assert.Contains("for.end1:", ir);
        // 初始值 i := 10
        Assert.Contains("store i32 10, ptr %i", ir);
        // 步长 -1：一元取负生成 sub i32 0, 1
        Assert.Contains("sub i32 0", ir);
        // 递增：add 将步长加到当前值
        Assert.Contains("add i32", ir);
    }

    [Fact]
    public void WhileLoop_ShouldGenerateCondBodyBlocks()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR i : DINT; END_VAR
                i := 0;
                WHILE i < 10 DO
                    i := i + 1;
                END_WHILE;
            END_PROGRAM
            """
        );

        // 三个基本块
        Assert.Contains("while.cond1:", ir);
        Assert.Contains("while.body1:", ir);
        Assert.Contains("while.end1:", ir);
        // 条件比较 i < 10
        Assert.Contains("icmp slt i32", ir);
        // 条件分支
        Assert.Contains("br i1", ir);
        // 循环体有加法
        Assert.Contains("add i32", ir);
        // 条件块跳回自身
        Assert.Contains("br label %while.cond1", ir);
    }

    [Fact]
    public void RepeatLoop_ShouldGenerateBodyCondBlocks()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR i : DINT; END_VAR
                i := 0;
                REPEAT
                    i := i + 1;
                UNTIL i >= 10
                END_REPEAT;
            END_PROGRAM
            """
        );

        // 两个基本块
        Assert.Contains("repeat.body1:", ir);
        Assert.Contains("repeat.end1:", ir);
        // UNTIL 条件 i >= 10
        Assert.Contains("icmp sge i32", ir);
        // 条件分支
        Assert.Contains("br i1", ir);
        // 循环体有加法
        Assert.Contains("add i32", ir);
    }

    [Fact]
    public void CaseStatement_ShouldGenerateSwitch()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR x : DINT; y : DINT; END_VAR
                x := 2;
                CASE x OF
                    1: y := 10;
                    2: y := 20;
                    3: y := 30;
                ELSE
                    y := 0;
                END_CASE;
            END_PROGRAM
            """
        );

        // switch 指令
        Assert.Contains("switch i32", ir);
        // 每个 case 值
        Assert.Contains("i32 1, label", ir);
        Assert.Contains("i32 2, label", ir);
        Assert.Contains("i32 3, label", ir);
        // ELSE 的 default 块
        Assert.Contains("case.default1:", ir);
        // 合并块
        Assert.Contains("case.end1:", ir);
        // 各分支赋值
        Assert.Contains("store i32 10, ptr %y", ir);
        Assert.Contains("store i32 20, ptr %y", ir);
        Assert.Contains("store i32 30, ptr %y", ir);
        Assert.Contains("store i32 0, ptr %y", ir);
    }

    [Fact]
    public void CaseWithRange_ShouldEmitRangeChecks_NotPerValueExpansion()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR x : DINT; y : DINT; END_VAR
                x := 5;
                CASE x OF
                    1..3: y := 1;
                    4..6: y := 2;
                ELSE
                    y := 0;
                END_CASE;
            END_PROGRAM
            """
        );

        // F12：区间标签改为范围判定 `lo <= x <= hi`（sge + sle + and），不再逐值展开。
        Assert.Contains("icmp sge i32", ir);
        Assert.Contains("icmp sle i32", ir);
        // 每个含区间的分支一个条件块
        Assert.Contains("case.range1:", ir);
        Assert.Contains("case.range2:", ir);
        // 不再有展开后的逐值 case（旧行为：i32 1..6, label x6）
        Assert.DoesNotContain("i32 1, label", ir);
        Assert.DoesNotContain("i32 4, label", ir);
        // 分支体仍可达
        Assert.Contains("store i32 1, ptr %y", ir);
        Assert.Contains("store i32 2, ptr %y", ir);
        Assert.Contains("store i32 0, ptr %y", ir);
    }

    [Fact]
    public void ExitInLoop_ShouldJumpToEnd()
    {
        var ir = CodeGenTestHelper.EmitIR(
            """
            PROGRAM P
            VAR i : DINT; END_VAR
                FOR i := 1 TO 10 BY 1 DO
                    IF i = 5 THEN
                        EXIT;
                    END_IF;
                END_FOR;
            END_PROGRAM
            """
        );

        // EXIT 特有：不可达后继块
        Assert.Contains("after.exit1:", ir);
        // EXIT 特有：无条件跳转到 for.end（for.cond 是条件跳转）
        Assert.Contains("br label %for.end1", ir);
        // FOR 循环完整结构
        Assert.Contains("for.cond1:", ir);
        Assert.Contains("for.body1:", ir);
        Assert.Contains("for.inc1:", ir);
        Assert.Contains("for.end1:", ir);
        // EXIT 在 IF 内部
        Assert.Contains("if.then1:", ir);
        // IF 条件 i = 5
        Assert.Contains("icmp eq i32", ir);
    }
}
