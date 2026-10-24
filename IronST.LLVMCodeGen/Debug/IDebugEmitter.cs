using IronST.IRBuilder.IR;

namespace IronST.LLVMCodeGen.Debug;

/// <summary>
/// 调试信息发射接口。
/// </summary>
public interface IDebugEmitter
{
    /// <summary>是否启用调试信息生成。</summary>
    bool IsEnabled { get; }

    /// <summary>初始化编译单元级别的调试元数据。</summary>
    void Initialize(Module module, string fileName, string directory);

    /// <summary>进入函数，记录函数级调试信息。</summary>
    void EnterFunction(Function func, string name, string sourceFile, int startLine);

    /// <summary>设置当前源码行位置。</summary>
    void SetLocation(int line);

    /// <summary>清除当前源码行位置（序言阶段等不产生有意义行号时使用）。</summary>
    void ClearLocation();

    /// <summary>将当前源码位置附加到指令上。</summary>
    void AttachLocation(Instruction inst);

    /// <summary>
    /// 完成调试信息生成。
    /// 命名说明：不用 <c>Finalize</c>——那是 <see cref="object.Finalize"/> 的析构槽位，
    /// 声明同名方法会触发 CS0465，并让调用方误以为与 GC 生命周期有关。
    /// </summary>
    void Complete();
}