// NullReferenceIntegrationTests.cs — A2：空引用解引用必须是运行期错误
//
// 背景：引用在字节码里是「帧槽号」，零初始化的引用其值不是数字 0，而是 Void。
// 若不判空，解引用会静默读到槽 0（另一个局部变量），既不报错也不可诊断。
//
// 契约：
//   · 未绑定（NULL）的 REFERENCE 被解引用 → RuntimeErrorException(NullReference)；
//   · 读取、写入、结构体字段三条路径都覆盖；
//   · 复制空引用（`q := r`）不报错 —— 报错的时机是「解引用」，不是「持有」；
//   · 非空引用的行为不变。
using IronST.Runtime;
using IronST.Runtime.Shared;

namespace IronST.Integration.Tests;

public class NullReferenceIntegrationTests
{
    [Fact]
    public void DereferenceUnboundReference_ThrowsNullReference()
    {
        var run = StRunner.RunExpectingError("""
            PROGRAM Main
            VAR
                r : REFERENCE TO INT;
                x : INT;
                a : ARRAY[0..1] OF INT;
            END_VAR
                x := r^;
                a[x] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.NotNull(run.Error);
        Assert.Equal(RuntimeErrorType.NullReference, run.Error!.ErrorType);
        Assert.Contains("空引用解引用", run.Error.Detail);
    }

    [Fact]
    public void DereferenceUnboundReferenceField_ThrowsNullReference()
    {
        var run = StRunner.RunExpectingError("""
            TYPE P : STRUCT a : INT; END_STRUCT; END_TYPE

            PROGRAM Main
            VAR
                r : REFERENCE TO P;
                x : INT;
            END_VAR
                x := r^.a;
                x := x + 1;
            END_PROGRAM
            """);

        Assert.NotNull(run.Error);
        Assert.Equal(RuntimeErrorType.NullReference, run.Error!.ErrorType);
    }

    [Fact]
    public void WriteThroughUnboundReference_ThrowsNullReference()
    {
        var run = StRunner.RunExpectingError("""
            PROGRAM Main
            VAR
                r : REFERENCE TO INT;
            END_VAR
                r^ := 5;
            END_PROGRAM
            """);

        Assert.NotNull(run.Error);
        Assert.Equal(RuntimeErrorType.NullReference, run.Error!.ErrorType);
    }

    [Fact]
    public void CopyingNullReference_IsAllowed()
    {
        // 「持有空引用」合法，只有解引用才报错；否则 `q := p` 这类复制会被误伤。
        var run = StRunner.RunExpectingError("""
            PROGRAM Main
            VAR
                r : REFERENCE TO INT;
                q : REFERENCE TO INT;
            END_VAR
                q := r;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
    }

    [Fact]
    public void BoundReference_StillWorks()
    {
        var run = StRunner.RunExpectingError("""
            PROGRAM Main
            VAR
                i : INT;
                r : REFERENCE TO INT;
                out : INT;
                a : ARRAY[0..1] OF INT;
            END_VAR
                i := 5;
                r REF= i;
                r^ := 70;
                out := r^;
                IF (i <> 70) OR (out <> 70) THEN
                    a[999] := 1;
                END_IF
                a[out - 70] := 1;
            END_PROGRAM
            """);

        Assert.Equal(VMState.Halted, run.State);
        Assert.Null(run.Error);
    }
}
