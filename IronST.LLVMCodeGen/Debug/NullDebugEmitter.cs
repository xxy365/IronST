using IronST.IRBuilder.IR;

namespace IronST.LLVMCodeGen.Debug;

/// <summary>
/// 空实现，不生成任何调试信息。当用户未指定调试选项时使用。
/// </summary>
public sealed class NullDebugEmitter : IDebugEmitter
{
    public bool IsEnabled => false;

    public void Initialize(Module module, string fileName, string directory) { }
    public void EnterFunction(Function func, string name, string sourceFile, int startLine) { }
    public void SetLocation(int line) { }
    public void ClearLocation() { }
    public void AttachLocation(Instruction inst) { }
    public void Complete() { }
}