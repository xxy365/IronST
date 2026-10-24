// ═══════════════════════════════════════════════════════════════
//  RuntimeError.cs — 运行时异常
// ═══════════════════════════════════════════════════════════════
namespace IronST.Runtime.Shared;

/// <summary>
/// VM 运行期错误。<b>自带源位置</b>：VM 在 <see cref="VMBase.Run"/> 的 catch 里按
/// <see cref="PC"/> 回填一次 <see cref="SourceLocation"/>，之后 <see cref="Message"/> 就带上
/// 「（文件:行）」—— 所有打印 <c>ex.Message</c> 的地方自动获得位置，
/// 不需要每个消费点各自去查源码映射。
/// </summary>
/// <remarks>
/// 分层的取舍：<c>Runtime.Shared</c> 不认识前端，这里也不做任何映射查询 —— 它只
/// <b>持有</b>一个 (文件, 行) 结果。查表逻辑由宿主经 <see cref="VMOptions.SourceLocator"/>
/// 注入（<see cref="SourceResolver.FromMap"/> 是个现成的壳）。没有注入时
/// <see cref="SourceLocation"/> 恒为 <c>null</c>，<see cref="Message"/> 与历史格式逐字一致。
/// </remarks>
public class RuntimeErrorException : Exception
{
    public RuntimeErrorType ErrorType { get; }

    /// <summary>出错指令的字节偏移。为 0 表示抛出点不持有 PC（如 StructHeap/BcValue 的深栈路径）。</summary>
    public uint PC { get; private set; }

    /// <summary>原始消息，不含 <c>[类型] at 0xPC:</c> 前缀与源位置后缀。</summary>
    public string Detail { get; }

    /// <summary>
    /// 源位置 <c>(文件, 行号)</c>。VM 在错误边界回填；<c>null</c> = 未知（**不伪造**）。
    /// </summary>
    /// <remarks>
    /// 刻意不叫 <c>Source</c>：<see cref="Exception.Source"/> 是基类上一个 <c>string</c> 属性
    /// （调用方名），同名的元组属性会把它隐藏掉（CS0114）并造成误导。
    /// </remarks>
    public (string file, int line)? SourceLocation { get; internal set; }

    public RuntimeErrorException(RuntimeErrorType type, uint pc, string message)
        : base(message)
    {
        ErrorType = type;
        PC = pc;
        Detail = message;
    }

    /// <summary>
    /// 补齐 PC：<b>仅当</b> <see cref="PC"/> 还是 0（抛出点不知道自己的 PC）时写入。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 由 VM 在<b>唯一还持有当前指令 PC 的边界</b>调用一次 ——
    /// <c>VMBase.RunLoop</c> 的 catch（<c>pc</c> 是那里的局部变量，
    /// <see cref="VMBase"/> 的 <c>_pc</c> 字段在指令执行途中是<b>陈旧</b>的，
    /// 只会在暂停/停机时同步，所以不能拿它当"当前 PC"）。
    /// </para>
    /// <para>
    /// 已非零就<b>不动</b>：那说明抛出点自己知道，且它比边界更权威
    /// （例如宿主函数抛出的异常自带 PC）。
    /// </para>
    /// </remarks>
    internal void AdoptPC(uint pc)
    {
        if (PC == 0)
            PC = pc;
    }

    /// <summary>
    /// 展示文本：<c>[类型] at 0xPC: 详情</c>，有源位置时补 <c>（文件:行）</c>。
    /// </summary>
    /// <remarks>
    /// 覆盖 <see cref="Exception.Message"/>（virtual）是这套设计的落点：位置只在这里拼一次，
    /// CLI / IDE / 示例宿主 / 测试日志等所有消费者都只是打印 <c>ex.Message</c>。
    /// </remarks>
    public override string Message =>
        SourceLocation is { } s
            ? $"[{ErrorType}] at 0x{PC:X}: {Detail}  （{s.file}:{s.line}）"
            : $"[{ErrorType}] at 0x{PC:X}: {Detail}";
}
