# IronST.CLI — 命令行入口

IEC 61131-3 Structured Text（ST）编译器的命令行工具。负责：

- 读取 `.st` / `.stproj` 源文件
- 生成 LLVM IR、LLVM Bitcode 或 `.isb` 字节码
- 生成、链接、检查 `.stmod` 模块
- 在 VM 上直接运行源码、项目、`.stmod` 或 `.isb`
- 提供反汇编、前端校验、符号分析和 REPL

参数解析与子命令路由在 `Commands/CliRouter.cs`；编译、运行、保存/加载的便利入口在
`IronRuntime.cs`。各子命令的实现都在 `Commands/` 目录下。

## 构建与启动

从仓库根目录运行：

```bash
# 直接运行
dotnet run --project IronST.CLI -- run test.st

# 先构建，再运行生成的 exe
dotnet build IronST.CLI
./IronST.CLI/bin/Debug/net10.0/ironst.exe run test.st
```

下文统一用 `ironst` 表示 CLI 可执行文件。

---

## 1. 命令总表

| 命令 | 别名 | 输入 | 作用 |
|---|---|---|---|
| `compile` | `c` | `.st ...` | 编译为 `.ll` / `.bc` / `.isb` |
| `build` | `b` | `.stproj` | 按项目构建为 `.isb` |
| `expand` | `e` | `.stproj` | 把项目源码展开成一个 `.st` |
| `mod build` | `m build` | `.st ...` 或 `.stproj` | 编译为 `.stmod` 模块 |
| `mod link` | `m link` | `.stmod ...` | 静态链接为一个完整模块 |
| `mod inspect` | `m inspect` | `.stmod` | 查看元数据、区段、函数索引、链接状态 |
| `run` | `r` | `.st` / `.stproj` / `.stmod` / `.isb` / `.irst` | 编译并执行，或直接执行已有产物 |
| `disasm` | `d` | `.isb` | 反汇编字节码 |
| `verify` | `v` | `.st` | 只做前端分析，报告诊断 |
| `analyze` | `a` | `.st` | 前端分析并打印符号、调用、引用表 |
| `repl` | — | 标准输入 | 交互式执行 ST 代码 |
| `demo` | — | — | 运行内置演示程序 |
| `help` | `-h` / `--help` | — | 显示帮助 |

说明：

- 命令名大小写不敏感（内部会 `ToLowerInvariant`）。
- 选项名大小写敏感。
- 无参数运行 `ironst` 会打印帮助并返回 `0`；未知命令会打印帮助并返回 `1`。

---

## 2. 输入 / 输出类型

| 扩展名 | 类型 | 可被哪些命令读取 | 说明 |
|---|---|---|---|
| `.st` | ST 源码 | `compile` / `run` / `mod build` | 可多个文件合并为一个编译单元 |
| `.stproj` | XML 项目文件 | `build` / `run` / `expand` / `mod build` | 源文件清单、入口、输出路径、模块引用 |
| `.stmod` | 可链接模块 | `run` / `mod link` / `mod inspect` | 保留函数名、签名、元数据、导入导出 |
| `.isb` | 最终字节码 | `run` / `disasm` | 可直接执行，无函数名/自定义元数据 |
| `.irst` | 序列化字节码输入 | `run` | CLI 帮助中只单列 `.isb`，但 `run` 也接受 `.irst` |
| `.ll` | LLVM IR 文本 | 外部工具 | `compile --ir` 生成 |
| `.bc` | LLVM Bitcode | 外部工具 | `compile --bc` 生成，需要 `opt` |
| `.map.json` | 旁车源映射 | `run` 自动读取 | PC → `(文件, 行号)`，用于运行期错误定位 |

重要区别：

- `.isb` 是最终可执行字节码，**没有函数名和函数索引**，因此不能用 `--entry` 按函数名调用。
- `.stmod` 是模块输入，保留函数索引和元数据；但只有**已链接**且与当前宿主兼容的 `.stmod` 才能被 `run` 直接执行。
- 裸 `.isb` / `.irst` 不能使用 `--entry`；入口已经固化在文件里。

---

## 3. `compile` — 编译

```text
ironst compile <file.st ...> [选项]
```

选项：

| 选项 | 说明 |
|---|---|
| `-o <out.isb>` | 写出 `.isb` 字节码，同时写旁车源映射 |
| `--ir <out.ll>` | 写出 LLVM IR 文本 |
| `--bc <out.bc>` | 写出 LLVM Bitcode；需要外部 `opt` 工具 |
| `--opt 0\|1\|2` | 优化级别，默认 `0`；只作用于 `--ir` / `--bc` |

行为说明：

- 可一次传入多个 `.st` 文件，它们会被合并为一个编译单元。
- 如果 `-o`、`--ir`、`--bc` 一个都没给，默认把 LLVM IR 写到**第一个源文件同名**的 `.ll`。
- `-o` 可以和 `--ir` / `--bc` 同时使用。
- `-o out.isb` 会额外生成旁车文件 `out.map.json`（实际命名规则是
  `Path.ChangeExtension(out.isb, ".map.json")`）。
- `--opt` **不会优化 `-o` 生成的 `.isb`**。当前实现中 `.isb` 始终来自未优化的
  `CompileResult`；`--opt` 只影响 `--ir` / `--bc`。
- `--bc` 依赖外部 `opt`；工具缺失或优化失败时返回退出码 `2`。
- `compile` 没有 `--entry` 选项。入口由编译管线自动选择；多文件且存在多个可执行
  `PROGRAM` 时，需要改用 `run --entry`，或先调整源码/项目入口。

示例：

```bash
# 只生成 IR（未指定输出时的默认行为）
ironst compile Main.st
# 生成 Main.ll

# 生成字节码 + 源映射
ironst compile Main.st Math.st -o app.isb
# 生成 app.isb 和 app.map.json

# 同时生成优化后的 IR 和 Bitcode
ironst compile Main.st --ir app.ll --bc app.bc --opt 2
```

---

## 4. `build` — 按项目构建 `.isb`

```text
ironst build <file.stproj> [-o out.isb]
```

行为：

- 只接受一个 `.stproj`。
- 使用项目中的 `<StartupProgram>` 作为入口。
- 不写 `-o` 时，输出路径优先使用项目 `<OutputPath>`；否则为
  `<项目目录>/<Name 或项目文件名>.isb`。
- 同时生成旁车源映射。
- `build` 不使用 `<STModuleReference>`；模块静态链接请使用
  `mod build <file.stproj>`。

示例：

```bash
ironst build MyProject.stproj
ironst build MyProject.stproj -o dist/release.isb
```

---

## 5. `expand` — 展开项目源码

```text
ironst expand <file.stproj> [-o out.st]
```

行为：

- 只接受一个 `.stproj`。
- 按项目文件中的 `<STCompile>` 顺序拼接源文件。
- 生成文件带注释头，行尾统一为 `\n`，文件之间插入空行。
- 不写 `-o` 时，输出为 `<项目目录>/<Name 或项目文件名>.st`。
- `expand` 不参与 `build` / `run` 的默认链路，只用于检查、分发或单文件场景。

示例：

```bash
ironst expand MyProject.stproj -o dist/MyProject.expanded.st
```

---

## 6. `run` — 编译并执行 / 直接执行

`run` 会根据输入扩展名选择不同的执行路径。

### 6.1 运行 ST 源码

```text
ironst run <file.st ...> [--entry NAME] [--stack N] [--timeout MS]
```

- 可多个 `.st` 文件，合并为一个编译单元。
- `--entry` 指定入口 `PROGRAM`；不指定时按编译管线规则自动选择。
- `--stack` 必须是正整数，默认 `8192`。
- `--timeout` 是墙钟超时（毫秒），默认不限。到期后 VM 在指令边界退出，
  CLI 输出「已取消。」并返回 `130`；运行期间按 `Ctrl+C` 走同一条取消路径
  （**不是**强杀进程，VM 会正常收尾）。
- 编译期和运行期使用同一套默认宿主环境（`Print` + IEC 字符串函数），避免
  “编译通过、运行期 HCALL 未注册”。

### 6.2 运行项目

```text
ironst run <file.stproj> [--entry NAME] [--stack N] [--timeout MS]
```

- 按项目配置编译并执行。
- `--entry` 覆盖项目 `<StartupProgram>`。
- 只接受一个 `.stproj`，不能和其它输入混用。

### 6.3 运行 `.stmod`

```text
ironst run <file.stmod> [--entry NAME] [--stack N] [--timeout MS]
```

- 只接受一个 `.stmod`，不能和 `.st` / `.stproj` / `.isb` 混用。
- 执行前会检查模块与当前 CLI 宿主环境是否兼容。
- 未链接模块（存在未解析外部引用）会被拒绝，需要先执行 `mod link`。
- `--entry` 按**函数名**选择入口；不指定时使用模块声明的入口。
- 入口函数必须唯一匹配且不能只是声明。
- 会读取旁车 `.map.json`，让运行期错误带“文件:行”。
- **CLI 不能给入口函数传实参**。如果目标函数需要参数，请使用 C# 的
  `STModInvoker.Invoke(...)`，或让入口是无参函数/PROGRAM。

### 6.4 运行 `.isb` / `.irst`

```text
ironst run <file.isb | file.irst> [--stack N] [--timeout MS]
```

- 只接受一个字节码文件。
- 不能使用 `--entry`（入口已固化）。
- 会读取旁车 `.map.json`（如果存在）。
- 使用默认宿主环境运行。

示例：

```bash
ironst run Main.st
ironst run Main.st Math.st --entry Main --stack 16384
ironst run MyProject.stproj
ironst run app.stmod --entry Main
ironst run app.isb
```

---

## 7. `mod build` — 生成 `.stmod`

```text
ironst mod build <file.st ...> [选项]
ironst mod build <file.stproj> [选项]
```

选项：

| 选项 | 说明 |
|---|---|
| `-o <out.stmod>` | 输出模块路径 |
| `--entry NAME` | 显式入口 `PROGRAM` 名 |
| `--ref <m.stmod>` | 引用已编译模块的导出；可重复 |
| `--meta KEY=VALUE` | 追加自定义元数据；可重复 |
| `--author NAME` | 写入 `module.author`（与 `--meta module.author=NAME` 等价；两者不能重复指定） |

行为：

- 源码模式可传多个 `.st`；项目模式只能传一个 `.stproj`，不能混用。
- 源码编译仍走 `CompileFiles`，因此每个编译单元至少要有一个可执行 `PROGRAM`；
  `--entry` 只能选择 `PROGRAM`，不能直接把 `FUNCTION` 当作编译入口。
- 不写 `-o` 时：
  - 源码模式：输出为第一个 `.st` 同名的 `.stmod`
  - 项目模式：输出为 `<项目目录>/<Name 或项目文件名>.stmod`
- `--ref` 只注入引用模块的导出声明。仅使用 `--ref` 时，产物可能是未链接模块，
  需要后续 `mod link` 才能被 `run` 执行。
- 如果 `.stproj` 中声明了 `<STModuleReference>`，`mod build` 会把当前产物与这些
  模块**真正静态链接**成一个自足模块。
- 写出时使用原子替换，失败不会留下半成品。
- 同时生成旁车源映射。

自定义元数据：

```bash
ironst mod build Main.st --entry Main \
  --meta mykey=myvalue \
  --author Alice \
  -o app.stmod
```

- `--meta` 的值统一按字符串写入。
- 重复 key 会报错。
- 以下由 CLI 自动生成或维护的 key 是保留 key，不能通过 `--meta` 覆盖：

  ```text
  module.name
  module.version
  compiler.version
  compiler.target
  module.debug
  module.requiredFeatures
  module.entryPC
  module.entryFunction
  module.linked
  module.globalCount
  module.imports
  module.exports
  module.sources
  ```

- `module.author`、`module.description`、`module.license`、`mykey` 等非保留 key
  可以自由添加。
- `mod inspect` 会显示全部元数据，包括自定义 key。

`mod build` 自动写入的元数据包括（按情况）：

```text
module.name
module.version
compiler.version
compiler.target
module.debug
module.requiredFeatures
module.entryPC
module.entryFunction
module.linked
module.globalCount
module.sources
module.imports
module.exports
```

---

## 8. `mod link` — 静态链接 `.stmod`

```text
ironst mod link <a.stmod> <b.stmod> ... [选项]
```

选项：

| 选项 | 说明 |
|---|---|
| `--entry NAME` | 选择入口函数；有歧义时必须指定 |
| `--name NAME` | 覆盖输出模块名 |
| `-o <out.stmod>` | 输出路径，默认当前目录下的 `linked.stmod` |

行为：

- 所有输入必须是存在的 `.stmod`。
- 链接器会解析导入/导出、重定位、常量和宿主函数 ID。
- 链接失败不会写出半成品。
- 输出模块是自足模块（`module.linked = true`）。
- 会合并各输入模块的旁车源映射，并按代码基址平移后写到输出模块的 `.map.json`。

示例：

```bash
ironst mod link Math.stmod Helper.stmod --entry Main -o app.stmod
```

---

## 9. `mod inspect` — 检查 `.stmod`

```text
ironst mod inspect <file.stmod>
```

行为：

- 只接受一个 `.stmod`。
- 打印：格式版本、入口、元数据、区段、链接状态、全局槽位数、导入/导出符号、
  函数索引（名字、签名、参数/局部变量数、代码偏移和长度）、重定位、常量池大小、
  代码区大小。
- **不执行字节码**。
- 会额外做宿主兼容性检查；如果模块引用了当前 CLI 宿主不提供的函数，会打印问题并
  返回 `1`。

示例：

```bash
ironst mod inspect app.stmod
```

---

## 10. `disasm` — 反汇编 `.isb`

```text
ironst disasm <file.isb>
```

行为：

- 读取一个 `.isb`（或加载器支持的等价字节码文件）。
- 打印入口 PC、全局槽位数和常量池大小。
- 从 `EntryPC` 开始一直反汇编到代码区末尾；不会在 `EXIT` 处停下，因此入口之后的
  其它函数也可能被列出。
- 单次最多输出 100000 条指令，超过后打印截断警告。
- 不读取源映射，不执行字节码。

示例：

```bash
ironst compile Main.st -o app.isb
ironst disasm app.isb
```

---

## 11. `verify` — 只做前端检查

```text
ironst verify <file.st>
```

行为：

- 只运行前端分析（词法/语法/语义），不生成 IR 或字节码。
- 打印诊断信息（错误和警告）。
- 有错误时返回 `1`，否则返回 `0`。
- 只读取第一个位置参数；多余的位置参数当前不会报错，也不会被处理。
- 注意：`verify` 只做纯前端分析，不注入 CLI 默认宿主声明；源文件调用 `Print`
  或字符串宿主函数时可能报“函数未声明”。完整编译请使用 `compile` / `run`。

示例：

```bash
ironst verify Main.st
```

---

## 12. `analyze` — 前端分析报告

```text
ironst analyze <file.st>
```

行为：

- 执行与 `verify` 相同的前端分析。
- 额外打印：
  - POU 列表（PROGRAM / FUNCTION / FUNCTION_BLOCK）
  - 变量表
  - 自定义类型
  - 调用点
  - 符号引用
  - 汇总统计
- 有错误时返回 `1`。
- 同样只读取第一个位置参数。
- 与 `verify` 一样不注入 CLI 默认宿主声明，宿主函数相关代码可能被报未声明。

示例：

```bash
ironst analyze Main.st
```

---

## 13. `repl` — 交互式执行

```text
ironst repl
```

行为：

- 从标准输入读取多行 ST 代码。
- **空行**执行当前缓冲区。
- 输入 `exit` 或 `quit` 退出。
- 每次执行都会重新编译并运行，变量状态不会跨块保留。
- 当前 REPL 挂载 IEC 标准库的前端声明（数学 + 字符串），不注册 CLI `run` 使用的
  `Print` 宿主函数；需要 `Print` 或完整默认宿主环境时请使用 `run`。

---

## 14. `demo` 与 `help`

```text
ironst demo
ironst help
ironst -h
ironst --help
```

- `demo` 运行内置演示，不读取外部文件，也不需要参数。
- `help` / `-h` / `--help` 打印帮助并返回 `0`。
- 无参数运行 `ironst` 同样打印帮助并返回 `0`。
- 未知命令会打印帮助并返回 `1`。

---

## 15. `.stproj` 项目文件

项目文件是 XML，根元素为 `<STProject>`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<STProject Version="1">
  <PropertyGroup>
    <Name>MyProject</Name>
    <StartupProgram>Main</StartupProgram>
    <OutputPath>dist/MyProject.isb</OutputPath>
  </PropertyGroup>

  <ItemGroup>
    <STCompile Include="src/Main.st" />
    <STCompile Include="src/Math.st" />
    <STCompile Include="src/Types.st" />
  </ItemGroup>

  <!-- 可选：mod build 时参与静态链接 -->
  <ItemGroup>
    <STModuleReference Include="lib/Helper.stmod" />
  </ItemGroup>
</STProject>
```

字段：

| 元素 | 必填 | 说明 |
|---|---|---|
| `<Name>` | 否 | 项目/模块名；缺省为项目文件名（不含扩展名） |
| `<StartupProgram>` | 否 | 入口 `PROGRAM` 名；缺省由编译管线自动选择 |
| `<OutputPath>` | 否 | `build` 输出路径；相对项目目录；缺省为 `<Name>.isb` |
| `<STCompile Include="...">` | 是 | 源文件；相对项目目录；至少一个 |
| `<STModuleReference Include="...">` | 否 | 仅 `mod build` 使用；必须是 `.stmod` |

重要限制：

- `build` 和 `run <file.stproj>` 不使用 `<STModuleReference>`。它们只编译
  `<STCompile>` 中的源码。
- 需要把模块引用静态链接进产物时，使用 `mod build <file.stproj>`；产物是自足的
  `.stmod`，再由 `run` 执行。
- `.stproj` 不能和 `.st` / `.stmod` / `.isb` 混在同一个 `run` 命令里。

---

## 16. 产物与旁车文件

### `.isb`

最终可执行字节码，包含入口 PC、代码、常量池、全局槽位数等。它不包含函数名索引，
也不包含 `.stmod` 的自定义元数据。

### `.stmod`

模块格式，包含：

- 元数据
- 函数索引（名字、签名、参数/局部变量数、代码偏移/长度、入口标志）
- 代码区、常量池、全局槽位数
- 导入/导出符号
- 重定位信息
- 链接状态

`.stmod` 是“可链接模块”的载体；只有链接完成的模块才能被 CLI `run` 直接执行。

### `.map.json`

旁车源映射，命名规则：

```text
app.isb    -> app.map.json
app.stmod  -> app.map.json
app.irst   -> app.map.json
```

生成时机：

- `compile -o`
- `build`
- `mod build`
- `mod link`（合并输入模块的映射并平移 PC）

`run` 会按同样的命名规则查找并加载它，用于把运行期错误定位到源文件和行号。
空映射不会写出文件。

### `.ll` / `.bc`

- `.ll`：LLVM IR 文本，`compile --ir` 生成。
- `.bc`：LLVM Bitcode，`compile --bc` 生成，需要外部 `opt`。

---

## 17. 默认宿主环境

`compile`、`build`、`run` 共用 `DefaultHostEnvironment`：

- 宿主函数 `Print`
- IEC 字符串函数：`LEN`、`LEFT`、`RIGHT`、`MID`、`CONCAT`、`INSERT`、
  `DELETE`、`REPLACE`、`FIND`

编译期声明、函数名到 HCALL ID 的映射、运行期注册三者同源，避免出现
“编译通过但运行期 HCALL 未注册”。

整数 `MOD` 由 `MathStandardLibrary` 在 VM 内建安装，不需要宿主额外注册；
有符号余数跟随被除数，除零报 `DivideByZero`。

> **内置 VM 不提供任何 IEC 数学函数**（`SQRT` / `SIN` / `LN` / 浮点 `ABS`…）。
> 这是**有意为之**：这些函数走「用户自建宿主注入」路径 —— 由使用者自行向
> `hostFunctions` 登记 **extern 名**（`sqrt` / `sin` / `log` / `fabs`，**不是** ST 名
> `SQRT`）并提供 handler。内置 VM 只装**语言内建运算符**的实现（如整数 `MOD`），
> 不装 IEC 标准库函数。

已知限制：

- 浮点数学函数（`SQRT`、`SIN` 等）在前端有 IEC 声明，但 CLI 默认宿主没有对应实现；
  相关调用目前不保证可运行 —— 需由使用者自行注入。
- `repl` 使用另一套库配置（IEC 标准库），不包含 CLI 的 `Print` 宿主函数。
- `demo` 使用自己的内置宿主环境。

---

## 18. 退出码与别名

| 退出码 | 含义 |
|---|---|
| `0` | 成功，或打印帮助 |
| `1` | 编译错误、前端错误、运行错误、文件/格式错误、参数错误、宿主不兼容、**工具内部错误** |
| `2` | 优化器相关失败：`opt` 缺失或 `--bc` 生成失败 |
| `130` | 用户取消（`OperationCanceledException`） |

> 顶层异常边界见 `Commands/CliRouter.cs` 的 `ReportFatal`：任何穿透到入口的异常都会被
> 转成诊断 + 上述退出码，**不会**打印 .NET 堆栈；排障时设置 `IRONST_DEBUG=1` 才会输出。

别名：

```text
c  = compile
b  = build
e  = expand
m  = mod
r  = run
d  = disasm
v  = verify
a  = analyze
-h / --help = help
```

命令名大小写不敏感；选项名区分大小写。

---

## 19. 已知限制

1. **`compile --opt` 不优化 `.isb`**
   `--opt` 只影响 `--ir` / `--bc`；`-o` 写出的 `.isb` 来自未优化的 `CompileResult`。

2. **`compile` 没有 `--entry`**
   多文件且存在多个 `PROGRAM` 时无法自动确定入口；需要改用 `run --entry` 或调整项目配置。

3. **`.isb` / `.irst` 不能按函数名调用**
   它们没有函数索引；`run` 对这两类输入拒绝 `--entry`。

4. **`.stmod` 必须先链接**
   存在未解析引用的模块不能直接 `run`，需要 `mod link`。

5. **CLI `run .stmod` 不能传函数实参**
   `--entry` 只能选择入口；没有命令行参数编组。需要参数时使用 C# 的
   `STModInvoker`，或提供无参入口。

6. **`build` / `run .stproj` 不处理 `<STModuleReference>`**
   模块引用目前只在 `mod build <file.stproj>` 中参与静态链接。

7. **`--meta` 的值只能是字符串**
   CLI 不提供布尔/整数/字节数组元数据的命令行写法；这些类型需要通过 C# 构造
   `STModMetadataEntry`。

8. **`verify` / `analyze` / `disasm` 只处理一个文件**
   当前只读取第一个位置参数，多余参数不会报错。

---

## 20. 代码入口与 C# API

### CLI 代码索引

| 文件 | 作用 |
|---|---|
| `Program.cs` | 设置 UTF-8 输出，调用 `CliRouter.Run` |
| `Commands/CliRouter.cs` | 顶层命令路由、帮助文本、退出码 |
| `Commands/CompileCommand.cs` | `compile` |
| `Commands/BuildCommand.cs` | `build` |
| `Commands/ExpandCommand.cs` | `expand` |
| `Commands/ModCommand.cs` | `mod build` / `mod link` / `mod inspect` |
| `Commands/RunCommand.cs` | `run` 的输入分派与执行 |
| `Commands/DisasmCommand.cs` | `disasm` |
| `Commands/VerifyCommand.cs` | `verify` |
| `Commands/AnalyzeCommand.cs` | `analyze` |
| `Commands/ReplCommand.cs` | `repl` |
| `Commands/DemoCommand.cs` | `demo` |
| `IronRuntime.cs` | 编译、运行、保存/加载、源映射便利入口 |
| `ProjectBuilder.cs` | `.stproj` 的读取/编译/展开与默认输出路径 |
| `STProjectFile.cs` | `.stproj` XML 模型 |
| `DefaultHostEnvironment.cs` | CLI 默认宿主声明、HCALL 映射、注册 |
| `HostFunctionRegistry.cs` | CLI 宿主函数实现（含 `Print`、字符串函数） |
| `STModReference.cs` | `mod build --ref` 的引用模块加载 |

### `IronRuntime`

常用方法：

```csharp
// 编译单个源码字符串
CompileResult Compile(
    string source,
    string? sourceFileName = null,
    bool emitDebugInfo = true,
    IReadOnlyDictionary<string, ushort>? hostFunctions = null,
    IReadOnlyList<ExternalLibrary>? libraries = null);

// 多文件合并编译
CompileResult CompileFiles(
    IReadOnlyList<(string Source, string FileName)> files,
    IReadOnlyDictionary<string, ushort>? hostFunctions = null,
    IReadOnlyList<ExternalLibrary>? libraries = null,
    string? entryFunctionName = null,
    bool allowExternalCalls = false);

// 执行编译结果
void Run(
    CompileResult result,
    Action<HostBridge>? configureHost = null,
    int stackSize = 8192);

// 执行已加载的字节码模块
void RunModule(
    BytecodeModule module,
    Action<HostBridge>? configureHost = null,
    int stackSize = 8192,
    IReadOnlyList<(byte TypeId, int FieldCount)>? structTypes = null,
    uint? entryReturnPC = null,
    IReadOnlyDictionary<int, (string file, int line)>? sourceMap = null);

// 保存/加载
void Save(CompileResult result, string path);
void SaveModule(BytecodeModule module, string path);
BytecodeModule LoadModule(string path);
void SaveSourceMap(CompileResult result, string binaryPath);
Dictionary<int, (string file, int line)> LoadSourceMap(string binaryPath);
```

### C# 按函数名调用 `.stmod`

`STModInvoker` 不依赖 CLI，可以直接对内存中的 `STModModule` 或从文件加载的模块调用：

```csharp
using IronST.Runtime;
using IronST.Runtime.Shared;
using IronST.Runtime.Shared.Modules;

STModModule module = STModReader.Load("app.stmod");

var result = STModInvoker.Invoke(
    module,
    "Add",
    new BcValue(2L),
    new BcValue(3L));

long value = result.ReturnValue.IntValue;   // 5
```

可选参数：

```csharp
var result = STModInvoker.Invoke(module, "Add", args, new STModInvokeOptions
{
    HostBridge = bridge,
    StackSize = 65536,
    StructTypes = structTypes,
    // mySourceMap: IReadOnlyDictionary<int, (string file, int line)>
    SourceLocator = pc => mySourceMap.TryGetValue((int)pc, out var loc) ? loc : null,
    CaptureRuntimeErrors = true,   // 默认捕获，错误放在 result.Error
});
```

调用规则：

- 参数个数必须等于函数索引中的 `ParamCount`，否则抛 `ArgumentException`。
- `STModInvokeOptions.StackSize` 默认 `65536`（CLI `run` 默认 `8192`）。
- 前 4 个标量参数写入 `A0`–`A3`，第 5 个起压栈。
- 返回值从 `A0` 读取。
- 聚合类型、`VAR_IN_OUT` 等仍需调用方自行按 ABI 编组。
- `CaptureRuntimeErrors = false` 时，运行期错误直接抛给调用方。
- `STModInvoker` 只负责按名装载和设置参数，不负责链接或宿主兼容性检查；建议只对
  已链接且宿主环境匹配的模块调用。

---

## 21. 典型工作流

### 源码 → 字节码 → 反汇编

```bash
ironst compile Main.st -o app.isb
ironst disasm app.isb
```

### 源码 → 可链接模块 → 链接 → 运行

```bash
# mod build 的源码入口仍走 CompileFiles，因此每个源码编译单元至少要有一个 PROGRAM。
# 这里假设 Math.st 提供函数并带一个 PROGRAM，Main.st 的 PROGRAM 名为 Main。
ironst mod build Math.st -o Math.stmod
ironst mod build Main.st --ref Math.stmod --entry Main -o Main.stmod
ironst mod link Main.stmod Math.stmod --entry Main -o app.stmod
ironst run app.stmod
```

### 项目 → 带元数据的自足模块

```bash
ironst mod build MyProject.stproj --author Alice --meta build=release -o app.stmod
ironst mod inspect app.stmod
ironst run app.stmod
```

### 项目 → `.isb`

```bash
ironst build MyProject.stproj -o dist/app.isb
ironst run dist/app.isb
```
