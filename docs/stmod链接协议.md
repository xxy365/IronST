# `.stmod` 链接协议（v1.x 增补）

> 本文件在《docs/stmod格式规范.md》(v1.0) 之上**增补**链接相关约定。
> 固定头（48 字节）、区段表（32 字节/条）、区段类型与区段 Id 的既有定义**一律保留**；
> 本协议只定义此前"预留但未实现"的区段负载、新元数据 Key 与链接/执行的语义。
>
> 目标（与任务书一致）：
> 1. 单个 `.stmod` 可直接运行；
> 2. 运行时可按函数名指定入口；
> 3. 每个 `.st` 可独立编译为 `.stmod`；
> 4. 模块可引用另一模块中定义的函数；
> 5. 未解析的外部函数引用可保存在 `.stmod` 中，供后续链接；
> 6. 多个 `.stmod` 可真正静态链接为一个完整 `.stmod`；
> 7. 链接产物不依赖原始 `.stmod` / `.st`；
> 8. 同名函数不得被静默任意选中（唯一才可用，歧义必须报错）；
> 9. `.isb` / `.stproj` 与其它既有功能保持兼容。

---

## 0. 与既有格式的关系

| 概念 | 载体 | 是否新增 |
|---|---|---|
| 文件头 / 区段表 | 既有 48 + 32 字节布局 | 否（不变） |
| 函数索引 | `FunctionIndex`（类型 2） | 否（不变） |
| 字节码 | `Code`（类型 3） | 否（不变） |
| 常量池 | `ConstantPool`（类型 4） | 否（不变） |
| 重定位记录 | `Relocations`（类型 7） | 否（不变，语义细化见 §6） |
| 导入符号 | `ImportSymbols`（类型 5） | **是**（此前预留未定义） |
| 导出符号 | `ExportSymbols`（类型 6） | **是**（此前预留未定义） |
| 链接状态 | 元数据 `module.linked` | **是** |
| 全局槽位数 | 元数据 `module.globalCount` | **是** |

`.isb` 格式与执行流程**完全不变**：`.stmod` 的执行由运行时先装载为
`BytecodeModule`（`Code` / `ConstantPool` / `GlobalCount` / `EntryPC`）后进行，
不引入绕过 VM 安全检查的第二条执行路径。

---

## 1. 兼容规则（新字段的旧版语义）

1. **新增元数据 Key**：旧读取器按"未知 Key → 忽略"处理（规范 §4.2），因此可无损向后兼容。
2. **新增区段（类型 5 / 6）**：写入器一律以 **可选**（`Flags` 不含 `Required`）写出，
   旧读取器按"未知可选区段 → 忽略"处理。
3. 若某模块把 `ImportSymbols` / `ExportSymbols` 标记为 **必需**，旧读取器必须拒绝
   （规范 §3.3 第 6 条）；本协议的写入器不这样做。
4. 缺失 `module.linked` 时：**存在 `ImportSymbols` 区段 ⇒ 视为未链接；否则视为已链接**。
5. 缺失 `module.globalCount` 时：取 `max(GlobalIdx 重定位记录.Addend) + 1`（无记录则 0）。

---

## 2. 符号模型

### 2.1 函数符号

- **符号身份 = 函数名**，与 `FunctionIndex.Name` 逐字节相同（序数比较，区分大小写）。
- 模块内函数名唯一（既有校验）；跨模块同名是**歧义**，须由链接器/入口选择显式处理（§7）。
- 调用约定即现有 VM 约定：前 4 个参数槽走 `A0`–`A3`，其余走调用方帧的参数区；
  返回值写入 `A0`；被调方以 `FRAME … LEAVE RET` 管理帧。链接器不改变调用约定。

### 2.2 签名

- `Signature` 为 IR 文本，形如 `DINT (DINT, DINT)`（与 `FunctionLayoutEntry.Signature` 同源）。
- 链接期签名兼容性判据：**字符串不等即不兼容**（确定性，不做类型等价推断）。

### 2.3 声明 vs 实现

| 情形 | `FunctionIndex.Flags` | 是否有可执行实现 |
|---|---|---|
| 普通函数 / PROGRAM | `Exported` 可能置位 | 是 |
| 宿主函数声明（Print、IEC 字符串） | `Declaration`（不置 `Exported`） | 否（运行期由 HostBridge 提供） |
| 外部模块函数引用 | `Declaration`（不置 `Exported`） | 否（须链接） |

- 只有**有可执行实现**的函数可作入口（§7）。
- `Declaration` 且不在宿主环境中的函数 = 外部模块引用，其调用在未链接模块中表现为
  未解析的 `Addr32` 重定位 + `ImportSymbols` 条目。

---

## 3. 元数据增补

| Key | 类型 | 必需 | 缺失时默认 | 含义 |
|---|---|---|---|---|
| `module.linked` | Boolean | 否 | 见 §1.4 | 模块是否已完全链接（无未解析导入） |
| `module.globalCount` | UInt32 | 否 | 见 §1.5 | 模块全局槽位总数（`_globals` 数组长度） |
| `module.imports` | String | 否 | — | 信息性：`;` 分隔的导入符号名（供 inspect） |
| `module.exports` | String | 否 | — | 信息性：`;` 分隔的导出符号名（供 inspect） |

`module.linked` 若存在则**必须是 Boolean**（`0x00`/`0x01`）；其它类型 → 拒绝。
`module.globalCount` 若存在则**必须是 UInt32**；其它类型 → 拒绝。

---

## 4. `ImportSymbols` 区段（类型 5，可选）

负载布局（全部小端）：

| 偏移 | 长度 | 字段 |
|---|---|---|
| 0 | 4 | `Count`（上限 1,000,000） |
| 4 | 4 | `Reserved`（必须为 0） |
| 8 | — | 记录序列 |

每条记录：

| 偏移 | 长度 | 字段 | 说明 |
|---|---|---|---|
| 0 | 4 | `NameLength` | `1..1 MiB` |
| 4 | 4 | `SignatureLength` | `0..1 MiB` |
| 8 | 2 | `Kind` | 1 = Function，2 = HostFunction |
| 10 | 2 | `Flags` | 位 0 = `Required`（v1 写出恒为 0） |
| 12 | `NameLength` | `Name` | UTF-8 |
| … | `SignatureLength` | `Signature` | UTF-8 |

### 4.1 校验（读取器）

- `Count` 超上限 → 拒绝。
- `NameLength == 0` 或超上限 → 拒绝；`SignatureLength` 超上限 → 拒绝。
- 记录负载超出区段 → 拒绝（**不得**按声明长度分配）。
- **符号名不得重复** → 重复即拒绝（确定性，不取首不取末）。
- `Kind` 未知 → 拒绝。
- 记录数 / 字节尾随不一致 → 拒绝。

> 该区段出现即代表模块**依赖外部符号**；无论 `Flags.Required` 为何，
> 直接执行前都必须先解析（§7）。

---

## 5. `ExportSymbols` 区段（类型 6，可选）

负载布局：

| 偏移 | 长度 | 字段 |
|---|---|---|
| 0 | 4 | `Count` |
| 4 | 4 | `Reserved`（必须为 0） |
| 8 | — | 记录序列 |

每条记录：

| 偏移 | 长度 | 字段 | 说明 |
|---|---|---|---|
| 0 | 4 | `FunctionId` | 必须存在于 `FunctionIndex` |
| 4 | 4 | `NameLength` | `1..1 MiB` |
| 8 | 4 | `SignatureLength` | `0..1 MiB` |
| 12 | 2 | `Kind` | 1 = Function |
| 14 | 2 | `Flags` | 位 0 = `Exported` |
| 16 | `NameLength` | `Name` | UTF-8 |
| … | `SignatureLength` | `Signature` | UTF-8 |

### 5.1 校验（读取器）

- `FunctionId` 必须能在 `FunctionIndex` 中找到 → 否则拒绝。
- 名称不得重复；`Kind` 未知 → 拒绝；长度上限与截断规则同 §4.1。

---

## 6. 重定位语义（链接期逐类）

既有 `RelocationTable` 的七字段记录不变。链接器对**每条记录**按下表处理：

| Kind | 符号身份 | 加数 | 链接期目标 | 写回宽度 |
|---|---|---|---|---|
| `Addr32` | 函数名 **或** 模块内标签名 | 模块内已解析的局部目标 PC | 见下 | 32 位无符号 |
| `GlobalIdx` | 全局变量名 | 模块局部槽位 | `ModuleGlobalBase + Addend` | 16 位有符号 |
| `ConstIdx` | 无（按设计不带符号） | 模块局部常量序号 | `ModuleConstBase + Addend` | 32 位有符号 |
| `HostIdx` | 宿主函数名 | 模块局部宿主 ID | 当前宿主环境按名解析出的最终 ID | 16 位无符号 |
| `Off8` | — | — | **不重定位** | — |

### 6.1 `Addr32` 的两类符号

- **函数符号**（符号名出现在任一输入模块的函数索引中）：链接期解析为**合并后函数索引**中
  同名函数的最终 `StartPC`。跨模块调用即靠这一类完成。
- **模块内标签符号**（如 `bb0`、`__Main_epilogue`）：函数体内的绝对 `JMP` 目标。
  写入器在 `mod build` 时把**已解析的局部 PC** 存入记录的 `Addend`；
  链接期目标 = `ModuleCodeBase + Addend`。（函数体逐字节拷贝，故平移量恒定。）

> **为什么 `Off8` 不需要重定位**：条件分支是函数**内部**的相对位移；链接器只在函数之间
> 拼接、**不修改函数体内部字节**，因此相对距离不变。绝不截断或静默生成错误字节码。

### 6.2 `HostIdx` 的按名重解析

沿用既有裁定：动态注册的宿主函数 ID **不是永久稳定身份**，必须按名字在当前宿主环境
重新解析。名字缺失或 ID 不兼容 → 拒绝。静态 ABI ID（`HostCallIds` 常量）不进重定位表。

### 6.3 全局槽位的合并语义（已知限制）

链接期按**输入顺序拼接**各模块的全局槽位（`ModuleGlobalBase`），
**不按变量名做跨模块去重或合并**：

- 两个模块各自声明的同名全局变量会得到**两个独立槽位**（互不干扰）；
- 因此"多个模块共享同一份全局状态"**不受支持**（当前运行时每个模块独立拥有
  `GlobalCount` 个槽位，`.stmod` 也不持久化全局名 → 无可信的跨模块身份）。
- 需要共享状态时，应把该状态收敛到**单一**定义模块，其它模块通过函数调用访问。

本协议**不宣称**实现跨模块全局共享；链接器只保证各模块原有全局槽位在合并后
互不重叠、索引正确（`ModuleGlobalBase + 原槽位`）。

### 6.4 不支持的重定位

`StructTypeId`（结构体类型 ID）在 `.stmod` 中没有稳定的跨模块身份
（格式不持久化结构体注册表），链接器遇到该 Kind 一律**拒绝**（§9），
**不**做猜测性重编号。

---

## 7. 链接状态与执行规则

### 7.1 可否直接执行

| 条件 | `ironst run <file.stmod>` |
|---|---|
| 无 `ImportSymbols` 且 `module.linked != false` | **允许**（执行入口函数或 `--entry`） |
| 存在 `ImportSymbols`，或 `module.linked == false` | **拒绝**："模块存在未解析的外部引用，请先 `mod link`" |
| 宿主 ABI 不兼容 | **拒绝**（`STModHostCompatibility`） |

### 7.2 入口规则

- 未指定 `--entry`：使用模块头部入口（`Flags.HasEntryPoint` + `module.entryPC`，即入口桩）。
  - **单模块**：直接用该模块的入口桩。
  - **链接**：取**首个声明了入口的输入模块**的入口（`module.entryFunction` 或带
    `EntryPoint` 标志的实现函数）。因此应把**主程序模块放在命令行最前**；
    需要精确选择时请显式传 `--entry`。
  - 若没有任何输入模块声明入口 → 产物不含入口（`HasEntryPoint=false`），
    此时 `run` 必须用 `--entry` 指定。
- `--entry NAME`：在函数索引中按**短名**查找：
  1. 命中唯一且**有可执行实现**（非 `Declaration`）→ 用之；
  2. 命中 0 个 → 报错并提示可用入口；
  3. 命中多个 → 报错，列出全部候选（函数 id / 模块限定名）供消歧；
  4. 命中唯一条目但为 `Declaration` / 宿主声明 → 报错："函数不可执行"。
- **绝不**默认选择第一个匹配项。
- 入口执行时，函数末尾 `RET` 返回到一段运行时插入的 `SYSCALL EXIT` 桩
  （仅在装载为 `BytecodeModule` 的内存副本上追加，**不改变文件**）。

---

## 8. 静态链接算法（P3）

输入：N 个 `.stmod`（顺序即命令行顺序）。输出：单个 `.stmod`。

1. 逐个读取并校验格式（§3 校验、必需区段、函数索引不变量）。
2. 校验格式版本、必需特性、`compiler.target`、宿主 ABI 兼容性。
3. 汇总**函数实现表**（= 各模块 `FunctionIndex` 中非 `Declaration` 的条目）；
   检测**重复定义**（同名函数在多个模块中均有实现）→ 拒绝并列出相关模块。
   （`Declaration` 且不在宿主环境中的才是外部引用；实现表即链接期的导出表。）
4. 解析所有导入符号；**未定义** → 拒绝。
5. 校验调用双方**签名一致**；不一致 → 拒绝。
6. 决定各模块 Code 的最终基址（按输入顺序拼接，8 字节对齐）。
7. 合并 `FunctionIndex`：重新分配 `FunctionId`、写入最终 `CodeOffset`/`CodeLength`。
8. 合并 `ConstantPool`：追加合并（不去重），建立 `ModuleConstBase`。
9. 合并全局槽位：建立 `ModuleGlobalBase`，复检总槽位 ≤ 32767。
10. 对每条重定位执行**有边界检查**的回填（§6）：偏移必须落在合并后区段内，宽度匹配。
11. 重建元数据（模块名、版本、`module.linked = true`、`module.globalCount`、导入/导出）。
12. 复检输出**不存在**未解析的强制重定位。
13. **重新读取**产物并做结构验证（同步骤 1 的校验）。
14. 原子写出（先写临时文件再替换），失败时不留下看似有效的半成品。

**确定性**：相同输入与选项产生逐字节相同的输出（元数据按 Key 序数排序；函数/区段顺序按输入顺序）。

---

## 9. 版本与拒绝规则（汇总）

| 情况 | 行为 |
|---|---|
| `FormatVersionMajor != 1` | 拒绝 |
| 未知**必需**区段 / 必需特性 | 拒绝 |
| `ImportSymbols` 标记 `Required` | 旧读取器拒绝；本写入器不产生 |
| 未知重定位 `Kind` | 拒绝（"不支持的重定位类型"） |
| `Addr32` 符号在链接后仍未解析 | 拒绝 |
| 输出含未解析强制重定位 | 拒绝（不产出） |
| 入口歧义 / 入口不可执行 / 入口缺失 | 拒绝并给出候选 |

---

## 10. 实现位置（P0–P5）

| 文件 | 职责 |
|---|---|
| `IronST.Runtime.Shared/Modules/STModFormat.cs` | 符号模型、新元数据 Key、区段类型 |
| `IronST.Runtime.Shared/Modules/STModReader.cs` | 新区段/新 Key 的解析与校验 |
| `IronST.Runtime.Shared/Modules/STModWriter.cs` | 新区段写出（可选） |
| `IronST.Runtime.Shared/Modules/STModLinker.cs` | 静态链接器（§8） |
| `IronST.Runtime.Shared/Modules/STModLoader.cs` | `.stmod` → `BytecodeModule`（执行装载） |
| `IronST.CLI/Commands/RunCommand.cs` | `.stmod` 执行 + `--entry` 选择 |
| `IronST.CLI/Commands/ModCommand.cs` | `mod build --ref` / `mod link` / `mod inspect` / `mod symbols` |

---

## 11. `.stproj` 集成（P5）

### 11.1 新增属性：`STModuleReference`

在项目的任意 `<ItemGroup>` 中声明要**静态链接进产物**的 `.stmod`：

```xml
<STProject Version="1">
  <PropertyGroup>
    <Name>App</Name>
    <StartupProgram>Main</StartupProgram>
  </PropertyGroup>
  <ItemGroup>
    <STCompile Include="Main.st" />
  </ItemGroup>
  <ItemGroup>
    <STModuleReference Include="lib/Math.stmod" />
  </ItemGroup>
</STProject>
```

- `Include` 相对**项目文件所在目录**解析（与 `STCompile` 一致），必须是 `.stmod`。
- 校验：`Include` 非空、扩展名 `.stmod`、去重、文件必须存在；任一不满足 → 项目加载失败。
- 该元素为**可选新增**：不写它的现有项目文件行为完全不变（向后兼容）。

### 11.2 `mod build <proj.stproj>` 的语义

| 项目是否声明 `STModuleReference` | `mod build <proj.stproj>` 的产物 |
|---|---|
| **是** | 编译本项目的源文件，再与这些模块**真正静态链接**为**一个自足模块**：`module.linked = true`、`Imports = []`、入口桩指向选定入口。**脱离全部输入仍可运行**。 |
| **否** | 与 `.st` 输入 + `--ref` 完全一致：注入被引模块的**导出声明**、保留未解析外部引用（`module.linked = false`），留待后续 `mod link`。 |

- 命令行 `--ref` 与项目内 `STModuleReference` **合并**（按绝对路径去重）；
  链接时按"本项目模块在前、引用模块在后"的顺序拼接。
- 链路复用同一套 `STModLinker`（§8），**不新增第二套链接实现**。
- 入口选择：
  - `--entry NAME` → 传给链接器（须唯一匹配，否则按 §7.2 报歧义）；
  - 未给 `--entry` → 用本项目编译产物的入口（因本项目模块排在最前，§7.2 的
    "首个声明入口的模块"规则自然选中它）。
- 链接失败（未解析引用、重复定义、签名不一致、入口歧义/不可执行……）→
  **不产出文件**（原子写，§8 步骤 14）。

### 11.3 未涉及的既有行为

- `mod build <proj.stproj> -o out.stmod` 之外的项目命令（`build`/`run`/`expand`
  对 `.stproj`）**行为不变**；其中 `run <proj.stproj>` 仍只按项目源码编译，
  不读取 `STModuleReference`（需要运行链接后的模块时，请先 `mod build` 再
  `run <产物.stmod>`）。
