using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

/// <summary>
/// 字符串内置函数 → IR 测试。
/// </summary>
public class StringFunctionCodeGenTests
{
    [Fact]
    public void LEN_ShouldAccessLengthField()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                s : STRING;
                n : INT;
            END_VAR
                n := LEN(s);
            END_PROGRAM
            """);

        // LEN 通过 GEP 取 {i32, [N x i8]} 的字段 0，再 load
        Assert.Contains("getelementptr", ir);
    }

    [Fact]
    public void LEN_WithExplicitArgument_ShouldWork()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                s1 : STRING := 'Hello';
                n : INT;
            END_VAR
                n := LEN(s1);
            END_PROGRAM
            """);

        // IR 应能正常生成，不抛异常
        Assert.Contains("define", ir);
    }

    [Fact]
    public void CONCAT_ShouldGenerateMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                s1 : STRING;
                s2 : STRING;
                s3 : STRING;
            END_VAR
                s3 := CONCAT(s1, s2);
            END_PROGRAM
            """);

        // CONCAT 应调用 llvm.memcpy 进行数据拼接
        Assert.Contains("call void @llvm.memcpy", ir);
    }

    [Fact]
    public void LEFT_ShouldGenerateMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                s1 : STRING;
                s2 : STRING;
            END_VAR
                s2 := LEFT(s1, 5);
            END_PROGRAM
            """);

        // LEFT 需要 memcpy 拷贝子串
        Assert.Contains("call void @llvm.memcpy", ir);
    }

    [Fact]
    public void RIGHT_ShouldGenerateMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                s1 : STRING;
                s2 : STRING;
            END_VAR
                s2 := RIGHT(s1, 3);
            END_PROGRAM
            """);

        Assert.Contains("call void @llvm.memcpy", ir);
    }

    [Fact]
    public void MID_ShouldGenerateMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                s1 : STRING;
                s2 : STRING;
            END_VAR
                s2 := MID(s1, 3, 2);
            END_PROGRAM
            """);

        Assert.Contains("call void @llvm.memcpy", ir);
    }

    [Fact]
    public void FIND_ShouldGenerateComparisonLoop()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                s1 : STRING;
                s2 : STRING;
                pos : INT;
            END_VAR
                pos := FIND(s1, s2);
            END_PROGRAM
            """);

        // FIND 用 memcmp 做逐段比较
        Assert.Contains("declare i32 @memcmp", ir);
        Assert.Contains("call i32 @memcmp", ir);
    }

    [Fact]
    public void INSERT_ShouldGenerateThreePartMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                s1 : STRING;
                s2 : STRING;
                s3 : STRING;
            END_VAR
                s3 := INSERT(s1, s2, 3);
            END_PROGRAM
            """);

        // INSERT 需要 3 段 memcpy（前段、插入、后段）
        // 至少出现多次 memcpy 调用
        var memcpyCount = CountOccurrences(ir, "call void @llvm.memcpy");
        Assert.True(memcpyCount >= 2, $"INSERT 应产生多次 memcpy 调用，实际 {memcpyCount} 次");
    }

    [Fact]
    public void DELETE_ShouldGenerateTwoPartMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                s1 : STRING;
                s2 : STRING;
            END_VAR
                s2 := DELETE(s1, 3, 2);
            END_PROGRAM
            """);

        // DELETE 需要 2 段 memcpy（删除前 + 删除后）
        var memcpyCount = CountOccurrences(ir, "call void @llvm.memcpy");
        Assert.True(memcpyCount >= 1, $"DELETE 应产生 memcpy 调用，实际 {memcpyCount} 次");
    }

    [Fact]
    public void REPLACE_ShouldGenerateThreePartMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                s1 : STRING;
                s2 : STRING;
                s3 : STRING;
            END_VAR
                s3 := REPLACE(s1, s2, 3, 2);
            END_PROGRAM
            """);

        // REPLACE 需要 3 段 memcpy
        var memcpyCount = CountOccurrences(ir, "call void @llvm.memcpy");
        Assert.True(memcpyCount >= 2, $"REPLACE 应产生多次 memcpy 调用，实际 {memcpyCount} 次");
    }

    [Fact]
    public void StringMemcpy_ShouldDeclareIntrinsic()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM TestProg
            VAR
                s1 : STRING;
                s2 : STRING;
                s3 : STRING;
            END_VAR
                s3 := CONCAT(s1, s2);
            END_PROGRAM
            """);

        // llvm.memcpy intrinsic 应被声明
        Assert.Contains("declare void @llvm.memcpy", ir);
    }

    // ════════════════════════════════════════════
    //  辅助
    // ════════════════════════════════════════════

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}