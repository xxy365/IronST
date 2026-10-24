using IronST.IRBuilder.IR;

namespace IronST.LLVMCodeGen.Debug;

/// <summary>
/// 真正记录调试位置的发射器。
///
/// 与 <see cref="NullDebugEmitter"/> 的区别：这里把每条 IR 指令对应的源码行
/// 收集起来，供上层整理成 ByteCodeGenerator 的 irSourceMap —— 断点就是靠它
/// 把「第 N 行」换算成 PC 的。不传这个发射器（默认 NullDebugEmitter）就
/// 什么都不记，SourceMap 为空，断点无处可挂。
///
/// 之所以不去生成 !dbg 元数据再走 IRSourceMapParser.Parse 的文本往返：
/// MetadataEmitter 还不会输出 DI 节点（default 分支只吐
/// &lt;unsupported: DILocation&gt;），而 FunctionEmitter.EmitBody 遍历
/// BasicBlocks→Instructions 的顺序与这里 BuildSourceMap 完全一致，
/// 直接按对象建映射更短也更可靠。
/// </summary>
public sealed class IRDebugEmitter : IDebugEmitter
{
    private readonly Dictionary<Instruction, int> _lines = new();

    /// <summary>
    /// IR 指令 → 该指令所属 POU 的源文件。
    ///
    /// 多文件编译时每个 POU 来自不同的文件，而 <see cref="BuildSourceMap"/> 的
    /// <c>sourceFile</c> 参数只有一个名字 —— 靠这份映射才能把 Utils.st 里的
    /// 指令标回 Utils.st（否则断点会挂到同名的行上、执行行高亮错文件）。
    /// </summary>
    private readonly Dictionary<Instruction, string> _files = new();

    private int _currentLine;

    /// <summary>当前正在发射的 POU 所在文件（由 <see cref="EnterFunction"/> 给出）。</summary>
    private string _currentFile = "";

    public bool IsEnabled => true;

    /// <summary>源文件名，由 Initialize 记录（多文件时作为缺省回退值）。</summary>
    public string SourceFile { get; private set; } = "";

    /// <summary>IR 指令 → 源码行号（1 起）。只收已通过 SetLocation 定位的。</summary>
    public IReadOnlyDictionary<Instruction, int> Lines => _lines;

    /// <summary>IR 指令 → 源文件。只收已通过 SetLocation 定位的。</summary>
    public IReadOnlyDictionary<Instruction, string> Files => _files;

    public void Initialize(Module module, string fileName, string directory)
        => SourceFile = fileName;

    public void EnterFunction(Function func, string name, string sourceFile, int startLine)
    {
        // 必须清零：否则上一个函数的最后一个行号会"漏"到本函数的序言指令上，
        // 让 prologue 被误判成可断点（FrameBuilder 会显式 ClearLocation，但
        // EnterFunction 到第一条语句之间的指令没人管）。
        _currentLine = 0;

        // 同理：不清的话上一个函数的文件名会漏到本函数（sourceFile 为空）的
        // 指令上，多文件时就指到了错的文件。
        _currentFile = string.IsNullOrEmpty(sourceFile) ? SourceFile : sourceFile;
    }

    public void SetLocation(int line) => _currentLine = line;

    public void ClearLocation() => _currentLine = 0;

    public void AttachLocation(Instruction inst)
    {
        if (_currentLine > 0)
        {
            _lines[inst] = _currentLine;
            _files[inst] = _currentFile;
        }
    }

    public void Complete() { }

    /// <summary>
    /// 整理成 ByteCodeGenerator.Generate 的 irSourceMap 参数。
    ///
    /// 遍历顺序必须与 FunctionEmitter.EmitBody 严格一致 —— 它按
    /// _instrIndex 在 BasicBlocks→Instructions 上递增取值，两边只要
    /// 顺序错位，所有行号就会整体串位。Key 用 Ordinal 以匹配
    /// IRSourceMapParser 的写法。
    /// </summary>
    /// <param name="module">刚生成完的 IR 模块。</param>
    /// <param name="sourceFile">
    /// 缺省文件名：只用于那些没记住所属文件的指令（<see cref="EnterFunction"/>
    /// 收到空文件名、或压根没进过函数）。多文件编译时逐条指令都用自己 POU 的文件名。
    /// </param>
    public Dictionary<string, List<(string file, int line)?>> BuildSourceMap(
        Module module, string sourceFile)
    {
        var map = new Dictionary<string, List<(string file, int line)?>>(
            StringComparer.Ordinal);

        foreach (var func in module.Functions)
        {
            var list = new List<(string file, int line)?>();

            foreach (var bb in func.BasicBlocks)
            {
                foreach (var inst in bb.Instructions)
                {
                    list.Add(_lines.TryGetValue(inst, out int line)
                        ? (FileOf(inst, sourceFile), line)
                        : null);
                }
            }

            map[func.Name ?? "__func"] = list;
        }

        return map;
    }

    /// <summary>该指令所属文件；没记住就回退到缺省名。</summary>
    private string FileOf(Instruction inst, string fallback) =>
        _files.TryGetValue(inst, out var file) && file.Length > 0 ? file : fallback;
}
