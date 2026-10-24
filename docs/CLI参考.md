# IronST CLI 与工程参考（0.1.0）

> 从 `IronST.CLI/Program.cs`（重构为命令行工具）、`CliRouter`、`DefaultHostEnvironment`、`HostFunctionRegistry` 整理。与 `README.md` CLI 章节对应。

---

## 1. 环境要求

- .NET SDK：`10.0.402`（由 `global.json` 固定，`rollForward: latestFeature`，`allowPrerelease: false`）。
- 目标平台：Windows 全量；Linux/macOS 仅 CLI/VM/LSP，可运行 `dotnet` 编译的代码，但 IDE（WPF）不可用，`dotnet build` 完整解决方案仅在 Windows 通过。
- 可选工具：系统 `opt` / `clang`（仅 `--bc` 输出与二次优化需要；缺失时退出码 2）。

---

## 2. 入口与基本用法

```bash
# 编译为字节码
dotnet run --project IronST.CLI -- compile file.st

# 编译并运行（编译+VM 执行）
dotnet run --project IronST.CLI -- run file.st

# 发布产物直接运行
./artifacts/dist/ironst-0.1.0-win-x64/ironst.exe run file.st
```

---

## 3. 命令详解

### `compile`

```bash
compile <file.st ...> [-o out.isb] [--ir out.ll] [--bc out.bc] [--opt 0|1|2]
```

- 输入：一个或多个 `.st` 文件（合并为一个编译单元）。
- 默认优化：`--opt 0`（不优化，便于调试）。
- `--opt 1`：基础优化（`mem2reg` + 指令合并）；`--opt 2`：完整优化（`-O2` 级别）。
- `--bc` 需要系统 `opt` 工具，缺失时退出码 `2`。
- 副产物：`.isb`、可选 `.ll`、可选 `.bc`、`.map.json`（映射文件）。

### `build`

```bash
build <file.stproj> [-o out.isb]
```

- 按 `.stproj` 工程文件构建为单个 `.isb`。
- `.stproj`：XML 工程描述，支持多文件、库文件、输出路径、入口程序名。

### `expand`

```bash
expand <file.stproj>
```

- 把工程源码展开为单个 `.st` 文件，用于检查合并结果。

### `mod`

```bash
mod build <file.st | .stproj>
mod link <a.stmod ...>
mod inspect <file.stmod>
```

- `.stmod`：模块格式（魔数 `STMD`），非可执行文件（`run` 明确拒绝 `.stmod` 直接执行）。
- `mod inspect`：查看元数据、区段、函数索引、链接状态。
- 跨模块静态链接（`mod link`）**未完成**（`B1`），`v1` 只实现前半段（编译到 `.stmod`）。

### `run`

```bash
run <file.st ...>
run <.stmod | .stproj | .isb>

# 选项
--entry NAME      # 显式入口 PROGRAM 名
--stack N         # 栈大小（默认 8192）
--timeout MS      # 墙钟超时（默认不限），取消退出码 130
--max-instructions N  # 指令预算（默认不限，0=不限）；超限退出码 1，错误类型 InstructionBudgetExceeded
```

- 运行 `.isb`：直接加载执行；运行 `.st`：先编译后执行。
- 取消：协作式（`VMOptions.Cancellation`）；`Ctrl+C` 或 `--timeout` 触发，退出码 `130`。
- 指令预算：每条指令边界判断，`N` 恰好允许 N 条，第 `N+1` 条报错（先判后扣）。

### `disasm`

```bash
disasm <file.isb>
```

- 反汇编字节码文件；输出格式：`PC: 指令`（按指令长度表逐条解析）。

### `verify`

```bash
verify <file.st>
```

- 只做前端分析（词法/语法/语义三遍扫描），不生成代码，不运行 VM。
- 报错即退出码 `1`，无错误退出码 `0`。
- **注意**：`verify` 不注入默认宿主函数（如 `Print`），因此包含 `Print` 的源码在 `verify` 下会报 `E2006 未声明的函数 'Print'`（`[缺陷登记 C7]`）。

### `analyze`

```bash
analyze <file.st>
```

- 前端分析并打印符号表、POU、变量、类型、调用、引用表；用于调试编译管线。

### `repl`

```bash
repl
```

- 交互式 REPL：多行输入，空行执行，`exit` 退出。
- 沙箱内无法完整交互测试（无真实终端），逻辑简单，未做自动化验证。

### `demo`

```bash
demo
```

- 运行内置演示程序（结构体 + 宿主函数演示），完整保留原 `Program.cs` 的演示逻辑。

---

## 4. 退出码契约

| 退出码 | 含义 | 触发场景 |
|---|---|---|
| `0` | 成功 | 编译/运行/反汇编/分析正常完成 |
| `1` | 编译/运行/参数错误 | 前端诊断、运行时错误、非法参数、指令预算超限 |
| `2` | 工具缺失 | `--bc` 需要的系统 `opt` 缺失（`[README]:142`） |
| `130` | 取消 | `Ctrl+C`、`--timeout` 触发协作式取消 |

- 预期失败（如已知错误输入）：诊断信息 + `1`。
- 非预期异常：标记为“工具缺陷” + `1`。
- 堆栈输出：仅在 `IRONST_DEBUG=1` 时打印完整堆栈（`[缺陷登记簿]` §顶层异常边界）。

---

## 5. 宿主函数与标准库

- **默认注册**：`Print`、字符串标准函数（`LEN`/`LEFT`/`RIGHT`/`MID`/`CONCAT`/`FIND`/`INSERT`/`DELETE`/`REPLACE`）。
- **浮点数学函数**：`SQRT`/`SIN`/`LN`/`ABS(REAL)` 降级为 C `extern`（`sqrt`/`sin`/`log`/`fabs`）；**不内置实现**（`[缺陷登记 F13]` 用户裁定：走用户自建宿主注入）。使用者需向 `hostFunctions` 注册并提供 handler；注入路径已验收（`FloatStdLibHostInjectionTests` 6 例全绿）。
- `MOD`：整数 `IMOD`（有符号）与 `UMOD`（无符号）已修复；除零抛 `DivideByZero`。
- 宿主函数 ID：CLI 按注册顺序分配，IDE 曾用硬编码常量；`Ordinal` vs `OrdinalIgnoreCase` 不一致（`[缺陷登记 HOST-2]`）；`.stmod` 链接阶段必须处理（随 `B1` 一起）。

---

## 6. 工程文件 `.stproj`

> `.stproj` 为工程描述文件（XML），支持多文件构建、库文件引用、输出路径覆盖。格式规范未独立成文，仅在 `CLI` 命令实现中可读（`CompileCommand`、`BuildCommand`）。

已实现：
- `build`：多文件合并编译为 `.isb`。
- `expand`：展开为单个 `.st`。
- `run`：可接 `.stproj` 直接运行（先构建后执行）。

未完成：
- `.stmod` 静态链接协议尚未冻结（`B4`）；旧制品版本拒绝策略未定。
- 工程文件的完整 XML Schema 未文档化，建议参考源码 `IronST.CLI/Commands/` 与 `Samples/`。

---

## 7. 运行与调试环境

- `VMOptions` 提供：`MaxInstructions`（默认 0=不限）、`Cancellation`（协作式）、`StackSize`、`StructTypes`（VM 构造时注册）。
- 调试 VM (`Runtime.Debug`)：断点（源行→PC 映射）、单步（`StepOver`/`StepIn`）、继续/停止、PC→源行实时映射、运行时错误捕获。
- IDE 调试：`DebugHostBridge.HostFunctions` 当前只注册 `Print`，字符串函数在 IDE 内不可用（`[缺陷登记 C6]`）；调试核心逻辑由 `Runtime.Tests` 覆盖，IDE 侧以编译+逻辑审查验收（WPF 无法在沙箱交互验证）。
