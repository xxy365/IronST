# IronST.IRBuilder 使用文档

> 纯 C#、零第三方依赖的 LLVM IR 工具链。支持通过编程方式构建 LLVM IR、发射 `.ll` 文本、解析 `.ll` 文件，以及通过管道调用 LLVM `opt` 工具进行优化。

---

## 目录

- [1. 快速开始](#1-快速开始)
- [2. 项目结构](#2-项目结构)
- [3. 类型系统](#3-类型系统)
- [4. 构建 IR（Builder API）](#4-构建-irbuilder-api)
- [5. 发射 .ll 文本（Emitter）](#5-发射-ll-文本emitter)
- [6. 解析 .ll 文本（Parser）](#6-解析-ll-文本parser)
- [7. 往返工作流（Build → Emit → Parse → Emit）](#7-往返工作流)
- [8. Debug 信息支持](#8-debug-信息支持)
- [9. 调用外部 opt（OptRunner）](#9-调用外部-optoptrunner)
- [10. CLI 工具 IronST.Opt](#10-cli-工具-ironstopt)
- [11. 完整 Demo 集锦](#11-完整-demo-集锦)
- [12. API 参考速查](#12-api-参考速查)

---

## 1. 快速开始

### 1.1 环境要求

| 项目 | 要求 |
|---|---|
| .NET SDK | 8.0+（推荐 10.0） |
| 第三方依赖 | **无** |
| LLVM opt（可选） | LLVM 17+ 已全面支持 opaque pointer |

### 1.2 安装

```bash
git clone <repo-url>
cd IronST.IRBuilder
dotnet build
```

### 1.3 30 秒示例

```csharp
using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;

// 构建一个简单的 add 函数
var mod = new Module("demo.ll");

var ft = new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 });
var func = new Function("add", ft);
func.Parameters[0].Name = "a";
func.Parameters[1].Name = "b";

var entry = new BasicBlock("entry");
func.AppendBasicBlock(entry);

var sum = BinaryOperator.CreateAdd(func.Parameters[0], func.Parameters[1], "sum");
entry.AddInstruction(sum);
entry.AddInstruction(new ReturnInst(sum));

mod.AddFunction(func);

// 输出 .ll 文本
var ir = new LLEmitter().Emit(mod);
Console.WriteLine(ir);
```

输出：

```llvm
source_filename = "demo.ll"
define i32 @add(i32 %a, i32 %b) {
entry:
  %sum = add i32 %a, %b
  ret i32 %sum
}
```

---

## 2. 项目结构

```
IronST.IRBuilder/
├── src/
│   ├── IronST.IRBuilder/           # 核心库
│   │   ├── IR/                     # IR 对象模型
│   │   │   ├── Module.cs           #   顶层模块
│   │   │   ├── Function.cs         #   函数定义/声明
│   │   │   ├── BasicBlock.cs       #   基本块
│   │   │   ├── Instruction.cs      #   所有指令类型
│   │   │   ├── Type.cs             #   类型系统
│   │   │   ├── Value.cs            #   SSA 值基类
│   │   │   ├── Constant.cs         #   常量类型
│   │   │   ├── GlobalVariable.cs   #   全局变量
│   │   │   ├── IRBuilder.cs        #   指令构建器
│   │   │   └── Enums.cs            #   枚举定义
│   │   ├── Parser/                 # .ll 解析器
│   │   │   ├── LLParser.cs         #   语法分析器
│   │   │   ├── Lexer.cs            #   词法分析器
│   │   │   └── Token.cs            #   词法单元
│   │   ├── Emitter/                # .ll 输出器
│   │   │   └── LLEmitter.cs        #   IR → 文本
│   │   ├── OptRunner/              # 调用外部 opt
│   │   │   └── OptRunner.cs        #   stdin/stdout 管道
│   │   └── tools/opt/              # 集成的 opt 二进制
│   │       ├── win-x64/
│   │       ├── linux-x64/
│   │       ├── linux-arm64/
│   │       ├── osx-arm64/
│   │       └── osx-x64/
│   └── IronST.Opt/                 # CLI 前端
│       └── Program.cs
├── tests/
│   └── IronST.IRBuilder.Tests/     # 单元测试
└── tools/
    ├── download-opt.ps1            # Windows 下载脚本
    └── download-opt.sh             # Linux/macOS 下载脚本
```

---

## 3. 类型系统

LLVM IR 的所有类型在 `IronST.IRBuilder.IR` 命名空间下表示。

### 3.1 预定义类型

```csharp
// 整数类型
IrType.Int1      // i1
IrType.Int8      // i8
IrType.Int16     // i16
IrType.Int32     // i32
IrType.Int64     // i64
IrType.Int128    // i128

// 浮点类型
IrType.Half      // half
IrType.Float     // float
IrType.Double    // double

// 特殊类型
IrType.Void      // void
IrType.Label     // label
IrType.Metadata  // metadata
IrType.Token     // token

// Opaque Pointer（LLVM 22 强制）
IrType.Pointer   // ptr
```

### 3.2 自定义整数类型

```csharp
// 任意位宽
var i24 = new IntegerType(24);   // i24
Console.WriteLine(i24.ToIRString()); // "i24"
```

### 3.3 浮点类型

```csharp
new FloatType(FloatKind.Half);      // half
new FloatType(FloatKind.BFloat);    // bfloat
new FloatType(FloatKind.Float);     // float
new FloatType(FloatKind.Double);    // double
new FloatType(FloatKind.X86_FP80);  // x86_fp80
new FloatType(FloatKind.FP128);     // fp128
new FloatType(FloatKind.PPC_FP128); // ppc_fp128
```

### 3.4 指针类型（Opaque Pointer）

```csharp
// LLVM 22 使用 opaque pointer，不再有 i32*、float* 等旧语法
IrType.Pointer                       // ptr
new PointerType(1)                   // ptr addrspace(1)
new PointerType(0)                   // ptr（等价于 IrType.Pointer）
```

### 3.5 函数类型

```csharp
// i32 (i32, i32)
new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 });

// void ()  —— 无参数
new FunctionType(IrType.Void, Array.Empty<IrType>());

// i32 (ptr, ...)  —— 可变参数（如 printf）
new FunctionType(IrType.Int32, new[] { IrType.Pointer }, isVarArg: true);
```

### 3.6 数组类型

```csharp
// [10 x i32]
new ArrayType(IrType.Int32, 10);

// [3 x [4 x float]]  —— 嵌套数组
new ArrayType(new ArrayType(IrType.Float, 4), 3);
```

### 3.7 向量类型

```csharp
// <4 x float>  —— 固定长度向量
new VectorType(4, IrType.Float);

// <vscale x 4 x i32>  —— 可伸缩向量（SVE/RISC-V V）
new VectorType(4, IrType.Int32, isScalable: true);
```

### 3.8 结构体类型

```csharp
// { i32, float, ptr }  —— 字面结构体
new StructType(new IrType[] { IrType.Int32, IrType.Float, IrType.Pointer });

// <{ i8, i32 }>  —— 打包结构体
new StructType(new IrType[] { IrType.Int8, IrType.Int32 }, isPacked: true);

// %MyStruct  —— 命名结构体（需要在 Module 中注册）
mod.AddNamedType("MyStruct", new StructType(new IrType[] { IrType.Int32, IrType.Float }));
// 输出: %MyStruct = type { i32, float }
```

---

## 4. 构建 IR（Builder API）

### 4.1 Module（模块）

模块是 IR 的顶层容器：

```csharp
var mod = new Module("my_module.ll");
mod.TargetTriple = "x86_64-unknown-linux-gnu";
mod.TargetDataLayout = "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128";
```

### 4.2 Function（函数）

```csharp
// 函数类型
var ft = new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 });

// 函数定义
var func = new Function("add", ft, LinkageType.External);
func.Parameters[0].Name = "a";
func.Parameters[1].Name = "b";

// 添加到模块
mod.AddFunction(func);

// 函数声明（无函数体）
mod.AddFunction(new Function("printf",
    new FunctionType(IrType.Int32, new[] { IrType.Pointer }, isVarArg: true)));
```

### 4.3 BasicBlock（基本块）

```csharp
var entry = new BasicBlock("entry");
var loop  = new BasicBlock("loop");
var exit  = new BasicBlock("exit");

func.AppendBasicBlock(entry);
func.AppendBasicBlock(loop);
func.AppendBasicBlock(exit);
```

### 4.4 手动添加指令

```csharp
// add i32 %a, %b
var add = BinaryOperator.CreateAdd(func.Parameters[0], func.Parameters[1], "sum");
entry.AddInstruction(add);

// ret i32 %sum
entry.AddInstruction(new ReturnInst(add));
```

### 4.5 使用 IRBuilder

`IRBuilder` 提供便捷的指令构建方法：

```csharp
var entry = new BasicBlock("entry");
func.AppendBasicBlock(entry);

var builder = new IRBuilder(entry);

// %x = alloca i32, align 4
var x = builder.CreateAlloca(IrType.Int32, n: "x", align: 4);

// store i32 42, ptr %x
builder.CreateStore(new ConstantInt(IrType.Int32, 42), x);

// %val = load i32, ptr %x, align 4
var val = builder.CreateLoad(IrType.Int32, x, n: "val", align: 4);

// ret i32 %val
builder.CreateRet(val);
```

在多个基本块之间切换：

```csharp
var builder = new IRBuilder(entry);

// 在 entry 中构建指令
var cmp = builder.CreateICmp(ICmpPredicate.Slt, ...);
builder.CreateCondBr(cmp, thenBB, elseBB);

// 切换到 thenBB
builder.InsertBlock = thenBB;
builder.CreateBr(mergeBB);

// 切换到 mergeBB
builder.InsertBlock = mergeBB;
var phi = builder.CreatePhi(IrType.Int32, "result");
phi.AddIncoming(...);
builder.CreateRet(phi);
```

### 4.6 全局变量

```csharp
// 全局变量
var init = new ConstantInt(IrType.Int32, 0);
var gv = new GlobalVariable("counter", IrType.Int32, false, LinkageType.Internal, init);
gv.Alignment = 4;
mod.AddGlobalVariable(gv);

// 全局常量字符串（自动追加 null 终止符）
var str = mod.AddGlobalString(".str", "Hello World!");
// 输出: @.str = private unnamed_addr constant [13 x i8] c"Hello World!\00", align 1
```

### 4.7 常量

```csharp
new ConstantInt(IrType.Int32, 42);           // i32 42
new ConstantInt(IrType.Int64, -1);            // i64 -1
new ConstantFP(IrType.Float, 3.14);           // float 0x40091EB860000000
new ConstantFP(IrType.Double, 2.718);         // double 2.718
new ConstantPointerNull();                     // null（ptr 类型）
UndefValue.Get(IrType.Int32);                 // undef
PoisonValue.Get(IrType.Int64);               // poison
new ConstantAggregateZero(IrType.Int32);      // zeroinitializer

// 数组常量 [3 x i32] [i32 1, i32 2, i32 3]
new ConstantArray(
    new ArrayType(IrType.Int32, 3),
    new Constant[] {
        new ConstantInt(IrType.Int32, 1),
        new ConstantInt(IrType.Int32, 2),
        new ConstantInt(IrType.Int32, 3)
    });

// 结构体常量 { i32, float } { i32 1, float 2.0 }
new ConstantStruct(
    new StructType(new IrType[] { IrType.Int32, IrType.Float }),
    new Constant[] {
        new ConstantInt(IrType.Int32, 1),
        new ConstantFP(IrType.Float, 2.0)
    });
```

---

## 5. 发射 .ll 文本（Emitter）

```csharp
using IronST.IRBuilder.Emitter;

var emitter = new LLEmitter();
string llText = emitter.Emit(module);

// 输出到控制台
Console.Write(llText);

// 写入文件
File.WriteAllText("output.ll", llText);
```

### 输出示例

```llvm
source_filename = "example.ll"
target triple = "x86_64-unknown-linux-gnu"

%Point = type { i32, float }

@.str = private unnamed_addr constant [14 x i8] c"Hello, LLVM!\00", align 1
@counter = internal global i32 0, align 4

declare i32 @printf(ptr, ...)

define i32 @add(i32 %a, i32 %b) {
entry:
  %sum = add i32 %a, %b
  ret i32 %sum
}

define i32 @abs(i32 %x) {
entry:
  %cmp = icmp slt i32 %x, 0
  br i1 %cmp, label %neg, label %end
neg:
  %neg_val = sub i32 0, %x
  br label %end
end:
  %result = phi i32 [ %x, %entry ], [ %neg_val, %neg ]
  ret i32 %result
}
```

---

## 6. 解析 .ll 文本（Parser）

```csharp
using IronST.IRBuilder.Parser;

var parser = new LLParser();

// 从字符串解析
var module = parser.Parse(llText);

// 从文件解析
var module = parser.Parse(File.ReadAllText("input.ll"));
```

### 访问解析结果

```csharp
// 遍历函数
foreach (var func in module.Functions)
{
    Console.WriteLine($"函数: @{func.Name}");
    Console.WriteLine($"  声明: {func.IsDeclaration}");
    Console.WriteLine($"  参数: {func.Parameters.Count}");

    // 遍历基本块
    foreach (var bb in func.BasicBlocks)
    {
        Console.WriteLine($"  块: {bb.Name}");

        // 遍历指令
        foreach (var inst in bb.Instructions)
        {
            Console.WriteLine($"    {inst.GetType().Name}: {inst}");

            // 访问操作数
            for (int i = 0; i < inst.Operands.Count; i++)
            {
                Console.WriteLine($"      操作数[{i}]: {inst.Operands[i]}");
            }
        }
    }
}

// 遍历全局变量
foreach (var gv in module.GlobalVariables)
{
    Console.WriteLine($"全局: @{gv.Name} (constant={gv.IsConstant})");
}

// 遍历命名类型
foreach (var nt in module.NamedTypes)
{
    Console.WriteLine($"类型: %{nt.Name} = {nt.Body?.ToIRString()}");
}
```

---

## 7. 往返工作流

Builder → Emitter → Parser → Emitter，验证输出一致性：

```csharp
using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using IronST.IRBuilder.Parser;

// === 1. 用 API 构建 ===
var mod = new Module("roundtrip.ll");
mod.TargetTriple = "x86_64-unknown-linux-gnu";

var ft = new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 });
var func = new Function("multiply", ft);
func.Parameters[0].Name = "x";
func.Parameters[1].Name = "y";

var entry = new BasicBlock("entry");
func.AppendBasicBlock(entry);

var builder = new IRBuilder(entry);
var result = builder.CreateMul(func.Parameters[0], func.Parameters[1], "product");
builder.CreateRet(result);

mod.AddFunction(func);

// === 2. 发射 ===
var emitter = new LLEmitter();
var ll1 = emitter.Emit(mod);
Console.WriteLine("=== 第一次发射 ===");
Console.WriteLine(ll1);

// === 3. 解析 ===
var parser = new LLParser();
var parsed = parser.Parse(ll1);

// === 4. 再次发射 ===
var ll2 = emitter.Emit(parsed);
Console.WriteLine("=== 第二次发射 ===");
Console.WriteLine(ll2);

// === 5. 验证一致性 ===
Console.WriteLine($"一致: {ll1 == ll2}");
```

---

## 8. Debug 信息支持

项目支持 LLVM IR 中的 debug metadata 透传与基本语义解析。所有 debug 信息在构建、发射、解析、opt 优化的完整流水线中全程在内存中流转，不落盘。

### 8.1 架构概述

debug 支持通过新增文件实现，对现有 IR 模型、Parser、Emitter 的核心逻辑改动极小：

```
现有文件（仅加插槽）            新增文件
┌──────────────────┐          ┌──────────────────────┐
│ Module.cs        │ +1 行    │ IR/MetadataNode.cs   │  MDString, MDTuple
│ Instruction.cs   │ +5 行    │ IR/MetadataTable.cs  │  编号/有名 metadata 存储
│ Function.cs      │ +8 行    │ IR/DebugInfo.cs      │  DIFile, DILocation 等
│ LLParser.cs      │ +钩子    │ Parser/MetadataParser.cs │  metadata 解析逻辑
│ LLEmitter.cs     │ +输出    │ Emitter/MetadataEmitter.cs │ metadata 输出逻辑
└──────────────────┘          └──────────────────────┘
```

### 8.2 数据模型

#### MetadataNode（metadata 节点基类）

所有 metadata 节点的基类，对应 `.ll` 中的 `!0 = ...`。

```csharp
// metadata 字符串，对应 !"hello"
var s = new MDString("hello");

// metadata 元组，对应 !{!0, i32 1, !1}
var t = new MDTuple(new object?[] { 0, "key", new MDString("val") });

// 通过 Id 引用
t.Id = 5; // 对应 !5 = !{...}
```

#### MetadataTable（metadata 表）

存储模块中所有 metadata 定义，挂载在 `Module.Metadata` 上：

```csharp
var mod = new Module();

// 编号 metadata：!0 = ...
mod.Metadata.SetNode(0, new MDString("test"));
var node = mod.Metadata.GetNode(0); // MDString("test")

// 有名 metadata：!llvm.dbg.cu = ...
mod.Metadata.SetNamed("llvm.dbg.cu", new MDTuple(new object?[] { 0 }));
var cu = mod.Metadata.GetNamed("llvm.dbg.cu");

// 遍历
foreach (var (id, md) in mod.Metadata.All) { ... }
foreach (var (name, md) in mod.Metadata.AllNamed) { ... }
```

#### DebugInfo 类型（DI* 节点）

继承自 `MetadataNode`，描述源码级调试信息：

| 类 | 对应 LLVM | 用途 |
|---|---|---|
| `DIFile` | `!DIFile(filename:, directory:)` | 源文件路径 |
| `DICompileUnit` | `!DICompileUnit(language:, file:, ...)` | 编译单元顶层信息 |
| `DISubprogram` | `!DISubprogram(name:, line:, ...)` | 函数元信息 |
| `DILocation` | `!DILocation(line:, column:, scope:)` | 源码位置（行、列、所属函数） |
| `DILexicalBlock` | `!DILexicalBlock(scope:, line:, column:)` | 作用域块 |
| `DIBasicType` | `!DIBasicType(name:, size:, encoding:)` | 基础类型（int、float） |
| `DISubroutineType` | `!DISubroutineType(types:)` | 函数签名类型 |

它们构成一棵树：

```
DICompileUnit (一次编译)
 ├─ File: DIFile               源文件路径
 └─ Subprograms: [DISubprogram]
      ├─ Name, Line             函数名、定义行号
      ├─ File: DIFile           所在文件
      └─ 指令上挂 DILocation
           ├─ Line, Column      第几行第几列
           └─ Scope: DISubprogram  属于哪个函数
```

### 8.3 指令级 metadata 附加

每条指令可以挂载多个 metadata 附加项：

```csharp
// 构建
var dbg = new DILocation { Line = 10, Column = 5 };
mod.Metadata.SetNode(3, dbg);

var ret = new ReturnInst(new ConstantInt(IrType.Int32, 0));
ret.SetMetadata("dbg", dbg);

// 查询
if (ret.MetadataAttachments != null)
{
    var loc = (DILocation)ret.MetadataAttachments["dbg"];
    Console.WriteLine($"行: {loc.Line}, 列: {loc.Column}");
}
```

支持的附加项类型：

| kind | 含义 |
|------|------|
| `dbg` | 位置信息（最常用） |
| `tbaa` | 类型别名分析 |
| `range` | 值范围 |

### 8.4 函数级 metadata

函数签名后也可以挂载 metadata：

```csharp
var func = new Function("main", ft);
var dbg = new DILocation { Line = 1, Column = 1 };
mod.Metadata.SetNode(4, dbg);
func.SetMetadata("dbg", dbg);
```

对应 `.ll` 输出：

```llvm
define i32 @main() !dbg !4 {
...
```

### 8.5 发射 debug 信息

`LLEmitter` 自动输出所有 metadata：

```csharp
var mod = new Module();

// 定义 metadata
var file = new DIFile { Filename = "main.c", Directory = "/home" };
mod.Metadata.SetNode(0, file);

var cu = new DICompileUnit { Language = 12, File = file, Producer = "clang" };
mod.Metadata.SetNode(1, cu);

mod.Metadata.SetNamed("llvm.dbg.cu", new MDTuple(new object?[] { 1 }));

// 构建函数（略）...

var ir = new LLEmitter().Emit(mod);
```

输出：

```llvm
define i32 @main() !dbg !4 {
entry:
  ret i32 0, !dbg !6
}

!0 = !{i32 12, !1}
!1 = !{..."main.c"..."/home"...}
...
!llvm.dbg.cu = !{!0}
```

metadata 段输出在所有函数定义之后，按编号顺序排列，有名 metadata 紧随其后。

### 8.6 解析 debug 信息

Parser 自动识别 `!N = ...` 定义和指令上的 `!dbg !N` 附加项：

```csharp
var ll = """
!0 = !DIFile(filename: "main.c", directory: "/home")
!1 = !DICompileUnit(language: DW_LANG_C99, file: !0)
!2 = !DISubprogram(name: "main", scope: !1, file: !0, line: 1)
!3 = !DILocation(line: 2, column: 3, scope: !2)

define i32 @main() !dbg !2 {
entry:
  ret i32 0, !dbg !3
}
""";

var mod = new LLParser().Parse(ll);

// 访问 metadata
var file = mod.Metadata.GetNode(0);       // MDTuple
var cu = mod.Metadata.GetNode(1);         // MDTuple

// 访问指令级 debug
var retInst = mod.Functions[0].BasicBlocks[0].Instructions[0];
var dbg = retInst.MetadataAttachments?["dbg"]; // MDTuple

// 访问函数级 debug
var func = mod.Functions[0];
var funcDbg = func.MetadataAttachments?["dbg"]; // MDTuple
```

### 8.7 往返一致性

debug 信息在 Build → Emit → Parse → Emit 往返中保持完整：

```csharp
// 构建（带 debug）
var mod = BuildModuleWithDebug();
var emitter = new LLEmitter();
var ll1 = emitter.Emit(mod);

// 解析
var parsed = new LLParser().Parse(ll1);

// 再次发射
var ll2 = emitter.Emit(parsed);

// ll1 和 ll2 包含相同的 metadata 结构
Assert.Contains("!dbg !", ll2);
```

### 8.8 opt 优化与 debug 信息

opt 对 debug metadata 的处理规则：

| 场景 | 结果 |
|------|------|
| 指令未被修改 | `!dbg` 保留 |
| 指令被内联 | `!dbg` 通过 `inlinedAt` 链保留，可追溯原始调用位置 |
| 指令被删除 | `!dbg` 随指令消失 |
| 指令被合并 | 保留其中一个 `!dbg` |
| 函数被整体内联 | metadata 完整保留，被调用者有 `inlinedAt` 链 |

完整流水线示例：

```csharp
using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using IronST.IRBuilder.OptRunner;
using IronST.IRBuilder.Parser;

// 1. 构建带 debug 信息的 IR
var mod = BuildModuleWithDebug();

// 2. 发射
var ll = new LLEmitter().Emit(mod);

// 3. opt 优化（debug 信息通过管道传递，不落盘）
var runner = new OptRunner();
string optimized = runner.RunAsText(ll, "-S -O2");

// 4. 解析优化后的 IR（debug 信息保留）
var optMod = new LLParser().Parse(optimized);

// 5. 验证 debug 信息还在
foreach (var func in optMod.Functions)
{
    if (func.MetadataAttachments?.ContainsKey("dbg") == true)
        Console.WriteLine($"@{func.Name} 保留了 debug 信息");

    foreach (var bb in func.BasicBlocks)
    {
        foreach (var inst in bb.Instructions)
        {
            if (inst.MetadataAttachments?.ContainsKey("dbg") == true)
                Console.WriteLine($"  {inst.GetType().Name} 保留了 debug 位置");
        }
    }
}
```

### 8.9 opt 相关参数

| 参数 | 作用 |
|------|------|
| （无特殊参数） | 默认保留 debug 信息 |
| `-strip-debug` | 主动去除所有 debug 信息 |
| `-debugify` | 给没有 debug 信息的 IR 注入合成 debug 信息（调试用） |

### 8.10 完整示例：构建带 debug 信息的程序

```csharp
using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;

var mod = new Module("debug_demo.ll");
mod.TargetTriple = "x86_64-unknown-linux-gnu";

// ---- 定义 debug metadata ----

// 源文件
var file = new DIFile { Filename = "main.c", Directory = "/home/user" };
mod.Metadata.SetNode(1, file);

// 编译单元
var cu = new DICompileUnit
{
    Language = 12, // DW_LANG_C99
    File = file,
    Producer = "IronST",
    IsOptimized = false,
    EmissionKind = "FullDebug"
};
mod.Metadata.SetNode(0, cu);

// 函数元信息
var sp = new DISubprogram
{
    Name = "add",
    Scope = cu,
    File = file,
    Line = 3,
    IsDefinition = true
};
mod.Metadata.SetNode(2, sp);

// 指令位置
var loc1 = new DILocation { Line = 4, Column = 12, Scope = sp };
mod.Metadata.SetNode(3, loc1);

var loc2 = new DILocation { Line = 5, Column = 3, Scope = sp };
mod.Metadata.SetNode(4, loc2);

// 有名 metadata
mod.Metadata.SetNamed("llvm.dbg.cu", new MDTuple(new object?[] { 0 }));

// ---- 构建函数 ----

var ft = new FunctionType(IrType.Int32, new[] { IrType.Int32, IrType.Int32 });
var func = new Function("add", ft);
func.Parameters[0].Name = "a";
func.Parameters[1].Name = "b";

var entry = new BasicBlock("entry");
func.AppendBasicBlock(entry);

var sum = BinaryOperator.CreateAdd(func.Parameters[0], func.Parameters[1], "sum");
sum.SetMetadata("dbg", loc1);
entry.AddInstruction(sum);

var ret = new ReturnInst(sum);
ret.SetMetadata("dbg", loc2);
entry.AddInstruction(ret);

// 函数级 debug
func.SetMetadata("dbg", sp);

mod.AddFunction(func);

// ---- 发射 ----
var ir = new LLEmitter().Emit(mod);
Console.WriteLine(ir);
```

输出：

```llvm
source_filename = "debug_demo.ll"
target triple = "x86_64-unknown-linux-gnu"

define i32 @add(i32 %a, i32 %b) !dbg !2 {
entry:
  %sum = add i32 %a, %b, !dbg !3
  ret i32 %sum, !dbg !4
}

!0 = !{i32 12, !1, ...}
!1 = !{..."main.c"..."/home/user"...}
!2 = !{..."add"...!1...i32 3...}
!3 = !{i32 4, i32 12, !2}
!4 = !{i32 5, i32 3, !2}
!llvm.dbg.cu = !{!0}
```

### 8.11 支持状态

| 功能 | 状态 |
|------|------|
| metadata 透传（存取 `!N = ...`） | 已实现 |
| 指令级 `!dbg !N` 附加 | 已实现 |
| 函数级 `!dbg !N` 附加 | 已实现 |
| 有名 metadata（`!llvm.dbg.cu` 等） | 已实现 |
| DI* 语义解析（DIFile、DILocation 等） | 已实现 |
| opt 优化保留 debug 信息 | 支持（opt 默认行为） |
| 内联 `inlinedAt` 链 | 依赖 opt 处理，透传存储 |
| 完整 DWARF 类型体系 | 后续扩展 |

---

## 9. 调用外部 opt（OptRunner）

### 9.1 基本用法

```csharp
using IronST.IRBuilder.OptRunner;

// 创建 runner（自动查找 opt 二进制）
var runner = new OptRunner();

string ir = @"
define i32 @add(i32 %a, i32 %b) {
entry:
  %sum = add i32 %a, %b
  ret i32 %sum
}
";

// 文本输出模式（.ll）
string optimized = runner.RunAsText(ir, "-S -O2");
Console.WriteLine(optimized);

// 二进制输出模式（.bc）
byte[] bitcode = runner.RunAsBitcode(ir, "-O2");
File.WriteAllBytes("output.bc", bitcode);
```

### 9.2 指定 opt 路径

```csharp
var runner = new OptRunner();

// 方式 1：手动指定路径
runner.OptPath = @"C:\Program Files\LLVM\bin\opt.exe";

// 方式 2：让自动查找机制工作（默认行为）
runner.OptPath = null; // 自动查找 tools/opt/{rid}/opt 或系统 PATH
```

### 9.3 自动查找顺序

`OptRunner.ResolveOptPath()` 按以下顺序查找 opt：

1. 用户显式设置的 `OptPath`
2. 入口程序集同目录下的 `tools/opt/{rid}/opt(.exe)`
3. `IronST.IRBuilder.dll` 同目录下的 `tools/opt/{rid}/opt(.exe)`
4. 当前工作目录下的 `tools/opt/{rid}/opt(.exe)`
5. 用户 Home 目录下的 `~/.ironst/tools/opt/{rid}/opt(.exe)`
6. 系统 PATH 中的 `opt`

### 9.4 下载 opt 二进制

```bash
# Windows (PowerShell)
cd tools
.\download-opt.ps1

# Linux / macOS
cd tools
chmod +x download-opt.sh
./download-opt.sh

# 指定版本
.\download-opt.ps1 -Version 19.1.4
./download-opt.sh 19.1.4
```

### 9.5 自定义 Pass 列表

```csharp
var runner = new OptRunner();

// 使用特定 pass
string result = runner.RunAsText(ir, "-S --passes=instcombine,gvn,simplifycfg");

// O3 优化
string result = runner.RunAsText(ir, "-S -O3");

// 仅内存优化
string result = runner.RunAsText(ir, "-S --passes=mem2reg");
```

### 9.6 完整的构建 → 优化 → 解析工作流

```csharp
using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using IronST.IRBuilder.OptRunner;
using IronST.IRBuilder.Parser;

// 1. 构建 IR
var mod = BuildMyModule();

// 2. 发射为 .ll 文本
var llText = new LLEmitter().Emit(mod);

// 3. 用 opt 优化
var runner = new OptRunner();
string optimized = runner.RunAsText(llText, "-S -O2");

// 4. 解析优化后的 IR
var optimizedMod = new LLParser().Parse(optimized);

// 5. 访问优化后的对象模型
foreach (var func in optimizedMod.Functions)
{
    Console.WriteLine($"优化后: @{func.Name}");
}
```

---

## 10. CLI 工具 IronST.Opt

### 10.1 基本用法

```bash
# 编译
dotnet build src/IronST.Opt

# 优化 .ll 文件（输出到 stdout）
dotnet run --project src/IronST.Opt -- input.ll -O2 -S

# 优化并输出到文件
dotnet run --project src/IronST.Opt -- input.ll -O2 -S -o output.ll

# 从 stdin 读取
echo 'define i32 @f(i32 %x) { ret i32 %x }' | dotnet run --project src/IronST.Opt --stdin -S -O2

# 输出 .bc 二进制
dotnet run --project src/IronST.Opt -- input.ll --emit-bc -o output.bc

# 自定义 pass
dotnet run --project src/IronST.Opt -- input.ll --passes="instcombine,gvn" -S

# 指定 opt 路径
dotnet run --project src/IronST.Opt -- input.ll -O2 --opt-path "/usr/local/bin/opt"
```

### 10.2 所有参数

| 参数 | 说明 |
|---|---|
| `[文件]` | 输入 .ll 文件路径 |
| `-i, --input <file>` | 输入 .ll 文件路径 |
| `-o, --output <file>` | 输出文件路径（默认 stdout） |
| `-S` | 输出文本 .ll（默认） |
| `--emit-bc` | 输出二进制 .bc |
| `-O0` | 无优化 |
| `-O1` | 基本优化 |
| `-O2` | 标准优化 |
| `-O3` | 激进优化 |
| `-Os` | 体积优化 |
| `-Oz` | 最小体积 |
| `--passes <passes>` | 自定义 pass 列表 |
| `--stdin` | 从标准输入读取 |
| `--opt-path <path>` | 手动指定 opt 路径 |
| `-h, --help` | 显示帮助 |

---

## 11. 完整 Demo 集锦

### Demo 1：生成 Hello World

```csharp
using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;

var mod = new Module("hello.ll");
mod.TargetTriple = "x86_64-unknown-linux-gnu";

// 声明 printf
var printfFt = new FunctionType(IrType.Int32, new[] { IrType.Pointer }, isVarArg: true);
mod.AddFunction(new Function("printf", printfFt));

// 添加字符串常量
mod.AddGlobalString(".str", "Hello, World!\n");

// 定义 main
var mainFt = new FunctionType(IrType.Int32, Array.Empty<IrType>());
var main = new Function("main", mainFt);

var entry = new BasicBlock("entry");
main.AppendBasicBlock(entry);

var builder = new IRBuilder(entry);

// 获取字符串指针
var str = mod.GlobalVariables[0];
var zero = new ConstantInt(IrType.Int32, 0);
var gep = builder.CreateGEP(IrType.Int8, str,
    new Value[] { zero, zero }, n: "ptr", inBounds: true);

// 调用 printf
var printfFunc = mod.Functions[0];
builder.CreateCall(printfFt, printfFunc, new[] { gep });

builder.CreateRet(new ConstantInt(IrType.Int32, 0));

mod.AddFunction(main);

Console.Write(new LLEmitter().Emit(mod));
```

输出：

```llvm
source_filename = "hello.ll"
target triple = "x86_64-unknown-linux-gnu"

@.str = private unnamed_addr constant [15 x i8] c"Hello, World!\0A\00", align 1

declare i32 @printf(ptr, ...)

define i32 @main() {
entry:
  %ptr = getelementptr inbounds i8, ptr @.str, i32 0, i32 0
  call i32 @printf(ptr %ptr)
  ret i32 0
}
```

### Demo 2：斐波那契（递归 + switch + call + phi）

```csharp
using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;

var mod = new Module("fib.ll");
mod.TargetTriple = "x86_64-unknown-linux-gnu";

var ft = new FunctionType(IrType.Int32, new[] { IrType.Int32 });
var fib = new Function("fib", ft);
fib.Parameters[0].Name = "n";
mod.AddFunction(fib);

var entry    = new BasicBlock("entry");
var base0    = new BasicBlock("base0");
var base1    = new BasicBlock("base1");
var recur    = new BasicBlock("recur");
var retBlock = new BasicBlock("ret");

fib.AppendBasicBlock(entry);
fib.AppendBasicBlock(base0);
fib.AppendBasicBlock(base1);
fib.AppendBasicBlock(recur);
fib.AppendBasicBlock(retBlock);

// entry: switch
var sw = new SwitchInst(fib.Parameters[0], recur);
sw.AddCase(new ConstantInt(IrType.Int32, 0), base0);
sw.AddCase(new ConstantInt(IrType.Int32, 1), base1);
entry.AddInstruction(sw);

// base0: 直接跳到 ret
base0.AddInstruction(new BranchInst(retBlock));

// base1: 直接跳到 ret
base1.AddInstruction(new BranchInst(retBlock));

// recur: fib(n-1) + fib(n-2)
var n1 = BinaryOperator.CreateSub(fib.Parameters[0],
    new ConstantInt(IrType.Int32, 1), "n1");
recur.AddInstruction(n1);

var call1 = new CallInst(ft, fib, new Value[] { n1 }, "f1");
recur.AddInstruction(call1);

var n2 = BinaryOperator.CreateSub(fib.Parameters[0],
    new ConstantInt(IrType.Int32, 2), "n2");
recur.AddInstruction(n2);

var call2 = new CallInst(ft, fib, new Value[] { n2 }, "f2");
recur.AddInstruction(call2);

var sum = BinaryOperator.CreateAdd(call1, call2, "sum");
recur.AddInstruction(sum);
recur.AddInstruction(new BranchInst(retBlock));

// ret: phi
var phi = new PHINode(IrType.Int32, "result");
phi.AddIncoming(new ConstantInt(IrType.Int32, 0), base0);
phi.AddIncoming(new ConstantInt(IrType.Int32, 1), base1);
phi.AddIncoming(sum, recur);
retBlock.AddInstruction(phi);
retBlock.AddInstruction(new ReturnInst(phi));

Console.Write(new LLEmitter().Emit(mod));
```

输出：

```llvm
define i32 @fib(i32 %n) {
entry:
  switch i32 %n, label %recur [ i32 0, label %base0 i32 1, label %base1 ]
base0:
  br label %ret
base1:
  br label %ret
recur:
  %n1 = sub i32 %n, 1
  %f1 = call i32 @fib(i32 %n1)
  %n2 = sub i32 %n, 2
  %f2 = call i32 @fib(i32 %n2)
  %sum = add i32 %f1, %f2
  br label %ret
ret:
  %result = phi i32 [ 0, %base0 ], [ 1, %base1 ], [ %sum, %recur ]
  ret i32 %result
}
```

### Demo 3：数组操作（GEP + 循环）

```csharp
using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;

var mod = new Module("array_sum.ll");
mod.TargetTriple = "x86_64-unknown-linux-gnu";

// 函数: i32 @array_sum(ptr %arr, i32 %len)
var ft = new FunctionType(IrType.Int32, new[] { IrType.Pointer, IrType.Int32 });
var func = new Function("array_sum", ft);
func.Parameters[0].Name = "arr";
func.Parameters[1].Name = "len";
mod.AddFunction(func);

var entry    = new BasicBlock("entry");
var loop     = new BasicBlock("loop");
var exit     = new BasicBlock("exit");

func.AppendBasicBlock(entry);
func.AppendBasicBlock(loop);
func.AppendBasicBlock(exit);

var entryBuilder = new IRBuilder(entry);
var i0 = entryBuilder.CreateAlloca(IrType.Int32, n: "i");
var s0 = entryBuilder.CreateAlloca(IrType.Int32, n: "sum");
entryBuilder.CreateStore(new ConstantInt(IrType.Int32, 0), i0);
entryBuilder.CreateStore(new ConstantInt(IrType.Int32, 0), s0);
entryBuilder.CreateBr(loop);

// loop:
var loopBuilder = new IRBuilder(loop);
var iVal = loopBuilder.CreateLoad(IrType.Int32, i0, n: "i.val");
var cmp = loopBuilder.CreateICmp(ICmpPredicate.Slt, iVal, func.Parameters[1], n: "cond");
loopBuilder.CreateCondBr(cmp, loop, exit);

// 循环体在 loop 内继续
var elemPtr = loopBuilder.CreateGEP(IrType.Int32, func.Parameters[0],
    new Value[] { iVal }, n: "elem.ptr", inBounds: true);
var elemVal = loopBuilder.CreateLoad(IrType.Int32, elemPtr, n: "elem");
var sumVal = loopBuilder.CreateLoad(IrType.Int32, s0, n: "sum.val");
var newSum = loopBuilder.CreateAdd(sumVal, elemVal, n: "new.sum");
loopBuilder.CreateStore(newSum, s0);

var iNext = loopBuilder.CreateAdd(iVal, new ConstantInt(IrType.Int32, 1), n: "i.next");
loopBuilder.CreateStore(iNext, i0);
loopBuilder.CreateBr(loop);

// exit:
var exitBuilder = new IRBuilder(exit);
var result = exitBuilder.CreateLoad(IrType.Int32, s0, n: "result");
exitBuilder.CreateRet(result);

Console.Write(new LLEmitter().Emit(mod));
```

### Demo 4：解析现有 .ll 并修改

```csharp
using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using IronST.IRBuilder.Parser;

string input = @"
define i32 @old_name(i32 %x) {
entry:
  %r = add i32 %x, 1
  ret i32 %r
}
";

// 解析
var mod = new LLParser().Parse(input);

// 修改：重命名函数
var func = mod.Functions[0];
func.Name = "new_name";

// 修改：添加一个新参数
// （需要重建函数类型和参数，这里简化演示仅改名）

// 重新发射
Console.Write(new LLEmitter().Emit(mod));
```

输出：

```llvm
define i32 @new_name(i32 %x) {
entry:
  %r = add i32 %x, 1
  ret i32 %r
}
```

### Demo 5：构建 → 优化 → 读取结果

```csharp
using IronST.IRBuilder.Emitter;
using IronST.IRBuilder.IR;
using IronST.IRBuilder.OptRunner;
using IronST.IRBuilder.Parser;

// 1. 构建 IR
var mod = new Module("demo.ll");
var ft = new FunctionType(IrType.Int32, new[] { IrType.Int32 });
var func = new Function("identity", ft);
func.Parameters[0].Name = "x";

var entry = new BasicBlock("entry");
func.AppendBasicBlock(entry);

var builder = new IRBuilder(entry);

// 故意加一个冗余操作: (x + 0) * 1
var add = builder.CreateAdd(func.Parameters[0],
    new ConstantInt(IrType.Int32, 0), "add");
var mul = builder.CreateMul(add,
    new ConstantInt(IrType.Int32, 1), "mul");
builder.CreateRet(mul);

mod.AddFunction(func);

// 2. 发射
var ll = new LLEmitter().Emit(mod);
Console.WriteLine("=== 优化前 ===");
Console.WriteLine(ll);

// 3. 用 opt -O2 优化
var runner = new OptRunner();
try
{
    string optimized = runner.RunAsText(ll, "-S -O2");
    Console.WriteLine("=== 优化后 ===");
    Console.WriteLine(optimized);

    // 4. 解析优化后的 IR
    var optMod = new LLParser().Parse(optimized);
    var optFunc = optMod.Functions[0];
    Console.WriteLine($"优化后函数: @{optFunc.Name}");
    Console.WriteLine($"基本块数: {optFunc.BasicBlocks.Count}");
}
catch (Exception ex)
{
    Console.WriteLine($"opt 不可用: {ex.Message}");
    Console.WriteLine("请运行 tools/download-opt 脚本下载 opt。");
}
```

---

## 12. API 参考速查

### 指令速查表

| LLVM IR 指令 | C# 构造方式 |
|---|---|
| `ret void` | `new ReturnInst()` |
| `ret i32 %v` | `new ReturnInst(v)` |
| `br label %l` | `new BranchInst(destBB)` |
| `br i1 %c, label %t, label %f` | `new BranchInst(cond, trueBB, falseBB)` |
| `add i32 %a, %b` | `BinaryOperator.CreateAdd(a, b, name)` |
| `sub i32 %a, %b` | `BinaryOperator.CreateSub(a, b, name)` |
| `mul i32 %a, %b` | `BinaryOperator.CreateMul(a, b, name)` |
| `icmp slt i32 %a, %b` | `new ICmpInst(ICmpPredicate.Slt, a, b, name)` |
| `alloca i32` | `new AllocaInst(IrType.Int32, name: n)` |
| `load i32, ptr %p` | `new LoadInst(IrType.Int32, p, name)` |
| `store i32 %v, ptr %p` | `new StoreInst(v, p)` |
| `getelementptr i32, ptr %p, i32 %i` | `new GetElementPtrInst(ty, p, indices)` |
| `sext i32 %v to i64` | `CastInst.CreateSExt(v, IrType.Int64, name)` |
| `zext i32 %v to i64` | `CastInst.CreateZExt(v, IrType.Int64, name)` |
| `trunc i64 %v to i32` | `CastInst.CreateTrunc(v, IrType.Int32, name)` |
| `bitcast ptr %v to ptr` | `CastInst.CreateBitCast(v, IrType.Pointer, name)` |
| `phi i32 [%v1, %bb1], [%v2, %bb2]` | `new PHINode(ty, name)` + `AddIncoming()` |
| `select i1 %c, i32 %t, i32 %f` | `new SelectInst(c, t, f, name)` |
| `call i32 @f(i32 %a)` | `new CallInst(ft, func, args, name)` |
| `switch i32 %v, label %def [i32 0, label %l]` | `new SwitchInst(v, def)` + `AddCase()` |
| `extractvalue {i32, float} %agg, 0` | `new ExtractValueInst(agg, indices, name)` |
| `insertvalue {i32, float} %agg, i32 %v, 0` | `new InsertValueInst(agg, v, indices, name)` |
| `unreachable` | `new UnreachableInst()` |

### 链接类型

```csharp
LinkageType.External          // external
LinkageType.Internal          // internal
LinkageType.Private           // private
LinkageType.Weak              // weak
LinkageType.Linkonce          // linkonce
LinkageType.LinkonceODR       // linkonce_odr
LinkageType.Common            // common
LinkageType.Appending         // appending
LinkageType.AvailableExternally  // available_externally
LinkageType.ExternalWeak      // extern_weak
```

### 整数比较谓词

```csharp
ICmpPredicate.Eq    // eq
ICmpPredicate.Ne    // ne
ICmpPredicate.Sgt   // sgt (有符号大于)
ICmpPredicate.Sge   // sge (有符号大于等于)
ICmpPredicate.Slt   // slt (有符号小于)
ICmpPredicate.Sle   // sle (有符号小于等于)
ICmpPredicate.Ugt   // ugt (无符号大于)
ICmpPredicate.Uge   // uge (无符号大于等于)
ICmpPredicate.Ult   // ult (无符号小于)
ICmpPredicate.Ule   // ule (无符号小于等于)
```

### 类型转换操作

```csharp
CastOp.Trunc           // trunc
CastOp.ZExt            // zext
CastOp.SExt            // sext
CastOp.FPTrunc         // fptrunc
CastOp.FPExt           // fpext
CastOp.FPToUI          // fptoui
CastOp.FPToSI          // fptosi
CastOp.UIToFP          // uitofp
CastOp.SIToFP          // sitofp
CastOp.PtrToInt        // ptrtoint
CastOp.IntToPtr        // inttoptr
CastOp.BitCast         // bitcast
CastOp.AddrSpaceCast   // addrspacecast
```

### 二元运算

```csharp
BinaryOp.Add    BinaryOp.FAdd
BinaryOp.Sub    BinaryOp.FSub
BinaryOp.Mul    BinaryOp.FMul
BinaryOp.UDiv   BinaryOp.SDiv   BinaryOp.FDiv
BinaryOp.URem   BinaryOp.SRem   BinaryOp.FRem
BinaryOp.Shl    BinaryOp.LShr   BinaryOp.AShr
BinaryOp.And    BinaryOp.Or     BinaryOp.Xor
```
