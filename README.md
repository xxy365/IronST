<p align="center">
  <h1 align="center">IronST</h1>
  <p align="center">IEC 61131-3 Structured Text 编译器 + 字节码 VM</p>
  <p align="center">
    <img src="https://img.shields.io/badge/.NET-10.0-purple" alt="dotnet 10">
    <img src="https://img.shields.io/badge/ANTLR4-4.13.1-blue" alt="antlr4">
    <img src="https://img.shields.io/badge/Bytecode-VM-orange" alt="vm">
    <img src="https://img.shields.io/badge/xUnit-v3-green" alt="xunit">
    <img src="https://img.shields.io/badge/License-LGPL%20v3-blue" alt="license">
  </p>
</p>

---

## 这是什么

IronST 是一个 IEC 61131-3 Structured Text（ST）编译器，自带字节码运行时（VM）。ST 是工业自动化领域最广泛使用的编程语言之一，用于 PLC（可编程逻辑控制器）编程。

**一句话**：写 ST → 编译为 IronST 字节码 → 在 VM 上运行（可选输出 LLVM IR / bitcode）。

```
ST 源码 → [ANTLR4 词法/语法分析] → [AST 转换] → [语义分析] → [LLVM IR 模型] → [字节码生成] → IronST VM
                                                                       └→ 可选：LLVM IR (.ll) / bitcode (.bc)
```

## 项目结构

```text
IronST/
├── grammars/
│   └── ST.g4                                # ANTLR4 语法定义
├── IronST.Common/                           # L0：类型系统 + 符号 + 诊断
├── IronST.IRBuilder/                        # L0：LLVM IR 模型 + 文本发射/解析 + OptRunner
├── IronST.Frontend/                         # L1：ANTLR4 解析 + AST + 语义分析
├── IronST.LLVMCodeGen/                      # L2：AST → LLVM IR 模型
├── IronST.BytecodeGen/                      # L2.5：LLVM IR → IronST 字节码
├── IronST.Runtime.Shared/                   # L3：字节码契约 + VMBase + 汇编器
├── IronST.Runtime/                          # L3：发布 VM
├── IronST.Runtime.Debug/                    # L3：调试 VM（断点/单步/监视）
├── IronST.Runtime.ConsoleApp/               # 运行字节码的控制台宿主示例
├── IronST.CLI/                              # L4：命令行（compile/run/disasm/mod/repl …）
├── IronST.StEditor/                         # WPF 编辑器控件（语法高亮/补全/诊断）
├── IronST.IDE/                              # WPF IDE（工程 / 编辑 / 调试）
├── IronST.LanguageServer/                   # L5：LSP 服务器（可执行 ironst-ls）
├── StEditor/
│   └── StEditor.Demo/                       # 编辑器控件的独立 Demo
└── IronST.*.Tests/                          # 各层测试工程（见「测试」）
```

**依赖方向**（每层只依赖下层，无反向依赖）：

| 层 | 项目 | 依赖 |
|---|---|---|
| L0 | IronST.Common | — |
| L0 | IronST.IRBuilder | — |
| L1 | IronST.Frontend | Common |
| L2 | IronST.LLVMCodeGen | Common, Frontend, IRBuilder |
| L2.5 | IronST.BytecodeGen | IRBuilder, LLVMCodeGen, Runtime.Shared |
| L3 | IronST.Runtime.Shared | — |
| L3 | IronST.Runtime | Runtime.Shared |
| L3 | IronST.Runtime.Debug | Runtime.Shared |
| L3 | IronST.Runtime.ConsoleApp | Runtime, Runtime.Debug |
| L4 | IronST.CLI | Common, Frontend, LLVMCodeGen, BytecodeGen, Runtime |
| — | IronST.StEditor | — |
| L5 | IronST.LanguageServer | Common, Frontend |
| L5 | IronST.IDE | CLI, Frontend, LLVMCodeGen, BytecodeGen, Runtime, Runtime.Debug, StEditor |

## 快速开始

### 环境要求

- .NET 10.0 SDK 或更高版本
- Windows / Linux / macOS（IDE 与编辑器控件为 WPF，仅 Windows）

### 构建

```bash
git clone <repository-url>
cd IronST
dotnet build IronST.slnx
```

### 运行测试

```bash
dotnet test            # 默认解决方案（IronST.slnx）
```

### 发布产物（0.1.0 预览版）

仓库不提交二进制。用脚本打 **self-contained 单文件**包（目标机无需安装 .NET）：

```bash
bash scripts/publish.sh                 # win-x64 + linux-x64
RIDS=win-x64 bash scripts/publish.sh    # 只打 Windows
```

产物在 `artifacts/dist/`：`ironst-<版本>-<rid>.tar.gz` 与 `SHA256SUMS.txt`。

```bash
tar -xzf ironst-0.1.0-win-x64.tar.gz
./ironst-0.1.0-win-x64/ironst.exe run your_program.st  # CLI
tar -tzf ironst-0.1.0-win-x64.tar.gz                   # 包内容
sha256sum -c SHA256SUMS.txt                           # 校验
```

| 包内文件 | 用途 |
|---|---|
| `ironst(.exe)` | CLI：`compile` / `run` / `disasm` / `mod` / `verify` / `repl` … |
| `ironst-ls(.exe)` | 语言服务器（JSON-RPC over stdio），供编辑器接入 |
| `LICENSE` / `README.md` / `CHANGELOG.md` | 许可、用法、版本说明 |

> IDE 是 Windows-only WPF，不进这组跨平台产物；Linux 产物由同一脚本交叉发布，
> 尚未在 Linux 上实跑（见 `docs/生产发布检查表.md` D5 / E4）。

### 编译并运行 ST

```bash
# 编译并执行（VM 直接运行字节码）
dotnet run --project IronST.CLI -- run test.st

# 只编译为字节码文件 test.isb
dotnet run --project IronST.CLI -- compile test.st

# 同时输出 LLVM IR 文本与 bitcode
dotnet run --project IronST.CLI -- compile test.st --ir out.ll --bc out.bc --opt 2
```

### 优化级别

```bash
# 不优化（默认，方便调试）
dotnet run --project IronST.CLI -- compile test.st --opt 0

# 基础优化（mem2reg + 指令合并）
dotnet run --project IronST.CLI -- compile test.st --opt 1

# 完整优化（-O2 级别）
dotnet run --project IronST.CLI -- compile test.st --opt 2
```

> `--opt` 走内置 `OptRunner`；`--bc` 需要系统 `opt` 工具（缺失时退出码为 2）。
> 若要交给 LLVM 二次优化/链接：`clang -O2 out.ll -o program`。

## 支持的语言特性

### POU 类型

| POU 类型 | 说明 | 示例 |
|---|---|---|
| `PROGRAM` | 无参数主程序，编译为 `void` 函数 | `PROGRAM Main ... END_PROGRAM` |
| `FUNCTION` | 有参数有返回值 | `FUNCTION Add : INT ... END_FUNCTION` |
| `FUNCTION_BLOCK` | 有状态的功能块 | `FUNCTION_BLOCK Counter ... END_FUNCTION_BLOCK` |

### 数据类型

| 类型 | LLVM IR | 位宽 |
|---|---|---|
| `BOOL` | `i1` | 1 bit |
| `SINT` / `BYTE` / `USINT` | `i8` | 8 bit |
| `INT` / `WORD` / `UINT` | `i16` | 16 bit |
| `DINT` / `DWORD` / `UDINT` / `ENUM` | `i32` | 32 bit |
| `LINT` / `LWORD` / `ULINT` / `TIME` / `DATE` | `i64` | 64 bit |
| `REAL` | `float` | 32 bit |
| `LREAL` | `double` | 64 bit |
| `STRING(N)` | `{ i32, [N x i8] }` | 可变 |
| `WSTRING(N)` | `{ i32, [N x i16] }` | 可变 |
| `ARRAY[lower..upper] OF type` | `[size x element]` | 可变 |
| `STRUCT` | `%Name = type { fields... }` | 可变 |
| `REFERENCE TO type` | `ptr` | 指针 |

### 语法

```iecst
TYPE Point :
STRUCT
    X : REAL;
    Y : REAL;
END_STRUCT
END_TYPE;

FUNCTION DistanceSq : REAL
VAR_INPUT
    a : Point;
    b : Point;
END_VAR
VAR
    dx : REAL;
    dy : REAL;
END_VAR
dx := a.X - b.X;
dy := a.Y - b.Y;
DistanceSq := dx * dx + dy * dy;
END_FUNCTION

PROGRAM Main
VAR
    p1 : Point;
    p2 : Point;
    dist : REAL;
    i : DINT;
    sum : DINT;
END_VAR

p1.X := 3.0;
p1.Y := 4.0;
p2.X := 1.0;
p2.Y := 2.0;

dist := DistanceSq(p1, p2);

FOR i := 0 TO 9 BY 1 DO
    sum := sum + i;
END_FOR

IF sum > 100 THEN
    sum := 100;
ELSIF sum > 50 THEN
    sum := 50;
ELSE
    sum := 0;
END_IF

CASE sum OF
    1:   sum := 10;
    2,3: sum := 20;
    4..6: sum := 30;
ELSE
    sum := 0;
END_CASE

WHILE sum > 0 DO
    sum := sum - 1;
END_WHILE

REPEAT
    sum := sum + 1;
UNTIL sum >= 10
END_REPEAT

END_PROGRAM
```

### 已支持的语法要素

- 所有基本数据类型（BOOL ~ LREAL、TIME/DATE/TOD/DT）
- 变量声明（VAR / VAR_INPUT / VAR_OUTPUT / VAR_IN_OUT / VAR_CONSTANT / VAR_RETAIN）
- 数组（单维/多维）、结构体、枚举、类型别名、子范围
- 所有算术运算（`+` `-` `*` `/` `MOD` `**`）
- 所有比较运算（`=` `<>` `<` `>` `<=` `>=`）
- 所有逻辑运算（`AND` `OR` `XOR` `NOT`）
- 控制流（IF/ELSIF/ELSE、FOR、WHILE、REPEAT、CASE）
- 函数调用、类型转换函数（`INT(x)`、`REAL(x)` 等）
- RETURN / EXIT 语句
- 字符串字面量（`'hello'`）
- 常量表达式（`1 + 2`、`3.14`、`2#1010`）
- 结构体字段访问（`p.X`）
- 数组下标访问（`arr[3]`、`m[1,2]`）
- 引用类型（`REFERENCE TO`）

## CLI 用法

```text
用法: ironst <命令> [参数]

命令:
  compile <file.st ...>          编译（可多个源文件，合并为一个编译单元）
        [-o out.isb]              字节码输出路径
        [--ir out.ll]             LLVM IR 文本输出
        [--bc out.bc]             LLVM Bitcode 输出（需 opt 工具）
        [--opt 0|1|2]             优化级别（0=不优化, 1=O1, 2=O2）
  build <file.stproj>            按项目文件构建为单个 .isb
        [-o out.isb]              覆盖项目 OutputPath
  expand <file.stproj>           把项目源码展开为一个 .st 文件
  mod build <file.st|.stproj>    编译为 .stmod 模块
  mod link <a.stmod ...>         静态链接为单个完整模块
  mod inspect <file.stmod>       查看元数据/区段/函数索引/链接状态
  run <file.st ...>              编译并执行（可多个源文件）
  run <file.stmod|.stproj|.isb>  执行模块 / 项目 / 字节码
        [--entry NAME]            显式入口 PROGRAM 名
        [--stack N]               栈大小（默认 8192）
        [--timeout MS]            运行超时（毫秒，默认不限）
        [--max-instructions N]    指令预算（单次运行上限，默认不限）
  disasm <file.isb>              反汇编字节码
  verify <file.st>               只做前端分析，报告错误
  analyze <file.st>              前端分析并打印符号/调用/引用表
  repl                           交互式 REPL
  demo                           运行内置演示程序
  help                           显示帮助

退出码: 0=成功, 1=编译/运行错误, 2=工具缺失(opt), 130=取消
```

### 示例

```bash
# 编译并运行单文件
dotnet run --project IronST.CLI -- run test.st

# 编译为字节码，再反汇编
dotnet run --project IronST.CLI -- compile test.st -o test.isb
dotnet run --project IronST.CLI -- disasm test.isb

# 多文件工程（.stproj）构建并运行
dotnet run --project IronST.CLI -- run MyProject.stproj

# 只做前端检查（不生成代码）
dotnet run --project IronST.CLI -- verify test.st
```

## 编译管线详解

```
┌──────────────────────────────────────────────────────┐
│                 ST 源码 (.st)                         │
└──────────────────────┬───────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────┐
│  FrontendPipeline.Analyze()                          │
│                                                      │
│  ┌────────────────────────────────────────────────┐  │
│  │  ANTLR4 词法/语法分析 (STLexer + STParser)     │  │
│  │  → ParseTree (语法树)                          │  │
│  └────────────────────────┬───────────────────────┘  │
│                           │                          │
│  ┌────────────────────────▼───────────────────────┐  │
│  │  ParseTreeToASTVisitor                         │  │
│  │  → CompilationUnitNode (AST)                   │  │
│  └────────────────────────┬───────────────────────┘  │
│                           │                          │
│  ┌────────────────────────▼───────────────────────┐  │
│  │  SemanticAnalyzer (三遍扫描)                    │  │
│  │  Pass 1: 注册自定义类型 → TypeSymbol           │  │
│  │  Pass 2: 注册 POU 签名 → FunctionSymbol        │  │
│  │  Pass 3: 分析 POU 体 → 类型推导 + 错误报告     │  │
│  └────────────────────────┬───────────────────────┘  │
│                           │                          │
│  → AnalysisResult { AST, Symbols, Diagnostics }      │
└──────────────────────────┬───────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────┐
│  IRGenerator.Generate()                              │
│                                                      │
│  Pass 1: 注册结构体类型 → LLVM named struct          │
│  Pass 2: 声明所有函数签名 → LLVM function            │
│  Pass 3: 生成函数体 → LLVM basic blocks + 指令       │
│  → LLVM IR 模型（Module）                            │
└──────────────────────────┬───────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────┐
│  OptRunner（--opt 1|2，可选）                         │
│  内置优化 / 调用系统 opt                              │
└──────────────────────────┬───────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────┐
│  ByteCodeGenerator.Generate()                        │
│  LLVM IR → IronST 字节码（BytecodeModule）            │
└──────────────────────────┬───────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────┐
│  运行 / 输出                                          │
│  IronST.Runtime VM 直接执行字节码                     │
│  .isb → 字节码文件；.ll / .bc → LLVM IR / bitcode     │
└──────────────────────────────────────────────────────┘
```

## API 使用

### 端到端：编译并运行

```csharp
using IronST.CLI;

string source = """
PROGRAM Main
VAR x : INT; y : INT; sum : INT; END_VAR
x := 10;
y := 20;
sum := x + y;
END_PROGRAM
""";

// 一步到位：编译为字节码并交给 VM 执行
IronRuntime.CompileAndRun(source, "main.st");
```

`IronRuntime` 还提供 `Compile`（只编译）、`Run` / `RunModule`（运行已有结果）、
`Save` / `LoadModule`（`.isb` 存取）；多文件用 `CompileFiles(files, entryFunctionName: ...)`。

### 底层链路

```csharp
using IronST.Frontend;
using IronST.LLVMCodeGen.CodeGen;
using IronST.BytecodeGen;

// 1) 前端分析（多文件用 FrontendPipeline.AnalyzeFiles）
var result = FrontendPipeline.Analyze(source, "main.st");
foreach (var d in result.Diagnostics)
    Console.WriteLine(d);

// 2) AST → LLVM IR 模型
var module = new IRGenerator().Generate(result);

// 3) LLVM IR → 字节码
var codeGen = new ByteCodeGenerator(sourceFileName: "main.st");
var bytecode = codeGen.Generate(module);
```

### 访问 AST 和符号表

```csharp
var result = FrontendPipeline.Analyze(source, "main.st");

// 遍历 POU
foreach (var pou in result.AST.POUs)
    Console.WriteLine($"{pou.Name} ({pou.GetType().Name})");

// 查找符号
var sym = result.Symbols.Resolve("MyFunction");
if (sym is FunctionSymbol fs)
    Console.WriteLine($"返回类型: {fs.ReturnType.Name}, 参数: {fs.Parameters.Count}");

// 检查诊断
foreach (var d in result.Diagnostics)
    Console.WriteLine($"{d.Span}: {d.Severity}: {d.Message}");
```

## 类型推导规则

二元运算的类型提升（Widen）逻辑：

```
同类型 + 同类型  → 该类型
整数 + 整数      → 取较大位宽
整数 + 浮点      → 浮点
浮点 + 浮点      → 取较大位宽
比较运算         → BOOL
逻辑运算         → BOOL
```

一元运算：
- `NOT` → `BOOL`
- `-`（取负）→ 操作数类型

## 生成的 LLVM IR 示例（`--ir`）

**优化前：**

```llvm
define i32 @Add(i32 %a, i32 %b) {
entry:
  %.param.a = alloca i32
  store i32 %a, ptr %.param.a
  %.param.b = alloca i32
  store i32 %b, ptr %.param.b
  %.Add.ret = alloca i32
  store i32 0, ptr %.Add.ret
  %ld = load i32, ptr %.param.a
  %ld1 = load i32, ptr %.param.b
  %add = add i32 %ld, %ld1
  store i32 %add, ptr %.Add.ret
  br label %return
return:
  %ret.load = load i32, ptr %.Add.ret
  ret i32 %ret.load
}
```

**优化后 (`--opt 2`)：**

```llvm
define i32 @Add(i32 %a, i32 %b) {
entry:
  %add = add i32 %a, %b
  ret i32 %add
}
```

## 错误码

错误码按层分组（定义见 `IronST.Common/Diagnostics/ErrorCode.cs`）：

| 段 | 含义 | 示例 |
|---|---|---|
| `E1xxx` | 语法/解析错误 | `E1004` 无法识别的字符、`E1007` 缺少分号 |
| `E2xxx` | 语义错误 | `E2001` 未声明的标识符、`E2002` 重复声明、`E2006` 未知的函数 |
| `E3xxx` | 代码生成错误 | `E3002` 字节码生成失败、`E3003` 不支持的语言特性 |
| `E4xxx` | 运行时错误 | `E4001` 数组越界、`E4002` 空引用、`E4004` 栈溢出 |

## 测试

### 运行全部测试

```bash
dotnet test
```

### 按项目运行

```bash
dotnet test IronST.Common.Tests/
dotnet test IronST.Frontend.Tests/
dotnet test IronST.LLVMCodeGen.Tests/
# …其余测试工程同理
```

### 按名称过滤

```bash
dotnet test --filter "FullyQualifiedName~IECType"
dotnet test --filter "Optimize"
```

### 测试覆盖

| 测试项目 | 覆盖范围 |
|---|---|
| Common.Tests | IECType 工厂方法/属性、LookupBuiltin、DiagnosticBag、SourceSpan、Symbol 层次结构 |
| Frontend.Tests | SymbolTable 作用域、SemanticAnalyzer 类型推导/Widen/错误报告、FrontendPipeline 端到端 |
| IRBuilder.Tests | LLVM IR 文本发射/解析、OptRunner |
| LLVMCodeGen.Tests | TypeMapper 类型映射、IRGenerator 指令/控制流/函数调用 |
| BytecodeGen.Tests | 字节码汇编器、Debug 指令、控制流/聚合/引用端到端 |
| Runtime.Tests | VM 执行、调用约定、调试 VM（断点/单步） |
| CLI.Tests | 子命令、多文件编译、`.stmod` 模块链接、`.stproj` 项目文件 |
| IDE.Tests | 工程模型、编辑分析、F5 调试接线 |
| LanguageServer.Tests | JSON-RPC 协议、补全、签名帮助、导航/hover、文本索引 |

## 已知限制

> 下列为**当前**仍存在的限制。此前版本列出的 FUNCTION_BLOCK Action、
> VAR_GLOBAL/VAR_EXTERNAL、多文件编译、内置字符串函数、REFERENCE 解引用
> 均已实现（见 `模块路线.md` 模块 B）。

| 限制 | 说明 |
|---|---|
| $-escape 字符串 | STRING 内的 `$n`、`$t` 等转义不处理（仅处理 `''` / `""` 双写转义） |
| 时间字面量 | TIME/DATE/TOD/DT 映射为 i64，不做实际时间计算 |

## 技术栈

| 组件 | 技术 | 用途 |
|---|---|---|
| 语法分析 | ANTLR4 4.13.1 | ST 词法/语法分析 |
| 中间表示 | 自研 LLVM IR 模型 + `OptRunner` | IR 生成 / 文本发射 / 调用系统 `opt` |
| 代码生成 | 自研字节码后端（`IronST.BytecodeGen`） | IR → IronST 字节码 |
| 执行 | IronST VM / 调试 VM | 直接运行字节码（含断点/单步） |
| 桌面界面 | WPF (.NET) + AvalonEdit + CommunityToolkit.Mvvm | IDE 与编辑器控件（Windows） |
| 测试框架 | xUnit v3 | 单元测试和集成测试 |
| 目标框架 | .NET 10.0 | 运行时 |

## 贡献

1. Fork 项目
2. 创建特性分支 (`git checkout -b feature/amazing`)
3. 提交改动 (`git commit -m 'Add amazing feature'`)
4. 推送分支 (`git push origin feature/amazing`)
5. 创建 Pull Request

### 开发规范

- 每层只依赖编号更小的层，禁止反向依赖
- 测试必须覆盖新功能的正常路径和错误路径
- 提交前运行 `dotnet test` 确保全部通过

## 许可证

本项目以 LGPL v3 许可证发布（见 [LICENSE](LICENSE)），允许与专有应用链接，要求对本库的修改保持 LGPL 开源。
