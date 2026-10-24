# 更新日志

本文件记录 IronST 的用户可见变更。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

版本号单一事实来源是仓库根目录的 `Directory.Build.props`
（`Version` / `AssemblyVersion` / `FileVersion` / `InformationalVersion`）；
每次发版同时打 `v<版本>` 标签，并在此处新增条目。

---

## 支持矩阵

| 维度 | 支持范围 | 说明 |
|---|---|---|
| .NET SDK | 10.0.402（含同 feature band 的更高补丁） | `global.json` 固定；`rollForward: latestFeature` |
| 运行时 | .NET 10.0 | 当前预览版（0.0.1）定位为语言工具链预览，不承诺生产级稳定 |
| 操作系统 | Windows 10/11：CLI、VM、LSP、IDE 全量 | Linux / macOS：CLI、VM、LSP 代码跨平台，**尚未纳入验证矩阵** |
| 桌面 IDE | 仅 Windows | WPF + AvalonEdit，无法跨平台 |
| 可选工具 | 系统 `opt` / `clang` | 仅 `--bc`（LLVM bitcode 输出）与二次优化需要；缺失时退出码 2 |

---

## [未发布] — 当前修复状态（0.0.1 预览修复版）

> 本条目记录从 `0.1.0` 到 `0.0.1` 的修复与状态同步，不代表正式发布版本。
> 生产使用前请先确认 `docs/生产发布检查表.md` 的未关闭项全部解决。

### 已修复（代码 + 测试状态已同步）

- 前端语义：`A3`（`VAR_RETAIN` / `NON_RETAIN` 拒绝路径：`SemanticAnalyzer` 已插入 `E3003` 诊断，不需要宿主持久化接口）；`A4`（`TIME`/`DATE`/`TOD`/`DT` 类型规则：`IECType.IsInteger` 已包含时间类型，`ExpressionEmitter` 复用 `i64` 指令路径）；`A5`（标准功能块：`IECStandardLibrary.CreateStandardFunctionBlocks()` 已新增 8 个标准 FB 声明 `TON`/`TOF`/`CTU`/`CTD`/`R_TRIG`/`F_TRIG`/`SR`/`RS`，运行时 `DefaultHostEnvironment` 已注册保留 HCALL ID `0x0100-0x0107`）；`A10`（未赋值 `REAL` 的 `<>` / 排序比较：`FrameBuilder.BuildPrologue` 默认值初始化 + `BcValue.Compare` `Void` 防御已修复）。
- 运行时：`A2`（空引用解引用保护：`NULLCK` 指令 `0x95` + `RuntimeErrorType.NullReference`）；`C1`（数组越界检查：`BOUNDS` 指令 `0x94`）；`C8`（寄存器压力：`558e934` 已修语句边界死值回收 + 临时寄存器安全回收，`C8RegressionTests` 全绿，但通用临时值回收未完全收敛）；时钟源注入接口（`HostCallIds.CLOCK` 注册，`DefaultHostEnvironment` 提供可覆盖实现，测试可注入固定值）。
- 测试工程：无效缺口记录测试已全部清理（`RetainVariablesTests`、`FBActionLibraryDeclarationTests`、`TimeArithmeticTests`、`TimeHostClockTests`、`ComplexLValueEndToEndTests` 旧版已删除）；修复状态测试已保留并编译通过（`StandardFunctionBlocksTests` 只保留 TON 修复状态断言，`ComplexLValueEndToEndTests_Part2` 已重写为实际功能断言）。

### 已修复（检查表状态已同步，不重复列出全部历史修复项）

- 结构体按值比较（`F5` / `A1`）、全局/外部聚合存储（`A8`）、聚合整体搬运（`A9`）、`REFERENCE` 前向引用（`F7`）、`MOD` 运行期可用（`F13` 前置修复）、字符串后端（`F11`）、`CASE` 区间标签（`F12`）、VM 协作式取消、CLI 顶层异常边界、构建可复现（`D4`）、LICENSE 加入（`D2`）、CHANGELOG 更新（`D3` 部分）、版本信息进入产物、发布脚本（`D5` 部分实现，Linux 未实跑）、CI 工作流（`D1` 部分实现，分支保护未开启）。

### 未修复 / 保留给后续（生产发布前必须解决的 P0 / 发布工程缺口）

- `A3` 持久化实现（拒绝路径已修，不需要宿主接口，但检查表要求实现或拒绝已满足拒绝验收；若目标是真实 PLC 程序，仍需决定是否提供持久化实现或明确文档拒绝）。
- `A7` 完整验收（复杂左值组合：引用链、数组元素、FB 实例、`VAR_IN_OUT` 聚合传参的全部端到端组合未覆盖；缺陷登记簿 §3.7 边界仍开放）。
- `F4` 外部库声明链路（`FB ACTION` 外部库声明未接通，无独立测试文件；随模块 `B` 链接协议 `B1` 一起完善）。
- 标准功能块完整工业行为（`A5` 已提供声明 + 基本运行时接口，完整计时/计数/边沿状态机行为非完整工业控制行为，随标准库路线图逐步完善，不阻塞预览版使用）。
- 发布工程：`D1`（分支保护手动开启）、`D3`（`v0.0.1` 标签与 `gh release create` 仍未执行）、`D5`（Linux 实跑归 `E4`、GitHub Release 上传需先打标签 `D3`）。
- 质量证据：`E1`（IEC 一致性测试集缺失）、`E2`（无 fuzz 回归）、`E3`（无 soak/benchmark 基线）、`E4`（Linux / macOS 未纳入验证矩阵）、`E5`（覆盖率未收集，`coverlet.collector` 已引用但 CI 未启用）。

### 支持矩阵（0.0.1 修复版）

| 维度 | 支持范围 | 说明 |
|---|---|---|
| .NET SDK | 10.0.402 | `global.json` 固定 |
| 运行时 | .NET 10.0 | 预览版，不承诺 API/字节码格式稳定 |
| 操作系统 | Windows 全量；Linux/macOS 代码跨平台但**未验证** | IDE 仅 Windows（WPF） |
| 桌面 IDE | 仅 Windows | WPF + AvalonEdit |
| 可选工具 | 系统 `opt` / `clang` | 仅 `--bc` 需要 |
| 发布状态 | 有 self-contained 脚本（`scripts/publish.sh`），有 `artifacts/dist/` 产物，**无标签、无 Release、无 CI 分支保护** | 预览修复版，不代表正式发布 |

---

## [0.0.1] — 当前修复状态（非正式发布，预览修复版）

> 基于 `master` 最新提交（无 `v0.0.1` 标签，无 GitHub Release）。本条目记录从 `0.1.0` 到 `0.0.1` 的修复与状态同步，不承诺正式发布版本。

修复内容见“已修复”与“未修复”两节；文档修复内容见 `docs/文档修复说明.md`（新增正式文档、删除临时稿、修复不一致、保留内部档案）。
