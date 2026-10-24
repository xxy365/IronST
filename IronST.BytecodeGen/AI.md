# IronST BytecodeGen 接口摘要

IRBuilder 对象模型到 IronST 字节码的单遍编译器。消费 `IronST.IRBuilder.IR` 的 Function/BasicBlock/Instruction 模型，输出 `BytecodeModule`（运行时可执行）+ `DebugModule`（调试元数据）+ `SourceMap`（源码位置映射）。四个文件：ByteCodeGenerator（入口）、FunctionEmitter（IR 指令分派与字节码发射）、DebugModule/DebugInstr（调试数据模型）、IRSourceMapParser（LLVM IR 调试元数据解析）。

---

## 命名空间

- `IronST.BytecodeGen`

---

## ByteCodeGenerator（编译器入口）

```csharp
public sealed class ByteCodeGenerator
```

构造参数：`string? sourceFileName`、`IReadOnlyDictionary<string, byte>? hostFunctions`（宿主函数名→HCALL 索引映射）。

### Generate

```csharp
public CompileResult Generate(
    Module module,
    string? entryFunctionName = null,
    Dictionary<string, List<(string file, int line)?>>? irSourceMap = null)
```

流程：
1. 遍历 `module.GlobalVariables`，通过 `asm.DeclareGlobal` 分配全局槽位，构建 `globalIndex` 字典
2. 过滤 stub 函数（`IsStub`：仅含 alloca/store/br/ret/unreachable 的函数体）
3. 定位入口函数：显式指定 > `"main"/"Main"` > 首个非声明函数
4. 发射入口 stub：`call entry` + `exit`
5. 入口函数排最前，其余函数随后，各自创建 `FunctionEmitter` 并 `Emit()`
6. `asm.Build()` 输出 `BytecodeModule`

### CompileResult

```csharp
public sealed class CompileResult
{
    public BytecodeModule Module { get; }
    public DebugModule DebugModule { get; }
    public IReadOnlyDictionary<int, (string file, int line)> SourceMap { get; }
}
```

### 便捷方法

| 方法 | 说明 |
|------|------|
| `GenerateModule(module)` | 仅返回 BytecodeModule |
| `EmitDebug(module)` | 仅返回 DebugModule |

---

## DebugModule / DebugFunction / DebugInstr（调试数据模型）

### DebugModule

```csharp
public sealed class DebugModule
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `GlobalNames` | `List<string>` | 全局变量名列表 |
| `Functions` | `List<DebugFunction>` | 函数调试信息列表 |

| 方法 | 说明 |
|------|------|
| `Dump()` | 导出可读文本（含标签、源码行号注释） |
| `BuildSourceMap()` | 返回 `SourceMap`（PC→源码位置映射） |

### DebugFunction

```csharp
public sealed class DebugFunction
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | `string` | 函数名 |
| `ParamCount` | `int` | 参数数量 |
| `TotalLocals` | `int` | 总局部变量槽位数 |
| `Instructions` | `List<DebugInstr>` | 指令列表 |
| `Labels` | `Dictionary<string, int>` | 标签名→指令索引 |
| `SourceFile` | `string?` | 所在源文件 |
| `SourceStartLine` / `SourceEndLine` | `int` | 源码行范围 |

### DebugInstr

```csharp
public struct DebugInstr
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `Opcode` | `OpCode` | 操作码 |
| `Dst` / `Src1` / `Src2` | `byte` | 寄存器操作数 |
| `Off8` | `sbyte` | 8 位有符号偏移/立即数 |
| `Imm16` | `short` | 16 位立即数/slot |
| `Imm32` | `int` | 32 位立即数/地址 |
| `Float` | `double` | 浮点常量（LDC 用） |
| `Str` | `string?` | 字符串常量（LDS 用） |
| `Label` | `string?` | 跳转标签（符号跳转用） |
| `CallTarget` | `string?` | 调用目标函数名 |
| `SrcFile` | `string?` | 源码文件名 |
| `SrcLine` | `int` | 源码行号 |

#### 工厂方法

| 方法 | 说明 |
|------|------|
| `Op(op)` | 1 字节指令（NOP/HALT/BRK/LEAVE/RET 等） |
| `R1(op, rd)` | 2 字节（LNIL/LTRUE/LFALSE/PUSH/POP/IINC/IDEC 等） |
| `R2(op, rd, rs)` | 3 字节双寄存器（MOV/SWAP/NOT/INEG/FNEG/TOINT/TOFLT 等） |
| `R3(op, rd, rs1, rs2)` | 4 字节三寄存器（所有算术/比较/位运算） |
| `I8(op, rd, val)` | LDI8 |
| `Slot(op, rd, slot)` | 含 slot 的指令（GETL/SETL/GETG/SETG/FRAME/PUSHN/POPN） |
| `Frame(n)` | FRAME |
| `I32(op, rd, val)` | LDI32 |
| `Ldc(rd, val)` | LDC（浮点常量池） |
| `Lds(rd, val)` | LDS（字符串常量池） |
| `Jmp(label)` | JMP16 符号跳转 |
| `Bz(cond, label)` / `Bnz(cond, label)` | 条件符号跳转 |
| `Branch(op, rs1, rs2, label)` | 双操作数条件分支（BEQ/BNE/BLT/BGE/BGT/BLE） |
| `Call(funcName)` | CALL 符号调用 |
| `Hcall(index)` | HCALL 宿主调用 |
| `Snew(rd, count)` / `Sget(rd, obj, field)` / `Sset(src, obj, field)` | 结构体操作 |
| `Pushn(n)` / `Popn(n)` | 栈调整 |

#### Format()

返回人类可读汇编字符串。所有已定义 OpCode 均有对应格式化分支，覆盖完整的强类型指令集（含 SCONCAT/FMOD/FPOW/FNE/FGT/FLTE/FGTE）。符号跳转指令优先输出标签名，否则输出数值偏移。

### SourceMap（BytecodeGen 内部版）

```csharp
public sealed class SourceMap
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `InstructionMap` | `Dictionary<int, SourceLoc>` | PC→源码位置（仅含源码信息的指令） |
| `FunctionRanges` | `List<FunctionRange>` | 函数级源码范围 |

| 方法 | 说明 |
|------|------|
| `FindByPc(int pc)` | 查找 PC 对应的最近源码位置（遍历最大 key <= pc） |
| `ToJson()` | 导出为 JSON 字符串（可保存 .debug.json） |

**注意：此 SourceMap 与 `IronST.Runtime.Dev.SourceMap` 是不同类。** BytecodeGen 版用 `int` 做 PC 键，`SourceLoc` 为 `(string File, int Line)`；Dev 版用 `uint` 做 PC 键，`SourceMapping` 为 record struct 含 FileIndex/Line/Column，支持二分查找。

---

## FunctionEmitter（IR→字节码发射器）

```csharp
internal sealed class FunctionEmitter
```

消费一个 `Function`（IRBuilder IR），通过 `BytecodeAssembler` 发射字节码，同时同步写入 `DebugFunction`。

### 寄存器分配

- R[0]–R[7] 固定用途（ZERO/SP/FP/RA/A0–A3）
- R[8]+ 线性递增分配，无回收
- 常量（ConstantInt/ConstantFP/ConstantPointerNull/ConstantAggregateZero）按需分配临时寄存器
- `EnsureReg(Value)` 核心方法：查缓存→常量 materialize→参数/alloca slot 加载→新分配

### 局部变量槽位

分配顺序：参数 slots (0..paramCount-1) → PHI slots → alloca slots → LR 保存 slot（仅函数内有 call 时分配）。超过 4 个参数通过 PUSH/POP 传递。

### IR 指令分派

| IR 指令 | 字节码映射 |
|---------|-----------|
| `BinaryOp.Add/Sub/Mul/SDiv/UDiv` | IADD/ISUB/IMUL/IDIV/IDIV |
| `BinaryOp.SRem` | `HCALL MATH_IMOD`（有符号余数） |
| `BinaryOp.URem` | `HCALL MATH_UMOD`（无符号余数） |
| `BinaryOp.FAdd/FSub/FMul/FDiv` | FADD/FSUB/FMUL/FDIV |
| `BinaryOp.And/Or/Xor` | AND/OR/XOR |
| `BinaryOp.Shl/LShr/AShr` | SHL/SHR/SAR |
| `ICmpPredicate.Eq/Ne/Slt/Sle/Sgt/Sge/Ult/Ule/Ugt/Uge` | IEQ/INE/ILT/ILTE/IGT/IGTE（有符号/无符号映射到同一条指令） |
| `FCmpPredicate.Oeq/One/Olt/Ole/Ogt/Oge` | FEQ/FNE/FLT/FLTE/FGT/FGTE |
| `CastOp.SIToFP/UIToFP` | TOFLT |
| `CastOp.FPToSI/FPToUI` | TOINT |
| 其他 Cast（zext/sext/trunc/fpext/fptrunc/bitcast 等） | MOV 或 NOP |
| `AllocaInst` | 加载 slot 值到寄存器；结构体 alloca 额外 SNEW+SETL |
| `LoadInst` | GETG（全局）/ GETL（标量 alloca）/ SGET（结构体字段 GEP）/ GETR（通用指针） |
| `StoreInst` | SETG / SETL / SSET / SETR；store 到结构体 alloca 直接忽略 |
| `GetElementPtrInst` | 结构体字段 GEP 记录到 `_structFieldPtrs`；数组/通用 GEP 用 IADD 累加 |
| `BranchInst` | 无条件：JMP16；条件：BZ（跳 false 分支）+ JMP16（跳 true 分支） |
| `ReturnInst` | MOV A0 + JMP epilogue |
| `SwitchInst` | 逐 case BEQ + JMP default |
| `CallInst` | >4 参数逆序 PUSH → 前 4 个 MOV A0-A3 → CALL/HCALL → 返回值 MOV from A0 |
| `PHINode` | GETL（phi slot 写入由 `EmitPhiMovesFor` 在分支前完成 SETL） |
| `SelectInst` | MOV default + BNZ + MOV true（分支实现） |
| `ExtractValueInst` | SGET |
| `InsertValueInst` | MOV dst + SSET |

### 结构体支持

- `_structAllocas`：标记结构体类型 alloca
- `_structFieldPtrs`：GEP 指令 → (basePtr, fieldIndex) 映射
- EmitLoad/EmitStore 优先走结构体字段路径（SGET/SSET）

### PHI 消除

`EmitPhiMovesFor(from, to)`：在分支指令发射前，遍历目标块头部的 PHINode，将来自当前块的 incoming value SETL 到 phi slot。无并行拷贝（依赖 IRBuilder 保证 phi 冲突安全）。

### 函数调用约定

- 前 4 个参数：A0–A3
- 第 5+ 个参数：逆序 PUSH，被调方入口 POP 回 `EmitExtraParamUnpack`
- 返回值：A0
- LR 保存：调用前 SETL 到 `_lrSlot`，调用后 GETL 恢复（仅函数体内有 call 时生效）
- 宿主调用：`_hostFunctions[calleeName]` → HCALL（8-bit ID）

### 源码位置

- 每条 IR 指令通过 `ReadSourceLoc()` 从 `_irSourceMap` 读取（由 `IRSourceMapParser.Parse()` 预计算）
- `EmitInstr(di, src)` 同时写入 `_sourceMap[asm.PC]` 和 `di.SrcFile/SrcLine`
- 函数发射完毕后 `DeriveSourceRange()` 从指令中推导 `SourceStartLine/SourceEndLine`

---

## IRSourceMapParser（LLVM IR 调试元数据解析）

```csharp
public static class IRSourceMapParser
```

| 方法 | 说明 |
|------|------|
| `Parse(string irText, string sourceFile)` | 解析 LLVM IR 文本，返回 `Dictionary<string, List<(string file, int line)?>>` |

解析流程：
1. 正则提取 `!N = !DILocation(line: X, ...)` 定义，构建 `diLocations` 字典
2. 逐行扫描，识别函数定义（`define ... @FuncName(`）和函数体边界（`{` / `}`）
3. 对函数体内每条 IR 指令行提取 `!dbg !N` 引用，查 `diLocations` 获取行号
4. 返回函数名→指令位置列表（索引对应函数内第 N 条指令，null 表示无源码信息）

---

## EmitToAssembler 桥接

`EmitToAssembler(DebugInstr di)` 方法是 DebugInstr→BytecodeAssembler 的桥梁。对每条 DebugInstr 按 OpCode switch 分派到底层 `_asm.Raw.XXX()` 或 `_asm.XXX()` 方法。完整覆盖所有 104 条指令。

特殊情况：
- `LDC` / `LDS`：通过 `_asm.AddConst(di.Float)` / `_asm.AddString(di.Str!)` 自动管理常量池/字符串池索引
- `JMP16` / `BZ` / `BNZ`：走 `_asm.Jmp/Bz/Bnz(di.Label!)` 符号跳转路径（延迟 patch）
- `CALL`：有 `CallTarget` 时走 `_asm.CallFunc(di.CallTarget)`（标签解析），否则走 `_asm.Raw.CALL(uint)`

---

## 重要提醒（防幻觉）

1. **所有算术/比较指令均为强类型**：I 前缀仅操作 IntValue，F 前缀仅操作 FloatValue。IR 中的 `Add` 映射 IADD，`FAdd` 映射 FADD，不会生成已移除的泛型指令。
2. **ICmp 有符号/无符号映射到同一条指令**：`Slt`/`Ult` 均映射 `ILT`，因为 IronST ISA 不区分有符号/无符号整数比较。
3. **FCmp 仅处理有序比较（O*）**：`Oeq/One/Olt/Ole/Ogt/Oge` 有精确映射；U*（无序）、Ord/Uno/True/False 等降级为 FEQ 并输出 debug 警告。
4. **fneg 不会出现在 IR 中**：IRBuilder 上游已将 fneg 转为 `fsub 0.0, x`，FunctionEmitter 不处理 FNegInst。
5. **分支指令 BEQ/BNE/BLT/BGE/BGT/BLE 用于 switch 和条件分支**：switch 的 case 比较用 BEQ（走 `BcValue.Eq`，支持跨类型提升）；条件分支（br i1）用 BZ/BNZ（走 IsTruthy/IsFalsy）。
6. **两个 SourceMap 类不同**：`IronST.BytecodeGen.SourceMap`（int PC 键，含 ToJson/FindByPc）和 `IronST.Runtime.Dev.SourceMap`（uint PC 键，含二分查找 FindByPC）是独立实现。
7. **ByteCodeGenerator 不直接发射字节码**：全部委托给 FunctionEmitter，FunctionEmitter 通过 `EmitInstr` 同步写 BytecodeAssembler 和 DebugFunction。
8. **超过 4 个参数的调用约定**：调用方逆序 PUSH 第 5+ 参数，被调方入口 FRAME 后 POP 到 SETL 槽位。
9. **IRSourceMapParser 仅解析 `!DILocation` 的行号**：忽略列信息，`column` 字段被丢弃。
10. **stub 函数不发射字节码**：`IsStub` 检测仅含 alloca/store/br/ret/unreachable 的函数体，跳过。
11. **DebugInstr.Format() 不会输出已移除的泛型指令格式**：无 ADD/SUB/MUL/DIV/MOD/POW/NEG/EQ/NEQ/LT/GT/LTE/GTE 的格式化分支。
12. **BytecodeAssembler 的便捷方法（IAdd/FAdd/Concat 等）由 Dev 层提供**：BytecodeGen 的 FunctionEmitter 不使用这些便捷方法，直接通过 `DebugInstr.R3` + `EmitToAssembler` 桥接到 `_asm.Raw.XXX`。
13. **`_sourceMap` 的 key 是 `asm.PC`（字节码偏移）**：在 `EmitInstr` 时记录，此时 PC 尚未发射当前指令（记录的是指令起始地址）。
14. **结构体 alloca 的 store 被忽略**：结构体通过 SNEW 初始化为全 Void，字段通过 SSET 写入，标量 store 到结构体 alloca 指针会被静默跳过。