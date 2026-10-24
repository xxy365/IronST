```markdown
# IronST Runtime Shared — 参考文档

## 1. 项目概述

IronST 是一个 IEC 61131-3 ST (Structured Text) 字节码虚拟机。`IronST.Runtime.Shared` 是运行时核心，包含：

- **BcValue** — 24 字节 tagged union 值类型（Void / Int64 / Float64 / String / Struct）
- **StructHeap** — 结构体 Arena 分配器
- **RegisterFile** — 256 个寄存器，R[0] 硬连线为 Void
- **VMBase** — 虚拟机基类（执行循环、栈、全局变量、常量池）
- **BytecodeModule** — .irst 字节码文件加载器
- **HostBridge** — 宿主方法注册表（HCALL）
- **VMContext** — 宿主与调试器共用的 VM 访问接口

所有类型位于命名空间 `IronST.Runtime.Shared`。

---

## 2. BcValue — 24 字节 Tagged Union

### 内存布局 (24 bytes, little-endian)

| Offset | Size | Field      | 说明                                             |
|--------|------|------------|--------------------------------------------------|
| 0      | 1    | Tag        | ValueTag 类型标签                                 |
| 1      | 1    | SubTag     | 子类型标签（Struct 时 = typeId，其他 = 0）         |
| 2–7    | 6    | padding    | 对齐填充                                         |
| 8–15   | 8    | IntValue   | Tag=Int64 时为数值，Tag=Struct 时为 slotIndex     |
| 8–15   | 8    | FloatValue | Tag=Float64 时为 IEEE 754 双精度（与 IntValue union）|
| 16–23  | 8    | RefValue   | Tag=String 时指向 System.String，其他 = null      |

### ValueTag 枚举

```csharp
public enum ValueTag : byte
{
    Void    = 0,  // 空值
    Int64   = 1,  // 64 位有符号整数
    Float64 = 2,  // IEEE 754 双精度浮点
    String  = 3,  // 托管字符串
    Struct  = 5,  // IEC 结构体/函数块实例
}
```

### 构造

```csharp
BcValue.Void                      // Tag=Void, 所有字段为默认值
new BcValue(42L)                  // Tag=Int64, IntValue=42
new BcValue(3.14)                 // Tag=Float64, FloatValue=3.14
new BcValue("hello")              // Tag=String, RefValue="hello"
BcValue.MakeStruct(typeId, slot)  // Tag=Struct, SubTag=typeId, IntValue=slot (internal)
```

### 便捷属性

```csharp
string? StringValue  // Tag=String 时读写 RefValue (强转)
```

### 真值判断

```csharp
bool IsTruthy   // Void→false, Int64(0)→false, Float64(0)→false, String(null/"")→false, Struct→true
bool IsFalsy    // !IsTruthy
```

### 类型转换

```csharp
long   AsInt64()    // Void→0, Int64→直接取, Float64→截断, String→解析, Struct→TypeError
double AsFloat64()  // Void→0, Int64→转double, Float64→直接取, String→解析, Struct→TypeError
```

### 比较

```csharp
// 相等比较 (EQ/NEQ/BEQ/BNE)
static bool Eq(in BcValue a, in BcValue b)
// 同类型直接比，Int64/Float64 跨类型提升，Struct 比 SubTag+IntValue（值语义），其他跨类型→false

// 三路比较 (CMP/BLT/BGE/BGT/BLE)
static int Compare(in BcValue a, in BcValue b)
// Int64→long.CompareTo, Float64→double.CompareTo, 跨数值→都转double, String→Ordinal字典序, 其他→TypeError
// 返回: 负数(a<b), 零(a==b), 正数(a>b)
```

### ToString 输出格式

| Tag     | 输出示例                  |
|---------|--------------------------|
| Void    | `nil`                    |
| Int64   | `42`                     |
| Float64 | `3.14159265`             |
| String  | 原始内容（null→""）       |
| Struct  | `Struct(type=5,slot=0)`  |

---

## 3. StructHeap — 结构体 Arena 分配器

所有结构体字段存放在一个连续的 `BcValue[]` Arena 中。BcValue(Struct) 本身不持有堆引用（RefValue=null），零 GC 压力。

### 构造

```csharp
var heap = new StructHeap(capacity);  // capacity = Arena 槽位总数
```

### API

```csharp
void RegisterType(byte typeId, int fieldCount)        // 注册类型，编译期调用
int  Alloc(byte typeId)                                // 分配实例，返回 slotIndex（bump allocator，不支持单个释放）
BcValue GetField(int slotIndex, byte fieldOffset)      // 读字段
void SetField(int slotIndex, byte fieldOffset, BcValue value)  // 写字段
void DeepCopy(int srcSlot, int dstSlot, int fieldCount)        // 深拷贝
void Reset()                                           // 热重载清空
```

### 字段访问

```
field N 存放在 Arena[slotIndex + N]
fieldOffset = 编译期确定的字段索引（0, 1, 2, ...）
```

### DeepCopy 使用场景

ISA 中没有对应指令，是宿主侧工具方法。典型场景：函数参数传递结构体值语义时，宿主代码调用 `DeepCopy` 创建独立副本。寄存器 MOV 指令对 Struct 只复制 BcValue（slotIndex 和 typeId），不复制 Arena 中的字段数据，属于浅拷贝。

---

## 4. RegisterFile — 256 寄存器

```csharp
var R = new RegisterFile(stackSize);
```

| 寄存器    | 用途                      | 初始值         |
|-----------|---------------------------|---------------|
| R[0]      | ZERO，硬连线为 Void       | 写入忽略      |
| R[1]      | SP（栈指针）              | stackSize     |
| R[2]      | FP（帧指针）              | stackSize     |
| R[3]      | LR（返回地址）            | 0             |
| R[4]      | SYSCALL/HCALL 参数/返回值 | 0             |
| R[5]–R[7] | SYSCALL/HCALL 额外参数    | 0             |
| R[8]–R[255]| 通用                      | 0             |

R[0] 读永远返回 Void，写永远忽略。`IncInt`/`DecInt` 跳过 R[0]。

---

## 5. BytecodeModule — .irst 字节码文件格式

### 头部 (固定 28 字节)

| Offset | Type  | 字段           | 说明                         |
|--------|-------|----------------|------------------------------|
| 0      | uint32| Magic          | 0x49525354 ("IRST")          |
| 4      | byte  | VersionMajor   | 1                            |
| 5      | byte  | VersionMinor   | —                            |
| 6      | byte  | VersionPatch   | —                            |
| 7      | byte  | Flags          | 保留                          |
| 8      | uint32| EntryPC        | 入口程序计数器                 |
| 12     | uint32| CodeSize       | 代码段字节数                   |
| 16     | int32 | ConstPoolCount | 统一常量池条目数                |
| 20     | int32 | GlobalCount    | 全局变量数                     |
| 24     | uint32| DebugInfoSize  | 调试信息字节数（可为0）         |

### 各段

```
[Header 28 bytes]
[Code: CodeSize bytes]
[ConstantPool: ConstPoolCount entries]
[DebugInfo: DebugInfoSize bytes (optional)]
```

### 常量池条目格式

每个条目以 8 字节为单位对齐：

```
[tag: 1 byte][pad: 7 bytes][data: 变长]
```

tag=1 (Int64):
```
[int64: 8 bytes LE]
```

tag=2 (Float64):
```
[float64: 8 bytes LE]
```

tag=3 (String):
```
[byte_length: 4 bytes LE][utf8_bytes][padding to make (4 + byte_length) a multiple of 8]
```

即 length 前缀 + utf8 内容的总字节数向上对齐到 8 字节。对齐在 tag + pad(7) 的 8 字节头之后独立计算。

例如 3 字节 UTF-8：length(4) + data(3) = 7 bytes，补 1 byte padding 凑 8。
例如 4 字节 UTF-8：length(4) + data(4) = 8 bytes，无额外 padding。

### 校验

- Magic 必须为 0x49525354
- VersionMajor 必须为 1
- CodeSize 上限 16 MB
- EntryPC 必须 < CodeSize
- 单条字符串上限 64 KB
- ConstPoolCount 上限 int.MaxValue

### 加载

```csharp
var module = BytecodeModule.Load(rawBytes);
```

### 字段

```csharp
uint EntryPC
byte[] Code
BcValue[] ConstantPool
int GlobalCount
byte[]? DebugInfo
```

### DebugInfo

格式当前未定义。预留字段，暂不使用。文件加载时仅保留原始字节，不做解析。

---

## 6. VMBase — 虚拟机基类

### VMOptions

```csharp
public class VMOptions
{
    public HostBridge? HostBridge { get; set; }           // 宿主方法注册表，null 则创建空实例
    public int StackSize { get; set; } = 65536;           // 栈大小（BcValue 槽位数）
    public int StructHeapCapacity { get; set; } = 100_000; // 结构体 Arena 总槽位
    public IReadOnlyList<(byte TypeId, int FieldCount)>? StructTypes { get; set; } // 结构体类型注册表
    public uint? EntryReturnPC { get; set; }              // 直接从函数入口执行时的返回地址（LR）
    public Func<uint, (string file, int line)?>? SourceLocator { get; set; } // PC → 源位置；null = 只报 PC
}
```

`SourceLocator` 是**运行期错误带源行号**的唯一开关：宿主把 PC → (文件, 行) 的表交给它
（用 `SourceResolver.FromMap(map)` 包一下即可），VM 在错误边界回填
`RuntimeErrorException.SourceLocation`，于是 `ex.Message` 自带「（文件:行）」。
`null`（默认）时 `Message` 与历史格式逐字一致。

### 生命周期

```csharp
// 创建（子类调用构造函数）
protected VMBase(BytecodeModule module, VMOptions options, IVMDebugHooks? hooks)

// 执行
void Run()      // 状态从 Init/Paused → Running，开始执行；Halted 时抛 InvalidOperationException
void Pause()    // 设置状态为 Paused
void Resume()   // 从 BRK 暂停处继续执行（跳过 BRK 字节）

// 热重载
void Reset(BytecodeModule module)  // 重置所有运行时状态，结构体 Arena 被清空

// 释放
void Dispose()
```

### Halted 后恢复

Halted 后可调用 `Reset(module)` 回到 Init 状态复用同一实例，无需新建。结构体 Arena 会被清空。

### VMState

```csharp
public enum VMState { Init, Running, Paused, Halted }
```

### 公开属性

```csharp
VMContext Context                      // 宿主/调试器访问接口
VMState State                          // 当前状态
RuntimeErrorException? LastError       // 最后一次运行时错误
int StackSize                          // 栈大小
byte[] Code                            // 代码段
BcValue[] ConstantPool                 // 统一常量池
```

### 内部字段

```csharp
StructHeap _structHeap   // internal, 结构体 Arena 分配器
```

### 错误处理

- 发布模式（hooks=null）：错误直接抛出 `RuntimeErrorException`，状态变为 Halted
- 开发模式（hooks!=null）：错误暂停在 Paused 状态，调用 `hooks.OnError(ex)`
- 两种模式都会先按 `VMOptions.SourceLocator` 回填 `SourceLocation`（见 §10），
  所以两条路径上的错误消息都带「（文件:行）」

---

## 7. HostBridge — 宿主方法注册表

```csharp
var bridge = new HostBridge();

// 注册宿主方法
bridge.Register(HostCallIds.PRINT_STR, ctx => {
    Console.Write(ctx.ReadReg(4).StringValue ?? "");
    return BcValue.Void;
});

// 注册后传给 VMOptions
var options = new VMOptions { HostBridge = bridge };
```

参数通过 R[4]–R[7] 传递，返回值存入 R[4]。

---

## 8. VMContext — VM 访问接口

```csharp
BcValue ReadReg(int index)
void    WriteReg(int index, BcValue v)
BcValue ReadGlobal(int index)              // 越界抛 SlotOverflow
void    WriteGlobal(int index, BcValue v)  // 越界抛 SlotOverflow
BcValue ReadStack(int index)               // 越界抛 SlotOverflow
void    WriteStack(int index, BcValue v)   // 越界抛 SlotOverflow

uint PC
VMState State
int StackSize
ReadOnlySpan<byte> Code
IReadOnlyList<BcValue> ConstantPool
```

---

## 9. IVMDebugHooks — 调试钩子接口

```csharp
public interface IVMDebugHooks
{
    bool PreStep(VMBase vm, uint pc);       // 每条指令前，返回 false 暂停
    void OnBreak(VMBase vm, uint pc);       // BRK 指令命中
    void OnError(RuntimeErrorException ex); // 运行时错误
}
```

发布模式传 null，JIT 消除所有分支，零开销。

---

## 10. RuntimeErrorException — 运行时异常

```csharp
public class RuntimeErrorException : Exception
{
    public RuntimeErrorType ErrorType { get; }
    public uint PC { get; }                        // 出错指令的字节偏移（0 = 未知）
    public string Detail { get; }                  // 原始消息（无前缀、无位置后缀）
    public (string file, int line)? SourceLocation { get; } // 源位置；null = 未知（不伪造）

    public override string Message =>
        SourceLocation is { } s
            ? $"[{ErrorType}] at 0x{PC:X}: {Detail}  （{s.file}:{s.line}）"
            : $"[{ErrorType}] at 0x{PC:X}: {Detail}";
}
```

**位置回填只有一个落点**：`VMBase.Run` 的 catch 里按 `PC` 查一次 `SourceLocator`，
写进 `SourceLocation`。因此 CLI / IDE / 示例宿主全都只是打印 `ex.Message` ——
不再各自查映射表（漏一处就少一处行号）。

`PC` 的可信性：组件（`StructHeap` / `BcValue` / `VMContext`）不持有 VM，抛的异常 `PC` 为 0
（“未知”），由 `VMBase.RunLoop` 的 catch 用**当前指令的 PC** 补齐
（`AdoptPC`）—— 那是唯一还在现场的边界；`_pc` 字段在指令执行途中是陈旧的。

```csharp
public enum RuntimeErrorType
{
    TypeError,
    DivideByZero,
    SlotOverflow,
    StackOverflow,
    StackUnderflow,
    IllegalOpcode,
    ArrayBounds,
}
```

---

## 10.1 SourceResolver — PC → 源位置 的解析器

```csharp
public static class SourceResolver
{
    // 表为 null / 空 ⇒ 返回 null（VM 不回填，错误只报 PC）
    public static Func<uint, (string file, int line)?>? FromMap(
        IReadOnlyDictionary<int, (string file, int line)>? map);

    // 取 key ≤ pc 的最近一条映射；没有则 null
    public static (string file, int line)? Lookup(
        IReadOnlyDictionary<int, (string file, int line)> map, uint pc);

    // 整张映射平移到新基址：链接器把各模块的机器码搬到 codeBase[i]，
    // 指令只发生平移 ⇒ 源映射跟着平移即可继续对上
    public static Dictionary<int, (string file, int line)> Shift(
        IReadOnlyDictionary<int, (string file, int line)> map, int codeBase);

    // 合并若干「(基址, 映射)」段
    public static Dictionary<int, (string file, int line)> Merge(
        IEnumerable<(int CodeBase, IReadOnlyDictionary<int, (string file, int line)> Map)> parts);
}
```

放这里而不是前端/字节码层：表本身只是数据，`Runtime.Shared` 拿它不需要认识 AST；
而这段查找逻辑若让每个宿主各写一遍，就会重复 —— 这正是“位置解析散落在消费端”的由来。

---

## 11. ISA — 指令集

所有指令通过 `byte[] Code` 中的 opcode 及操作数编码。操作数紧跟 opcode 字节，PC 自动前进（PC += 指令长度）。

### 11.1 指令速查表

| Opcode | Hex  | 长度 | 格式                          | 说明                                                                 |
|--------|------|------|-------------------------------|----------------------------------------------------------------------|
| NOP    | 0x00 | 1    | [NOP]                         | 空操作                                                               |
| HALT   | 0x01 | 1    | [HALT]                        | 停机                                                                 |
| BRK    | 0x02 | 1    | [BRK]                         | 断点（开发模式调 hooks.OnBreak）                                      |
| SYSCALL| 0x03 | 2    | [SYSCALL][func]               | 系统调用（仅 EXIT/ABORT）                                             |
| LNIL   | 0x10 | 2    | [LNIL][rd]                    | R[rd] = Void                                                         |
| LTRUE  | 0x11 | 2    | [LTRUE][rd]                   | R[rd] = Int64(1)                                                     |
| LFALSE | 0x12 | 2    | [LFALSE][rd]                  | R[rd] = Int64(0)                                                     |
| LDC    | 0x14 | 6    | [LDC][rd][idx:4 LE]           | R[rd] = ConstantPool[idx]                                            |
| MOV    | 0x18 | 3    | [MOV][rd][rs]                 | R[rd] = R[rs]（Struct 为浅拷贝，只复制 slotIndex 和 typeId）           |
| SWAP   | 0x19 | 3    | [SWAP][rd][rs]                | swap R[rd], R[rs]                                                    |
| PUSH   | 0x20 | 2    | [PUSH][rs]                    | SP--，检查 SP < 0 则 StackOverflow，然后 stack[SP] = R[rs]            |
| POP    | 0x21 | 2    | [POP][rd]                     | R[rd] = stack[SP++]                                                  |
| PUSHN  | 0x22 | 3    | [PUSHN][n:2 LE]               | SP -= n（不清零腾出的槽位，内容为未定义值）                             |
| POPN   | 0x23 | 3    | [POPN][n:2 LE]                | SP += n                                                              |
| FRAME  | 0x24 | 3    | [FRAME][n:2 LE]               | push LR（检测 SP<0），push FP（检测 SP<0），FP=SP，SP-=n（检测 SP<0）；每步独立检测，任一步溢出即抛 StackOverflow |
| LEAVE  | 0x25 | 1    | [LEAVE]                       | SP=FP, pop FP, pop LR                                                |
| GETL   | 0x30 | 4    | [GETL][rd][slot:2 LE]         | R[rd] = stack[FP-1-slot]                                             |
| SETL   | 0x31 | 4    | [SETL][rs][slot:2 LE]         | stack[FP-1-slot] = R[rs]                                             |
| GETG   | 0x32 | 4    | [GETG][rd][slot:2 LE]         | R[rd] = globals[slot]                                                |
| SETG   | 0x33 | 4    | [SETG][rs][slot:2 LE]         | globals[slot] = R[rs]                                                |
| GETR   | 0x34 | 3    | [GETR][rd][rs]                | R[rd] = stack[FP-1-R[rs].Int]                                        |
| SETR   | 0x35 | 3    | [SETR][rs][rslot]             | stack[FP-1-R[rslot].Int] = R[rs]                                     |
| GETGI  | 0x36 | 3    | [GETGI][rd][rs]               | R[rd] = globals[R[rs].Int]                                           |
| SETGI  | 0x37 | 3    | [SETGI][rs][rslot]            | globals[R[rslot].Int] = R[rs]                                        |
| IADD   | 0x50 | 4    | [IADD][rd][rs1][rs2]          | R[rd] = R[rs1].Int + R[rs2].Int                                      |
| ISUB   | 0x51 | 4    | [ISUB][rd][rs1][rs2]          | R[rd] = R[rs1].Int - R[rs2].Int                                      |
| IMUL   | 0x52 | 4    | [IMUL][rd][rs1][rs2]          | R[rd] = R[rs1].Int * R[rs2].Int                                      |
| IDIV   | 0x53 | 4    | [IDIV][rd][rs1][rs2]          | R[rd] = R[rs1].Int / R[rs2].Int (除数为0→DivideByZero)                |
| FADD   | 0x55 | 4    | [FADD][rd][rs1][rs2]          | R[rd] = R[rs1].Float + R[rs2].Float                                  |
| FSUB   | 0x56 | 4    | [FSUB][rd][rs1][rs2]          | R[rd] = R[rs1].Float - R[rs2].Float                                  |
| FMUL   | 0x57 | 4    | [FMUL][rd][rs1][rs2]          | R[rd] = R[rs1].Float * R[rs2].Float                                  |
| FDIV   | 0x58 | 4    | [FDIV][rd][rs1][rs2]          | R[rd] = R[rs1].Float / R[rs2].Float (除数为0→DivideByZero)            |
| INEG   | 0x59 | 3    | [INEG][rd][rs]                | R[rd] = -R[rs].Int                                                   |
| FNEG   | 0x5A | 3    | [FNEG][rd][rs]                | R[rd] = -R[rs].Float                                                 |
| IINC   | 0x5B | 2    | [IINC][rs]                    | R[rs].Int++（要求 Tag=Int64，否则 TypeError）；对 R[0] 静默忽略（写保护，不报错）|
| IDEC   | 0x5C | 2    | [IDEC][rs]                    | R[rs].Int--（要求 Tag=Int64，否则 TypeError）；对 R[0] 静默忽略（写保护，不报错）|
| AND    | 0x60 | 4    | [AND][rd][rs1][rs2]           | R[rd] = R[rs1].Int & R[rs2].Int                                      |
| OR     | 0x61 | 4    | [OR][rd][rs1][rs2]            | R[rd] = R[rs1].Int \| R[rs2].Int                                     |
| XOR    | 0x62 | 4    | [XOR][rd][rs1][rs2]           | R[rd] = R[rs1].Int ^ R[rs2].Int                                      |
| SHL    | 0x63 | 4    | [SHL][rd][rs1][rs2]           | R[rd] = R[rs1].Int << (R[rs2].Int & 0x3F)                            |
| SHR    | 0x64 | 4    | [SHR][rd][rs1][rs2]           | R[rd] = (ulong)R[rs1].Int >> (R[rs2].Int & 0x3F)                     |
| SAR    | 0x65 | 4    | [SAR][rd][rs1][rs2]           | R[rd] = R[rs1].Int >> (R[rs2].Int & 0x3F)（算术右移）                  |
| NOT    | 0x66 | 3    | [NOT][rd][rs]                 | R[rd] = ~R[rs].Int                                                   |
| ROL    | 0x67 | 4    | [ROL][rd][rs1][rs2]           | 64 位循环左移                                                         |
| ROR    | 0x68 | 4    | [ROR][rd][rs1][rs2]           | 64 位循环右移                                                         |
| ANDI   | 0x69 | 4    | [ANDI][rd][rs][imm8]          | R[rd] = R[rs].Int & imm8                                             |
| ORI    | 0x6A | 4    | [ORI][rd][rs][imm8]           | R[rd] = R[rs].Int \| imm8                                            |
| XORI   | 0x6B | 4    | [XORI][rd][rs][imm8]          | R[rd] = R[rs].Int ^ imm8                                             |
| SHLI   | 0x6C | 4    | [SHLI][rd][rs][imm8]          | R[rd] = R[rs].Int << (imm8 & 0x3F)                                   |
| SHRI   | 0x6D | 4    | [SHRI][rd][rs][imm8]          | R[rd] = (ulong)R[rs].Int >> (imm8 & 0x3F)                            |
| CLZ    | 0x6E | 3    | [CLZ][rd][rs]                 | R[rd] = 前导零计数                                                    |
| CMP    | 0x70 | 4    | [CMP][rd][rs1][rs2]           | R[rd] = Compare(R[rs1], R[rs2]) → Int64(-1/0/1)                      |
| IEQ    | 0x76 | 4    | [IEQ][rd][rs1][rs2]           | R[rd] = (R[rs1].Int == R[rs2].Int) ? 1 : 0                           |
| INE    | 0x77 | 4    | [INE][rd][rs1][rs2]           | R[rd] = (R[rs1].Int != R[rs2].Int) ? 1 : 0                           |
| FEQ    | 0x7C | 4    | [FEQ][rd][rs1][rs2]           | R[rd] = (R[rs1].Float == R[rs2].Float) ? 1 : 0                       |
| TYPE   | 0x7E | 3    | [TYPE][rd][rs]                | R[rd] = (long)R[rs].Tag                                              |
| ISTYPE | 0x7F | 4    | [ISTYPE][rd][rs][tag]         | R[rd] = (R[rs].Tag == tag) ? 1 : 0                                   |
| RET    | 0x80 | 1    | [RET]                         | PC = R[3] (LR)                                                       |
| JMPR   | 0x81 | 2    | [JMPR][rs]                    | PC = R[rs].Int                                                       |
| CALLR  | 0x82 | 2    | [CALLR][rs]                   | R[3]=PC+2, PC=R[rs].Int                                              |
| JMP    | 0x85 | 5    | [JMP][addr:4 LE]              | PC = addr                                                            |
| CALL   | 0x86 | 5    | [CALL][addr:4 LE]             | R[3]=PC+5, PC=addr                                                   |
| BZ     | 0x87 | 3    | [BZ][rs][rel:1 signed]        | if R[rs].IsFalsy then PC = PC+3+rel                                  |
| BNZ    | 0x88 | 3    | [BNZ][rs][rel:1 signed]       | if R[rs].IsTruthy then PC = PC+3+rel                                 |
| BEQ    | 0x89 | 4    | [BEQ][rs1][rs2][rel:1]        | if Eq(R[rs1],R[rs2]) then PC = PC+4+rel                              |
| BNE    | 0x8A | 4    | [BNE][rs1][rs2][rel:1]        | if !Eq(R[rs1],R[rs2]) then PC = PC+4+rel                             |
| BLT    | 0x8B | 4    | [BLT][rs1][rs2][rel:1]        | if Compare < 0 then PC = PC+4+rel                                    |
| BGE    | 0x8C | 4    | [BGE][rs1][rs2][rel:1]        | if Compare >= 0 then PC = PC+4+rel                                   |
| BGT    | 0x8D | 4    | [BGT][rs1][rs2][rel:1]        | if Compare > 0 then PC = PC+4+rel                                    |
| BLE    | 0x8E | 4    | [BLE][rs1][rs2][rel:1]        | if Compare <= 0 then PC = PC+4+rel                                   |
| SNEW   | 0x90 | 5    | [SNEW][rd][typeId][count:2 LE]| 调用 heap.Alloc(typeId)，在 Arena 中预留 count 个连续 BcValue 槽位（全部初始化为 Void），返回起始 slotIndex 存入 R[rd]。count 与 RegisterType(typeId, count) 一致。单条指令只分配一个实例。|
| SGET   | 0x91 | 4    | [SGET][rd][robj][field]       | R[robj] 须为 Struct（否则 TypeError）；field 须 < 字段数（否则 SlotOverflow，message 含 "SGET: field N out of range M"）；R[rd] = heap.GetField(R[robj].IntValue, field) |
| SSET   | 0x92 | 4    | [SSET][rs][robj][field]       | R[robj] 须为 Struct（否则 TypeError）；field 须 < 字段数（否则 SlotOverflow，message 含 "SSET: field N out of range M"）；heap.SetField(R[robj].IntValue, field, R[rs]) |
| TOINT  | 0xA0 | 3    | [TOINT][rd][rs]               | R[rd] = R[rs].AsInt64()                                              |
| TOFLT  | 0xA1 | 3    | [TOFLT][rd][rs]               | R[rd] = R[rs].AsFloat64()                                            |
| TOSTR  | 0xA2 | 3    | [TOSTR][rd][rs]               | R[rd] = new BcValue(R[rs].ToString())                                |
| TOBOOL | 0xA3 | 3    | [TOBOOL][rd][rs]              | R[rd] = R[rs].IsTruthy ? 1 : 0                                       |
| HCALL  | 0xB0 | 3    | [HCALL][id:2 LE]              | host.TryInvoke(id, ctx) → R[4] = result                              |
| VMCALL | 0xC0 | 2    | [VMCALL][param]               | PC+=2, state=Paused（返回宿主，宿主再次调用 Run() 继续执行）           |
| VMRET  | 0xC1 | 1    | [VMRET]                       | PC+=1, state=Paused（返回宿主，宿主再次调用 Run() 继续执行）           |

### 11.2 FRAME/LEAVE 栈帧布局

```
FRAME n:
  stack[--SP] = R[3]     // push LR
  stack[--SP] = R[2]     // push FP
  FP = SP
  SP = SP - n

  栈帧布局 (高地址 → 低地址):
    [FP+1] = saved LR
    [FP]   = saved FP
    [FP-1] = local 0
    [FP-2] = local 1
    ...
    [FP-n] = local n-1

GETL/SETL slot 计算: addr = FP - 1 - slot

LEAVE:
  SP = FP
  R[2] = stack[SP]       // pop FP
  R[3] = stack[SP+1]     // pop LR
  SP = SP + 2
```

### 11.3 分支指令的相对偏移

BZ/BNZ/BEQ/BNE/BLT/BGE/BGT/BLE 使用有符号 1 字节相对偏移。

目标地址计算：

```
BZ/BNZ:     PC = (PC + 3) + (int8)rel
BEQ–BLE:    PC = (PC + 4) + (int8)rel
```

rel 为有符号，正值向前跳，负值向后跳。

### 11.4 VMCALL/VMRET 恢复机制

Run() 返回后，宿主可再次调用 `vm.Run()` 继续执行。Run() 方法会将状态从 Paused 设为 Running，然后从当前 PC 继续执行循环。Resume() 仅用于 BRK 断点恢复，会跳过 BRK 字节。VMCALL/VMRET 后宿主应直接调用 Run()。

### 11.5 SYSCALL 功能号

仅保留 2 个：

```csharp
public enum SyscallFunc : byte
{
    EXIT  = 0x00,  // 停机
    ABORT = 0x10,  // 异常中止 (R[4]=code)，抛出 RuntimeError
}
```

### 11.6 HCALL 宿主方法 ID

```csharp
public static class HostCallIds
{
    // I/O
    public const ushort PRINT_STR   = 0x0001;  // R[4]=string
    public const ushort PRINT_INT   = 0x0002;  // R[4]=int
    public const ushort PRINT_CHAR  = 0x0003;  // R[4]=char_code
    public const ushort READ_INT    = 0x0004;  // → R[4]
    public const ushort READ_STR    = 0x0005;  // → R[4]

    // 时间
    public const ushort TIME        = 0x000B;  // Unix ms → R[4]
    public const ushort SLEEP       = 0x000C;  // R[4]=ms
    public const ushort CLOCK       = 0x000D;  // 微秒 → R[4]

    // 工具
    public const ushort RAND        = 0x000E;  // 随机 int64 → R[4]
    public const ushort MEMSIZE     = 0x000F;  // GC 内存 → R[4]

    // 字符串
    public const ushort STR_CONCAT  = 0x0020;  // R[4]=s1, R[5]=s2 → R[4]
    public const ushort STR_LEN     = 0x0021;  // R[4]=string → R[4]
    public const ushort STR_SUBSTR  = 0x0022;  // R[4]=str, R[5]=start, R[6]=len → R[4]
    public const ushort STR_FIND    = 0x0023;  // R[4]=haystack, R[5]=needle → R[4]
    public const ushort STR_REPLACE = 0x0024;  // R[4]=src, R[5]=old, R[6]=new → R[4]

    // 数学
    public const ushort MATH_FMOD   = 0x0030;  // R[4]=a, R[5]=b → R[4]
    public const ushort MATH_IMOD   = 0x0031;  // R[4]=a, R[5]=b → R[4]
    public const ushort MATH_FPOW   = 0x0032;  // R[4]=base, R[5]=exp → R[4]
    public const ushort MATH_SQRT   = 0x0033;  // R[4]=value → R[4]
    public const ushort MATH_SIN    = 0x0034;  // R[4]=radians → R[4]
    public const ushort MATH_COS    = 0x0035;  // R[4]=radians → R[4]
    public const ushort MATH_TAN    = 0x0036;  // R[4]=radians → R[4]
    public const ushort MATH_LOG    = 0x0037;  // R[4]=value → R[4]
    public const ushort MATH_ABS    = 0x0038;  // R[4]=value → R[4]
    public const ushort MATH_UMOD   = 0x0039;  // R[4]=a, R[5]=b → R[4]（ulong 取余）
}
```

> `MATH_IMOD` / `MATH_UMOD` 是语言内建运算：`VMBase` 构造时通过
> `MathStandardLibrary.Register` 安装，宿主显式注册同 ID 时不会被覆盖。
> `MATH_FMOD` / `MATH_FPOW` / 浮点标准库仍属于 F13 未放行范围。
>
> **内置 VM 不提供任何 IEC 数学库函数**（`SQRT` / `SIN` / `LN` / 浮点 `ABS`…）：
> 走「用户自建宿主注入」路径，由使用者自行向 `hostFunctions` 登记 extern 名
> （`sqrt` / `sin` / `log` / `fabs`）并提供 handler。
> 内置 VM 只装**语言内建运算符**的实现（如整数 `MOD`），不装 IEC 标准库函数。
> 该边界由 `IronST.Runtime.Tests/VMBuiltinMathGuardTests.cs` 守卫。

---

## 12. 使用示例

### 12.1 基本执行流程

```csharp
// 1. 加载模块
var module = BytecodeModule.Load(irstBytes);

// 2. 注册宿主方法
var bridge = new HostBridge();
bridge.Register(HostCallIds.PRINT_STR, ctx => {
    Console.Write(ctx.ReadReg(4).StringValue ?? "");
    return BcValue.Void;
});

// 3. 创建 VM 选项
var options = new VMOptions
{
    HostBridge = bridge,
    StackSize = 65536,
    StructHeapCapacity = 100_000,
};

// 4. 创建 VM（需子类化 VMBase，或使用已有的 VM 实现）
var vm = new MyVM(module, options);

// 5. 注册结构体类型
vm._structHeap.RegisterType(typeId: 1, fieldCount: 3);

// 6. 执行
vm.Run();

// 7. 热重载
var newModule = BytecodeModule.Load(newBytes);
vm.Reset(newModule);
vm.Run();
```

### 12.2 宿主方法参数约定

所有 HCALL/SYSCALL 通过寄存器传递参数：

- 输入：R[4]–R[7]（最多 4 个参数）
- 输出：R[4]（1 个返回值）

宿主方法签名：`Func<VMContext, BcValue>`

### 12.3 结构体操作流程

```
编译期:
  typeId = 编译器分配
  RegisterType(typeId, fieldCount)

运行时 (SNEW/SGET/SSET):
  SNEW rd, typeId, count  →  slot = heap.Alloc(typeId), R[rd] = MakeStruct(typeId, slot)
  SGET rd, robj, field    →  R[rd] = heap.GetField(R[robj].IntValue, field)
  SSET rs, robj, field    →  heap.SetField(R[robj].IntValue, field, R[rs])
```

---

## 13. 错误码速查

| RuntimeErrorType | 触发条件                                                                 |
|-----------------|--------------------------------------------------------------------------|
| TypeError       | 类型不匹配（IINC/IDEC 非 Int64、LEN/SCONCAT 非 String、Compare 不兼容类型、SGET/SSET 目标非 Struct等）|
| DivideByZero    | IDIV/FDIV 除数为 0                                                       |
| SlotOverflow    | GETL/SETL/GETG/SETG 栈/全局越界、SGET/SSET field 越界、StructHeap Arena 越界 |
| StackOverflow   | PUSH/FRAME 导致 SP < 0                                                   |
| StackUnderflow  | POP/POPN 导致 SP > StackSize                                             |
| IllegalOpcode   | 未定义 opcode、未知 SYSCALL、HCALL 未注册                                  |
```