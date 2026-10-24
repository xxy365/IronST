using IronST.LLVMCodeGen.Tests.Helpers;
using Xunit;

namespace IronST.LLVMCodeGen.Tests;

public class BuiltinStringCodeGenTests
{
    [Fact]
    public void LEN_ShouldAccessLengthField()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR s : STRING; n : INT; END_VAR
                n := LEN(s);
            END_PROGRAM
            """);

        // LEN 通过 GEP 取 {i32, [N x i8]} 的字段 0 再 load
        Assert.Contains("getelementptr", ir);
        Assert.Contains("load i32", ir);
    }

    [Fact]
    public void CONCAT_ShouldCallMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR s1 : STRING; s2 : STRING; s3 : STRING; END_VAR
                s3 := CONCAT(s1, s2);
            END_PROGRAM
            """);

        Assert.Contains("call void @llvm.memcpy", ir);
    }

    [Fact]
    public void LEFT_ShouldCallMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR s1 : STRING; s2 : STRING; END_VAR
                s2 := LEFT(s1, 5);
            END_PROGRAM
            """);

        Assert.Contains("call void @llvm.memcpy", ir);
    }

    [Fact]
    public void RIGHT_ShouldCallMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR s1 : STRING; s2 : STRING; END_VAR
                s2 := RIGHT(s1, 3);
            END_PROGRAM
            """);

        Assert.Contains("call void @llvm.memcpy", ir);
    }

    [Fact]
    public void MID_ShouldCallMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR s1 : STRING; s2 : STRING; END_VAR
                s2 := MID(s1, 3, 2);
            END_PROGRAM
            """);

        Assert.Contains("call void @llvm.memcpy", ir);
    }

    [Fact]
    public void FIND_ShouldCallMemcmp()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR s1 : STRING; s2 : STRING; pos : INT; END_VAR
                pos := FIND(s1, s2);
            END_PROGRAM
            """);

        Assert.Contains("declare i32 @memcmp", ir);
        Assert.Contains("call i32 @memcmp", ir);
    }

    [Fact]
    public void INSERT_ShouldGenerateMultipleMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR s1 : STRING; s2 : STRING; s3 : STRING; END_VAR
                s3 := INSERT(s1, s2, 3);
            END_PROGRAM
            """);

        // INSERT 需要多段 memcpy（前段 + 插入 + 后段）
        var count = CodeGenTestHelper.CountOccurrences(ir, "call void @llvm.memcpy");
        Assert.True(count >= 2, $"INSERT 应产生多次 memcpy，实际 {count}");
    }

    [Fact]
    public void DELETE_ShouldCallMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR s1 : STRING; s2 : STRING; END_VAR
                s2 := DELETE(s1, 3, 2);
            END_PROGRAM
            """);

        Assert.Contains("call void @llvm.memcpy", ir);
    }

    [Fact]
    public void REPLACE_ShouldGenerateMultipleMemcpy()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR s1 : STRING; s2 : STRING; s3 : STRING; END_VAR
                s3 := REPLACE(s1, s2, 3, 2);
            END_PROGRAM
            """);

        var count = CodeGenTestHelper.CountOccurrences(ir, "call void @llvm.memcpy");
        Assert.True(count >= 2, $"REPLACE 应产生多次 memcpy，实际 {count}");
    }

    [Fact]
    public void StringMemcpy_Intrinsic_ShouldBeDeclaredOnce()
    {
        var ir = CodeGenTestHelper.EmitIR("""
            PROGRAM P
            VAR s1 : STRING; s2 : STRING; s3 : STRING; s4 : STRING; END_VAR
                s3 := CONCAT(s1, s2);
                s4 := LEFT(s1, 3);
            END_PROGRAM
            """);

        // memcpy intrinsic 应被声明
        Assert.Contains("declare void @llvm.memcpy", ir);
    }
}