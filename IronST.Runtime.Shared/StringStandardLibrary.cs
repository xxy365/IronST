// StringStandardLibrary.cs — IEC 61131-3 字符串标准函数的宿主侧实现（C# 注入）。
//
// 定位：这些函数在语言层由 IronST.Frontend.IECStandardLibrary 声明（名称/签名不变），
// 但**实现**从"编译器内联展开的 LLVM IR"改为"宿主函数"：
//   前端声明 → IR 声明 → 字节码 HCALL → 本类注册到 HostBridge 的 C# 处理函数。
//
// 约定（与既有 Print 宿主调用同路，见 StringEndToEndTests / F11）：
//   · 实参按位置传入 A0、A1、A2、A3（STRING 参数是单槽 BcValue(String)）；
//   · 返回值写入 R[4]（= A0），由调用方取回。
//
// 语义与 LLVMCodeGen/CodeGen/StringFunctions.cs 对齐（1-based 位置、按长度截断），
// 但对越界输入给出**确定**结果（截断/空串）而非 C 层的未定义行为。
namespace IronST.Runtime.Shared;

/// <summary>
/// 字符串标准库的宿主实现与 ID 映射。
/// </summary>
public static class StringStandardLibrary
{
    /// <summary>
    /// 函数名 → 宿主调用 ID。把它并入 <c>hostFunctions</c> 传给
    /// <c>ByteCodeGenerator</c>，并用 <see cref="Register"/> 注册处理函数即可启用。
    /// </summary>
    public static IReadOnlyDictionary<string, ushort> Functions { get; } =
        new Dictionary<string, ushort>(StringComparer.Ordinal)
        {
            ["LEN"] = HostCallIds.STR_LEN,
            ["LEFT"] = HostCallIds.STR_LEFT,
            ["RIGHT"] = HostCallIds.STR_RIGHT,
            ["MID"] = HostCallIds.STR_SUBSTR,
            ["CONCAT"] = HostCallIds.STR_CONCAT,
            ["INSERT"] = HostCallIds.STR_INSERT,
            ["DELETE"] = HostCallIds.STR_DELETE,
            ["REPLACE"] = HostCallIds.STR_REPLACE,
            ["FIND"] = HostCallIds.STR_FIND,
        };

    /// <summary>把这 9 个字符串函数注册到宿主桥。</summary>
    public static void Register(HostBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);

        bridge.Register(HostCallIds.STR_LEN, ctx => new BcValue((long)Str(ctx, 0).Length));
        bridge.Register(HostCallIds.STR_CONCAT, ctx => new BcValue(Str(ctx, 0) + Str(ctx, 1)));
        bridge.Register(HostCallIds.STR_LEFT, ctx => new BcValue(Left(Str(ctx, 0), Int(ctx, 1))));
        bridge.Register(HostCallIds.STR_RIGHT, ctx => new BcValue(Right(Str(ctx, 0), Int(ctx, 1))));
        bridge.Register(HostCallIds.STR_SUBSTR, ctx => new BcValue(Mid(Str(ctx, 0), Int(ctx, 1), Int(ctx, 2))));
        bridge.Register(HostCallIds.STR_FIND, ctx => new BcValue(Find(Str(ctx, 0), Str(ctx, 1))));
        bridge.Register(HostCallIds.STR_INSERT, ctx => new BcValue(Insert(Str(ctx, 0), Str(ctx, 1), Int(ctx, 2))));
        bridge.Register(HostCallIds.STR_DELETE, ctx => new BcValue(Delete(Str(ctx, 0), Int(ctx, 1), Int(ctx, 2))));
        bridge.Register(HostCallIds.STR_REPLACE, ctx => new BcValue(Replace(Str(ctx, 0), Str(ctx, 1), Int(ctx, 2), Int(ctx, 3))));
    }

    // ── 实参读取：按位置 A0..A3（STRING = BcValue(String)，INT = BcValue(Int64)）──

    private static string Str(VMContext ctx, int argIndex)
        => ctx.ReadReg(BytecodeBuilder.A0 + argIndex).StringValue ?? "";

    private static int Int(VMContext ctx, int argIndex)
        => (int)ctx.ReadReg(BytecodeBuilder.A0 + argIndex).IntValue;

    // ── 语义：与 StringFunctions.cs 对齐（1-based 位置、按长度截断）；
    //    越界输入钳到合法区间（不抛异常），而非 C 层未定义行为。──

    private static string Left(string s, int n)
    {
        if (n <= 0 || s.Length == 0)
            return "";
        return n >= s.Length ? s : s[..n];
    }

    private static string Right(string s, int n)
    {
        if (n <= 0 || s.Length == 0)
            return "";
        return n >= s.Length ? s : s[(s.Length - n)..];
    }

    /// <param name="start">1-based 起点（IEC MID 的 P）。</param>
    private static string Mid(string s, int n, int start)
    {
        int offset = ClampStart(start, s.Length);
        if (offset >= s.Length)
            return "";
        int take = n <= 0 ? 0 : Math.Min(n, s.Length - offset);
        return s.Substring(offset, take);
    }

    private static string Insert(string in1, string in2, int position)
    {
        int offset = ClampStart(position, in1.Length);
        return string.Concat(in1.AsSpan(0, offset), in2, in1.AsSpan(offset));
    }

    private static string Delete(string s, int n, int start)
    {
        int offset = ClampStart(start, s.Length);
        int remove = n <= 0 ? 0 : Math.Min(n, s.Length - offset);
        return string.Concat(s.AsSpan(0, offset), s.AsSpan(offset + remove));
    }

    private static string Replace(string in1, string in2, int n, int start)
    {
        int offset = ClampStart(start, in1.Length);
        int remove = n <= 0 ? 0 : Math.Min(n, in1.Length - offset);
        return string.Concat(in1.AsSpan(0, offset), in2, in1.AsSpan(offset + remove));
    }

    /// <summary>1-based 位置，未找到返回 0（与 LLVM 实现一致）。</summary>
    private static long Find(string haystack, string needle)
    {
        int idx = haystack.IndexOf(needle, StringComparison.Ordinal);
        return idx < 0 ? 0L : idx + 1L;
    }

    /// <summary>1-based 起点 → 0-based 偏移，钳到 [0, len]。</summary>
    private static int ClampStart(int start, int len)
    {
        int offset = start - 1;
        if (offset < 0)
            return 0;
        return offset > len ? len : offset;
    }
}
