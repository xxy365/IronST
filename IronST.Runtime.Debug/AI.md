# IronST Bytecode VM — AI 参考手册

## 架构概述

IronST 是 IEC 61131-3 ST 字节码虚拟机。运行时核心在 `IronST.Runtime.Shared`，构建与调试工具在 `IronST.Runtime.Dev`，发布 VM 在 `IronST.Runtime`。

**执行模型**：线性指令流 + 256 寄存器 + 调用栈 + 全局变量 + 统一常量池 + 结构体堆。

---

## 值类型 BcValue (24 字节 tagged union)

| Tag | 枚举值 | 含义 |
|-----|--------|------|
| 0 | Void | 空值 |
| 1 | Int64 | 64 位有符号整数 |
| 2 | Float64 | IEEE 754 双精度 |
| 3 | String | 托管字符串 |
| 4 | — | 保留，未定义 |
| 5 | Struct | 结构体实例（含 typeId + slotIndex） |

**真值规则**：Void→false，Int64(0)→false，Float64(0)→false，String(null/"")→false，Struct→true。

### BcValue 公共 API

属性：
- `Tag` : ValueTag — 类型标签
- `IntValue` : long — Tag=Int64 时数值，Tag=Struct 时 slotIndex
- `FloatValue` : double — Tag=Float64 时 IEEE 754 值
- `StringValue` : string? — Tag=String 时读写 RefValue，其他 null
- `IsTruthy` : bool — Void/0/0/null""→false，其他 true
- `IsFalsy` : bool — !IsTruthy

构造：
- `BcValue.Void` — Void
- `new BcValue(42L)` — Int64
- `new BcValue(3.14)` — Float64
- `new BcValue("hello")` — String
- `BcValue.MakeStruct(typeId, slot)` — Struct（internal）

转换：
- `AsInt64() : long` — Void→0, Float64→截断, String→解析, Struct→TypeError
- `AsFloat64() : double` — Void→0, Int64→转double, String→解析, Struct→TypeError
- `ToString() : string` — Void→"nil", Int64→"42", Float64→"3.14...", String→原文, Struct→"Struct(type=N,slot=M)"

比较（静态方法）：
- `Eq(a, b) : bool` — 同类型直比，Int64/Float64 跨类型提升，Struct 比 SubTag+IntValue
- `Compare(a, b) : int` — 数值比较/跨数值转double，String 按 Ordinal 字典序，返回负数/零/正数

---

## 寄存器

| 寄存器 | 用途 | 说明 |
|--------|------|------|
| R[0] | ZERO | 硬连线 Void，写入忽略 |
| R[1] | SP | 栈指针 |
| R[2] | FP | 帧指针 |
| R[3] | LR | 返回地址 |
| R[4]–R[7] | A0–A3 | HCALL/SYSCALL 参数与返回值 |
| R[8]–R[255] | 通用 | 由 BytecodeAssembler 从 R[8] 自动分配 |

### 初始状态（VM 启动时）

- R[0] = Void（硬连线，永远 Void）
- R[1] = StackSize（SP，栈从高地址向下增长）
- R[2] = StackSize（FP）
- R[3] = 0（LR）
- R[4]–R[255] = BcValue.Void
- globals[0..GlobalCount-1] = BcValue.Void

---

## 枚举类型

### VMState

```
Init = 0, Running = 1, Paused = 2, Halted = 3
```

### RuntimeErrorType

```
TypeError, DivideByZero, SlotOverflow, StackOverflow, StackUnderflow, IllegalOpcode
```

### RuntimeErrorException

继承 Exception（有 Message/StackTrace），额外属性：
- `ErrorType` : RuntimeErrorType
- `PC` : uint

---

## 指令集 (ISA)

所有操作数 little-endian。每条指令长度由 opcode 确定，同一指令长度固定。

### 系统控制

| 指令 | 格式 | 长度 | 说明 |
|------|------|------|------|
| NOP | [op] | 1 | 空操作 |
| HALT | [op] | 1 | 停机，状态→Halted |
| BRK | [op] | 1 | 断点。开发模式调 hooks.OnBreak()；发布模式同 HALT |

### 常量加载

| 指令 | 格式 | 长度 | 说明 |
|------|------|------|------|
| LNIL | [op][rd] | 2 | R[rd]=Void |
| LTRUE | [op][rd] | 2 | R[rd]=Int64(1) |
| LFALSE | [op][rd] | 2 | R[rd]=Int64(0) |
| LDC | [op][rd][idx:4] | 6 | R[rd]=ConstantPool[idx]（Int64/Float64/String 统一入口） |

整数、浮点、字符串均通过 `LDC` 从统一常量池加载。

### 数据移动

| 指令 | 格式 | 长度 | 说明 |
|------|------|------|------|
| MOV | [op][rd][rs] | 3 | R[rd]=R[rs]，Struct 为浅拷贝（只复制 slotIndex+typeId） |
| SWAP | [op][rd][rs] | 3 | 交换 R[rd] 与 R[rs] |

### 栈操作

| 指令 | 格式 | 长度 | 说明 |
|------|------|------|------|
| PUSH | [op][rs] | 2 | SP--，检查溢出，stack[SP]=R[rs] |
| POP | [op][rd] | 2 | R[rd]=stack[SP++] |
| PUSHN | [op][n:2] | 3 | SP-=n（不清零） |
| POPN | [op][n:2] | 3 | SP+=n |
| FRAME | [op][n:2] | 3 | push LR, push FP, FP=SP, SP-=n（每步检测溢出） |
| LEAVE | [op] | 1 | SP=FP, pop FP, pop LR |

**栈帧布局**：
```
[FP+1] = saved LR
[FP]   = saved FP
[FP-1] = local 0
[FP-2] = local 1
...
[FP-n] = local n-1
```
GETL/SETL 计算：`addr = FP - 1 - slot`

### 变量访问

| 指令 | 格式 | 长度 | 说明 |
|------|------|------|------|
| GETL | [op][rd][slot:2] | 4 | R[rd]=stack[FP-1-slot] |
| SETL | [op][rs][slot:2] | 4 | stack[FP-1-slot]=R[rs] |
| GETG | [op][rd][slot:2] | 4 | R[rd]=globals[slot] |
| SETG | [op][rs][slot:2] | 4 | globals[slot]=R[rs] |
| GETR | [op][rd][rs] | 3 | R[rd]=stack[FP-1-R[rs].Int] |
| SETR | [op][rs][rslot] | 3 | stack[FP-1-R[rslot].Int]=R[rs] |
| GETGI | [op][rd][rs] | 3 | R[rd]=globals[R[rs].Int] |
| SETGI | [op][rs][rslot] | 3 | globals[R[rslot].Int]=R[rs] |

### 算术

| 指令 | 格式 | 长度 | 说明 |
|------|------|------|------|
| IADD | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Int+R[rs2].Int |
| ISUB | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Int-R[rs2].Int |
| IMUL | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Int*R[rs2].Int |
| IDIV | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Int/R[rs2].Int，除零→DivideByZero |
| FADD | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Float+R[rs2].Float |
| FSUB | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Float-R[rs2].Float |
| FMUL | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Float*R[rs2].Float |
| FDIV | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Float/R[rs2].Float，除零→DivideByZero |
| INEG | [op][rd][rs] | 3 | R[rd]=-R[rs].Int |
| FNEG | [op][rd][rs] | 3 | R[rd]=-R[rs].Float |
| IINC | [op][rs] | 2 | R[rs].Int++，要求 Tag=Int64，对 R[0] 静默忽略 |
| IDEC | [op][rs] | 2 | R[rs].Int--，要求 Tag=Int64，对 R[0] 静默忽略 |

### 位运算

| 指令 | 格式 | 长度 | 说明 |
|------|------|------|------|
| AND | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Int & R[rs2].Int |
| OR | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Int \| R[rs2].Int |
| XOR | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Int ^ R[rs2].Int |
| SHL | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Int << (R[rs2].Int & 0x3F) |
| SHR | [op][rd][rs1][rs2] | 4 | R[rd]=(ulong)R[rs1].Int >> (R[rs2].Int & 0x3F) |
| SAR | [op][rd][rs1][rs2] | 4 | R[rd]=R[rs1].Int >> (R[rs2].Int & 0x3F)，算术右移 |
| NOT | [op][rd][rs] | 3 | R[rd]=~R[rs].Int |
| ROL | [op][rd][rs1][rs2] | 4 | 64 位循环左移 |
| ROR | [op][rd][rs1][rs2] | 4 | 64 位循环右移 |
| ANDI | [op][rd][rs][imm8] | 4 | R[rd]=R[rs].Int & imm8 |
| ORI | [op][rd][rs][imm8] | 4 | R[rd]=R[rs].Int \| imm8 |
| XORI | [op][rd][rs][imm8] | 4 | R[rd]=R[rs].Int ^ imm8 |
| SHLI | [op][rd][rs][imm8] | 4 | R[rd]=R[rs].Int << (imm8 & 0x3F) |
| SHRI | [op][rd][rs][imm8] | 4 | R[rd]=(ulong)R[rs].Int >> (imm8 & 0x3F) |
| CLZ | [op][rd][rs] | 3 | 前导零计数 |

### 比较

| 指令 | 格式 | 长度 | 说明 |
|------|------|------|------|
| CMP | [op][rd][rs1][rs2] | 4 | R[rd]=Compare(R[rs1],R[rs2])→Int64(-1/0/1) |
| IEQ | [op][rd][rs1][rs2] | 4 | R[rd]=(R[rs1].Int==R[rs2].Int)?1:0 |
| INE | [op][rd][rs1][rs2] | 4 | R[rd]=(R[rs1].Int!=R[rs2].Int)?1:0 |
| FEQ | [op][rd][rs1][rs2] | 4 | R[rd]=(R[rs1].Float==R[rs2].Float)?1:0 |
| TYPE | [op][rd][rs] | 3 | R[rd]=(long)R[rs].Tag |
| ISTYPE | [op][rd][rs][tag] | 4 | R[rd]=(R[rs].Tag==tag)?1:0 |

排序比较使用 CMP + 分支指令组合。例如 `a < b`：先 `CMP r, a, b`，再 `BLT r, ZERO, label`。或直接 `BLT a, b, label`（BLT 内部调用 Compare）。

### 控制流

| 指令 | 格式 | 长度 | 说明 |
|------|------|------|------|
| RET | [op] | 1 | PC=R[3](LR) |
| JMPR | [op][rs] | 2 | PC=R[rs].Int |
| CALLR | [op][rs] | 2 | R[3]=PC+2, PC=R[rs].Int |
| JMP | [op][addr:4] | 5 | PC=addr（绝对地址） |
| CALL | [op][addr:4] | 5 | R[3]=PC+5, PC=addr |
| BZ | [op][rs][rel:1] | 3 | R[rs].IsFalsy → PC=PC+3+rel |
| BNZ | [op][rs][rel:1] | 3 | R[rs].IsTruthy → PC=PC+3+rel |
| BEQ | [op][r1][r2][rel:1] | 4 | Eq 为 true 时跳转 |
| BNE | [op][r1][r2][rel:1] | 4 | Eq 为 false 时跳转 |
| BLT | [op][r1][r2][rel:1] | 4 | Compare < 0 时跳转 |
| BGE | [op][r1][r2][rel:1] | 4 | Compare >= 0 时跳转 |
| BGT | [op][r1][r2][rel:1] | 4 | Compare > 0 时跳转 |
| BLE | [op][r1][r2][rel:1] | 4 | Compare <= 0 时跳转 |

rel 为有符号 1 字节偏移，目标 = (PC+指令长度)+rel。

### 结构体

| 指令 | 格式 | 长度 | 说明 |
|------|------|------|------|
| SNEW | [op][rd][typeId][count:2] | 5 | Arena 分配 count 个 BcValue 槽位，R[rd]=MakeStruct(typeId, slot) |
| SGET | [op][rd][robj][field] | 4 | R[rd]=heap.GetField(R[robj].IntValue, field)，robj 须为 Struct |
| SSET | [op][rs][robj][field] | 4 | heap.SetField(R[robj].IntValue, field, R[rs]) |

结构体字段存放在连续 `BcValue[]` Arena 中。MOV 对 Struct 只复制 BcValue（浅拷贝），不复制 Arena 字段。

### 类型转换

| 指令 | 格式 | 长度 | 说明 |
|------|------|------|------|
| TOINT | [op][rd][rs] | 3 | R[rd]=R[rs].AsInt64() |
| TOFLT | [op][rd][rs] | 3 | R[rd]=R[rs].AsFloat64() |
| TOSTR | [op][rd][rs] | 3 | R[rd]=R[rs].ToString() |
| TOBOOL | [op][rd][rs] | 3 | R[rd]=R[rs].IsTruthy?1:0 |

### 宿主互操作

| 指令 | 格式 | 长度 | 说明 |
|------|------|------|------|
| SYSCALL | [op][func:1] | 2 | 系统调用。EXIT(0x00)=停机，ABORT(0x10)=异常中止(R[4]=code) |
| HCALL | [op][id:2] | 3 | 调用宿主注册方法，参数 R[4]–R[7]，返回值 R[4] |
| VMCALL | [op][param] | 2 | PC+=2, state=Paused（返回宿主，宿主再调 Run() 继续） |
| VMRET | [op] | 1 | PC+=1, state=Paused（返回宿主，宿主再调 Run() 继续） |

**HCALL 宿主方法 ID**：

| ID | 名称 | 用法 |
|----|------|------|
| 0x0001 | PRINT_STR | R[4]=string |
| 0x0002 | PRINT_INT | R[4]=int |
| 0x0003 | PRINT_CHAR | R[4]=char_code |
| 0x0004 | READ_INT | →R[4] |
| 0x0005 | READ_STR | →R[4] |
| 0x000B | TIME | Unix ms→R[4] |
| 0x000C | SLEEP | R[4]=ms |
| 0x000D | CLOCK | 微秒→R[4] |
| 0x000E | RAND | 随机 int64→R[4] |
| 0x000F | MEMSIZE | GC 内存→R[4] |
| 0x0020 | STR_CONCAT | R[4]=s1, R[5]=s2→R[4] |
| 0x0021 | STR_LEN | R[4]=string→R[4] |
| 0x0022 | STR_SUBSTR | R[4]=str, R[5]=start, R[6]=len→R[4] |
| 0x0023 | STR_FIND | R[4]=haystack, R[5]=needle→R[4] |
| 0x0024 | STR_REPLACE | R[4]=src, R[5]=old, R[6]=new→R[4] |
| 0x0030 | MATH_FMOD | R[4]=a, R[5]=b→R[4] |
| 0x0031 | MATH_IMOD | R[4]=a, R[5]=b→R[4] |
| 0x0032 | MATH_FPOW | R[4]=base, R[5]=exp→R[4] |
| 0x0033 | MATH_SQRT | R[4]=value→R[4] |
| 0x0034 | MATH_SIN | R[4]=radians→R[4] |
| 0x0035 | MATH_COS | R[4]=radians→R[4] |
| 0x0036 | MATH_TAN | R[4]=radians→R[4] |
| 0x0037 | MATH_LOG | R[4]=value→R[4] |
| 0x0038 | MATH_ABS | R[4]=value→R[4] |

---

## Opcode 字节值

所有 opcode 的 hex 值由 `OpCode` 枚举定义，Builder 内部使用 `(byte)OpCode.XXX`，用户不需要手动编码。完整枚举见 `IronST.Runtime.Shared.Enums.cs`。

---

## 错误类型

| RuntimeErrorType | 触发条件 |
|-----------------|----------|
| TypeError | IINC/IDEC 非 Int64，Compare 不兼容类型，SGET/SSET 目标非 Struct，AsInt64/AsFloat64 对 Struct |
| DivideByZero | IDIV/FDIV 除零 |
| SlotOverflow | 栈/全局/字段越界，StructHeap Arena 越界 |
| StackOverflow | PUSH/FRAME 导致 SP<0 |
| StackUnderflow | POP/POPN 导致 SP>StackSize |
| IllegalOpcode | 未知 opcode，未知 SYSCALL，HCALL 未注册 |

---

## 内存管理

StructHeap 使用 bump allocator（只分配，不单个释放）。BcValue(Struct) 不持有堆引用（RefValue=null），零 GC 压力。MOV 对 Struct 为浅拷贝（只复制 slotIndex+typeId），不复制 Arena 字段。热重载 `vm.Reset(module)` 时 Arena 整体清空。

### StructHeap API（宿主侧）

```csharp
void RegisterType(byte typeId, int fieldCount)       // 编译期注册
int  Alloc(byte typeId)                               // 分配实例，返回 slotIndex
BcValue GetField(int slotIndex, byte fieldOffset)     // 读字段
void SetField(int slotIndex, byte fieldOffset, BcValue value)  // 写字段
void DeepCopy(int srcSlot, int dstSlot, int fieldCount)        // 深拷贝
void Reset()                                          // 热重载清空
```

DeepCopy 是宿主侧工具方法，ISA 中没有对应指令。典型场景：函数参数传递结构体值语义时，宿主代码调用 DeepCopy 创建独立副本。

---

## BytecodeBuilder — 底层构建器

命名空间 `IronST.Runtime.Dev`。用于手动发射单条指令。

### 寄存器常量

```csharp
public const byte ZERO=0, SP=1, FP=2, LR=3;
public const byte A0=4, A1=5, A2=6, A3=7;
public const byte R8=8, R9=9, R10=10, R11=11;
public const byte R16=16, R17=17, R18=18, R19=19;
public const byte R20=20, R21=21, R22=22, R23=23;
```

R[12]–R[15]、R[24]–R[255] 无预定义常量，由 AllocTemp() 自动管理。

### 常量池

```csharp
var b = new BytecodeBuilder();
int idxI = b.AddConst(42L);       // 添加 Int64
int idxF = b.AddConst(3.14);      // 添加 Float64
int idxS = b.AddString("hello");  // 添加 String
```

统一常量池，所有类型共用索引空间。每次调用都追加新条目，不做去重。`LDC rd, idx` 加载任意类型。

### 发射指令

```csharp
b.LNIL(10);
b.LTRUE(10);
b.LFALSE(10);
b.LDC(10, idxS);
b.LDC(10, idxI);

b.MOV(8, 9);
b.IADD(8, 9, 10);
b.FADD(8, 9, 10);
b.CMP(8, 9, 10);
b.IEQ(8, 9, 10);

b.FRAME(3);
b.SETL(4, 0);
b.GETL(8, 0);
b.LEAVE();
b.RET();

b.CALL(100);
b.JMP(200);
b.BNZ(8, -5);

b.HCALL(HostCallIds.PRINT_STR);
b.SYSCALL((byte)SyscallFunc.EXIT);
b.HALT();

b.SNEW(8, typeId:1, count:3);
b.SGET(9, 8, 0);
b.SSET(10, 8, 1);
```

### 构建与保存

```csharp
BytecodeModule module = b.Build();         // 入口 PC=0
BytecodeModule module = b.BuildModule(42); // 入口 PC=42
b.SaveTo("out.irst");                     // 保存到文件
```

---

## BytecodeAssembler — 高级构建器

包装 Builder，提供标签、函数、全局变量、临时寄存器。

### 标签系统

```csharp
var asm = new BytecodeAssembler();

asm.Label("loop");
asm.Bnz(8, "loop");     // 自动计算相对偏移
asm.Jmp("end");         // 自动 Patch 绝对地址
asm.Label("end");
```

支持前向引用，`Build()` 时自动回填。

### 函数定义与调用

```csharp
asm.DefFunc("add", paramCount: 2, extraLocals: 1, () => {
    asm.GetParam(8, 0);    // R[8] = 参数 0
    asm.GetParam(9, 1);    // R[9] = 参数 1
    byte sum = asm.IAdd(8, 9);
    asm.Raw.MOV(asm.A0, sum);
    asm.FreeTemp(sum);
    // 自动 LEAVE + RET
});
```

`DefFunc` 自动：FRAME → 保存 A0–A3 到局部变量 → 执行 body → LEAVE → RET。

#### DefFunc 栈帧布局

`DefFunc("f", paramCount:2, extraLocals:3)` 生成的帧：

```
slot 0 = A0（参数 0，由 DefFunc 自动 SETL 保存）
slot 1 = A1（参数 1，由 DefFunc 自动 SETL 保存）
slot 2 = extraLocal 0
slot 3 = extraLocal 1
slot 4 = extraLocal 2
```

`GetParam(rd, 0)` → GETL rd, 0 → 读 slot 0。
`GetLocal(rd, 2)` → GETL rd, 2 → 读 slot 2（extraLocal 0）。

规则：paramCount 个 slot 存参数（0 到 paramCount-1），extraLocals 个 slot 紧随其后（paramCount 到 paramCount+extraLocals-1）。

#### 函数调用约定

调用前手动 MOV 参数到 A0–A3（R[4]–R[7]），再 CallFunc：

```csharp
byte arg1 = asm.Load(10L);
byte arg2 = asm.Load(20L);
asm.Raw.MOV(asm.A0, arg1);   // 第 1 参数 → R[4]
asm.Raw.MOV(asm.A1, arg2);   // 第 2 参数 → R[5]
asm.FreeTemp(arg1);
asm.FreeTemp(arg2);
asm.CallFunc("add");
// 返回后 asm.A0（R[4]）即为返回值
```

参数最多 4 个（A0–A3）。DefFunc paramCount 限制 0–4。

简化版：

```csharp
asm.DefFunc("noop", () => { ... });                    // 无参数无额外局部变量
asm.DefFunc("f", paramCount: 2, () => { ... });        // 无额外局部变量
```

### 全局变量

```csharp
asm.DeclareGlobal("counter");      // 声明，自动分配 slot
asm.GetGlobal(8, "counter");       // R[8] = globals["counter"]
asm.SetGlobal(8, "counter");       // globals["counter"] = R[8]
```

### 临时寄存器

```csharp
byte r = asm.AllocTemp();  // 从 R[8] 开始自动分配
asm.FreeTemp(r);           // 释放供复用
```

### 便捷方法

```csharp
// 加载（自动分配寄存器，使用 LDC + 常量池）
byte ri = asm.Load(42L);        // Int64
byte rf = asm.Load(3.14);       // Float64
byte rs = asm.Load("hello");    // String
byte rb = asm.Load(true);       // Bool → LTRUE
byte rb2 = asm.Load(false);     // Bool → LFALSE
byte rn = asm.LoadNil();        // Void → LNIL

// 算术（自动分配目标寄存器）
byte sum  = asm.IAdd(r1, r2);
byte diff = asm.ISub(r1, r2);
byte prod = asm.IMul(r1, r2);
byte quot = asm.IDiv(r1, r2);
byte fsum = asm.FAdd(r1, f2);

// 比较
byte cmp  = asm.Compare(r1, r2);  // CMP → -1/0/1
byte eq   = asm.IEq(r1, r2);     // Int64 相等 → 1/0
byte ne   = asm.INe(r1, r2);     // Int64 不等 → 1/0
byte feq  = asm.FEq(r1, f2);    // Float64 相等 → 1/0

// 结构体
byte obj = asm.StructNew(typeId: 1, fieldCount: 3);
byte val = asm.StructGet(obj, 0);
asm.StructSet(val, obj, 1);

// 打印（内部使用 HCALL）
asm.Print("Hello");        // 打印字符串常量
asm.PrintInt(r1);          // 打印整数寄存器
asm.PrintStr(r1);          // 打印字符串寄存器
asm.PrintLn();             // 换行（Load(10L) → HCALL PRINT_CHAR）

// 控制
asm.Exit();                // SYSCALL EXIT
asm.Break();               // BRK 断点
```

### 底层透传

```csharp
asm.Raw.HCALL(0x0001);     // 访问底层 Builder 发射任意指令
int pc = asm.PC;            // 当前代码字节数
int idx = asm.AddConst(100L);
int idf = asm.AddConst(2.718);
int ids = asm.AddString("world");
```

### 构建

```csharp
BytecodeModule module = asm.Build();
BytecodeModule module = asm.BuildModule(42);
asm.SaveTo("out.irst");
```

---

## VMContext（HCALL 回调参数类型）

命名空间 `IronST.Runtime.Shared`

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

## HostBridge

命名空间 `IronST.Runtime.Shared`

```csharp
void Register(ushort id, Func<VMContext, BcValue> handler)
```

---

## BytecodeModule 属性

命名空间 `IronST.Runtime.Shared`

```
EntryPC       : uint
Code          : byte[]
ConstantPool  : BcValue[]
GlobalCount   : int
DebugInfo     : byte[]?   // 可空的字节数组，预留，格式未定义
```

---

## VMOptions

```csharp
HostBridge?           : HostBridge  — 宿主方法注册表，null 则创建空实例
StackSize             : int         — 栈大小（BcValue 槽位数），默认 65536
StructHeapCapacity    : int         — 结构体 Arena 总槽位，默认 100000
```

VMDevOptions 继承 VMOptions，额外要求：
```csharp
DebugSession          : VMDebugSession  // required
```

---

## 运行流程

### 发布模式（IronST.Runtime）

```csharp
using IronST.Runtime;

var vm = new VM(module, options);
vm.Run();
```

```csharp
public sealed class VM : VMBase
{
    public VM(BytecodeModule module, VMOptions? options = null)
        : base(module, options ?? new VMOptions(), hooks: null) { }
}
```

### 开发模式（IronST.Runtime.Dev）

```csharp
var session = new VMDebugSession();
var devOptions = new VMDevOptions { HostBridge = bridge, DebugSession = session };
var vm = new DebugVM(module, devOptions);
vm.Run();
```

```csharp
public sealed class DebugVM : VMBase
{
    public DebugVM(BytecodeModule module, VMDevOptions options)
        : base(module, options, hooks: options.DebugSession)
    {
        // options.DebugSession 既是 hooks 参数，也被内部持有
        // DebugSession.Attach(this) 在构造函数内自动调用
    }

    public VMDebugSession DebugSession { get; }
}
```

### VMBase 生命周期方法

```csharp
Run()           — Init/Paused → Running，从当前 PC 执行
Pause()         — 设 Paused
Resume()        — 仅用于 BRK 断点恢复，跳过 BRK 字节后继续
Reset(module)   — 重置所有状态，回 Init，Arena 清空
Dispose()       — 释放
```

Paused 恢复方式：
- BRK 暂停 → `Resume()`（跳过 BRK 字节）或 `Run()`（不跳过，会再次命中 BRK）
- VMCALL 暂停 → `Run()`（从 PC+2 继续）
- VMRET 暂停 → `Run()`（从 PC+1 继续）
- 单步/断点暂停 → `Run()`

Halted 后恢复：`vm.Reset(module)` → 回到 Init → `vm.Run()`。

---

## 错误处理

发布模式（hooks=null）：Run() 抛出 RuntimeErrorException，同时 State 设为 Halted。

开发模式（hooks!=null）：State 设为 Paused，调用 hooks.OnError(ex)。

调用链：
```
VM 执行出错
→ hooks.OnError(ex)                          // IVMDebugHooks 接口方法
→ VMDebugSession.OnError 内部判断是否 break
→ 触发 OnDebugEvent(new RuntimeErrorEvent)   // 宿主事件回调
```

宿主获取错误：
```csharp
try { vm.Run(); }
catch (RuntimeErrorException ex) { ... }
// 或
vm.LastError
```

---

## IVMDebugHooks 接口

```csharp
public interface IVMDebugHooks
{
    bool PreStep(VMBase vm, uint pc);        // 每条指令前，返回 false 暂停
    void OnBreak(VMBase vm, uint pc);        // BRK 命中
    void OnError(RuntimeErrorException ex);  // 运行时错误
}
```

### PreStep 执行语义

PreStep 在每条指令执行前调用。
- 返回 true → 执行该指令，PC 正常前进。
- 返回 false → 该指令不执行，State 设为 Paused，Run() 返回。恢复后（Run）会再次调用 PreStep，此时返回 true 才执行该指令。

单步/断点暂停均通过 return false 实现，指令尚未执行。

---

## 调试 (Dev 包)

### 断点

```csharp
// 地址断点
var bp = session.AddAddressBreakpoint(0x10);

// 条件断点
session.AddConditionalBreakpoint(0x20, ctx => ctx.ReadReg(8).IntValue > 100);

// 命中计数断点
session.AddHitCountBreakpoint(0x30, hitCount: 5);

// 管理
session.RemoveBreakpoint(bp.Id);
session.ClearBreakpoints();
```

### 单步

```csharp
session.SetSingleStep();  // Step Into
session.SetStepOver();    // Step Over（CALL 时不进入，到 RET 后暂停）
```

### 监视与错误断点

```csharp
session.WatchedGlobalSlots.Add(0);
session.WatchedLocalSlots.Add(1);
session.BreakOnTypeError = true;
session.BreakOnDivideByZero = true;
session.BreakOnSlotOverflow = true;
```

### 跟踪日志

```csharp
session.TraceEnabled = true;
session.MaxTraceEntries = 50_000;
IReadOnlyList<DebugTraceEntry> log = session.TraceLog;
```

每条记录含 PC、Opcode、反汇编字符串、寄存器快照。

### 调试事件

```csharp
session.OnDebugEvent += evt => {
    switch (evt) {
        case BreakpointHitEvent bp:
            Console.WriteLine($"Breakpoint #{bp.BreakpointId} at 0x{bp.PC:X}");
            break;
        case StepCompleteEvent step:
            Console.WriteLine($"Step at 0x{step.PC:X}");
            break;
        case RuntimeErrorEvent err:
            Console.WriteLine($"Error: {err.Error.ErrorType} at 0x{err.PC:X}");
            break;
    }
};
```

### 源码映射

```csharp
var sm = new SourceMap();
sm.SourceFiles.Add("main.st");
sm.Mappings.Add(new SourceMap.SourceMapping(PC: 0, FileIndex: 0, Line: 1, Column: 0));
session.SourceMap = sm;

var loc = session.SourceMap.FindByPC(pc); // 二分查找
```

### TraceLogger（独立工具）

```csharp
var trace = new TraceLogger(watchRegs: new[] { 0, 1, 2, 3, 4, 8, 9 });
// 在 IVMDebugHooks.PreStep 中调用 trace.AddEntry(pc, vm)
string dump = trace.Dump();
trace.SaveToFile("trace.log");
```

### 反汇编

```csharp
string asm = Disassembler.Disassemble(vm.Code, pc);
// → "IADD R8, R9, R10"
// 未知: "??? 0xNN [xB]"
// 截断: "<trunc>"
// 越界: "<eof>"
```

---

## .irst 文件格式

```
[Header 28 bytes]
  Magic: 0x49525354 ("IRST")
  VersionMajor: 1
  VersionMinor, Patch, Flags: 各 1 字节
  EntryPC: uint32
  CodeSize: uint32 (上限 16MB)
  ConstPoolCount: int32
  GlobalCount: int32
  DebugInfoSize: uint32

[Code: CodeSize bytes]

[ConstantPool: ConstPoolCount entries]
  每条: [tag:1][pad:7][data]
  tag=1(Int64):  [int64:8]
  tag=2(Float64): [float64:8]
  tag=3(String): [byte_len:4][utf8][padding to 8-byte align]
  单条字符串上限 64KB

[DebugInfo: DebugInfoSize bytes (optional)]
```

---

## 完整示例

```csharp
using IronST.Runtime;
using IronST.Runtime.Shared;
using IronST.Runtime.Dev;

// ── 1. 构建字节码 ──
var asm = new BytecodeAssembler();
asm.DeclareGlobal("result");

asm.DefFunc("add", paramCount: 2, () => {
    asm.GetParam(8, 0);
    asm.GetParam(9, 1);
    byte sum = asm.IAdd(8, 9);
    asm.Raw.MOV(asm.A0, sum);
    asm.FreeTemp(sum);
});

// 主流程
byte a = asm.Load(10L);
byte b = asm.Load(20L);
asm.Raw.MOV(asm.A0, a);
asm.Raw.MOV(asm.A1, b);
asm.FreeTemp(a);
asm.FreeTemp(b);
asm.CallFunc("add");

asm.SetGlobal(asm.A0, "result");
asm.Print("Result: ");
asm.PrintInt(asm.A0);
asm.PrintLn();
asm.Exit();

BytecodeModule module = asm.Build();

// ── 2. 注册宿主方法 ──
var bridge = new HostBridge();
bridge.Register(HostCallIds.PRINT_STR, ctx => {
    Console.Write(ctx.ReadReg(4).StringValue ?? "");
    return BcValue.Void;
});
bridge.Register(HostCallIds.PRINT_INT, ctx => {
    Console.Write(ctx.ReadReg(4).IntValue);
    return BcValue.Void;
});
bridge.Register(HostCallIds.PRINT_CHAR, ctx => {
    Console.Write((char)ctx.ReadReg(4).IntValue);
    return BcValue.Void;
});

// ── 3a. 发布模式运行 ──
var options = new VMOptions { HostBridge = bridge };
var vm = new VM(module, options);
vm.Run();

// ── 3b. 或开发模式运行 ──
var session = new VMDebugSession();
session.SetSingleStep();
session.OnDebugEvent += evt => {
    if (evt is StepCompleteEvent s)
        Console.WriteLine($"Step at 0x{s.PC:X}");
};
var devOptions = new VMDevOptions { HostBridge = bridge, DebugSession = session };
var dvm = new DebugVM(module, devOptions);
dvm.Run();

// ── 4. 热重载 ──
vm.Reset(BytecodeModule.Load(newBytes));
vm.Run();
```