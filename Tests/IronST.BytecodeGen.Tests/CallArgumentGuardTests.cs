// CallArgumentGuardTests.cs — CALL-NRE：调用实参为空时的后端防护
//
// 背景：ExpressionEmitter.ResolveCallArguments 的命名绑定路径可能留下未解析的
// null 槽位，FunctionEmitter.EmitCall 直接对其取 .Type ⇒ 编译期 NullReferenceException
// （编译器崩溃而非诊断），见 docs/阶段0B设计.md §6.3.9 / 探针 G。
//
// 前端已在 CALL-ARITY 切片中拒绝此类调用；本用例不依赖前端，直接在 IR 层构造一个
// 含 null 实参的 call，验证后端给出明确的 InvalidOperationException（而非 NRE）。
// BytecodeGen 侧的契约错误一律用 InvalidOperationException。

using IronST.BytecodeGen;
using IronST.IRBuilder.IR;

namespace IronST.BytecodeGen.Tests;

public class CallArgumentGuardTests
{
    /// <summary>
    /// 最小 IR：callee 有函数体（避免落入"外部声明"分支），main 以 null 实参调用它。
    /// </summary>
    private static Module ModuleWithNullArgumentCall()
    {
        var module = new Module("nullarg.st");

        var calleeType = new FunctionType(IrType.Int32, new[] { IrType.Int32 });
        var callee = new Function("callee", calleeType);
        module.AddFunction(callee);
        var calleeEntry = new BasicBlock { Name = "entry" };
        callee.AppendBasicBlock(calleeEntry);
        calleeEntry.AddInstruction(new ReturnInst(new ConstantInt(IrType.Int32, 0)));

        var mainType = new FunctionType(IrType.Int32, Array.Empty<IrType>());
        var main = new Function("main", mainType);
        module.AddFunction(main);
        var entry = new BasicBlock { Name = "entry" };
        main.AppendBasicBlock(entry);

        // 故意传 null 实参（模拟命名绑定未覆盖的槽位）。
        var call = new CallInst(calleeType, callee, new Value[] { null! }, "r");
        entry.AddInstruction(call);
        entry.AddInstruction(new ReturnInst(call));

        return module;
    }

    [Fact]
    public void Generate_NullArgument_ThrowsInvalidOperationException()
    {
        var module = ModuleWithNullArgumentCall();

        var ex = Assert.Throws<InvalidOperationException>(
            () => new ByteCodeGenerator("nullarg.st").Generate(module)
        );

        // 消息须指出被调方，便于定位
        Assert.Contains("callee", ex.Message);
    }
}
