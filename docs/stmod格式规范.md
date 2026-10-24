# `.stmod` 二进制模块格式规范（v1.0）

> 状态：**v1 已实现**（写入器 / 读取器 / `ironst mod build` / `ironst mod inspect`）。
> 规范和实现同批落地；本文是读取器的行为契约，测试按本文独立解析产出的字节流。

---

## 0. 定位与边界

| 文件 | 用途 | 魔数 | 是否可执行 |
|---|---|---|---|
| `.isb` | 最终可执行字节码 | `0x49525354`（"IRST"） | 是，由 VM 直接执行 |
| `.stmod` | 编译器读取 / 链接的**模块输入** | `"STMD"` | **否**，`ironst run` 明确拒绝 |

`.stmod` **不是**"改了扩展名的 `.isb`"：

- 有固定头 + 区段表，可按区段定位、可扩展；
- 有可扩展 Key-Value 元数据；
- 有**真实编译产物**的函数索引（函数在 Code 区段中的精确字节范围）；
- 有独立的格式版本与校验机制；
- 与 `.isb` 不共用文件头，读取器互相拒绝对方的文件。

链接流程目标（**v1 只实现前半段**）：

```
.st 源文件 → 编译 → .stmod → [其他项目导入 .stmod] → 链接 → .isb → VM 执行
                          ^^^^^^^^^^^^^^^^^^^^^^^^ v1 未实现，见 §9
```

---

## 1. 通用约定

| 项目 | 规定 |
|---|---|
| 字节序 | **一律 Little Endian**（无例外、无平台相关项） |
| 字符串 | UTF-8，**无 BOM、无结尾 NUL**，长度以**字节数**计 |
| 对齐 | 所有区段起始偏移按 `alignment` 对齐（v1 恒为 8）；区段之间的空隙必须写 0 |
| 保留字段 | 一律写 0；读取器见到非 0 即拒绝（为未来语义预留，避免"旧读取器误解新语义"） |
| 长度字段 | 一律无符号；读取前必须做上界校验与**先比较、再相减**的溢出安全判断 |

---

## 2. 固定文件头（固定 48 字节）

| 偏移 | 长度 | 字段 | 类型 | 说明 |
|---|---|---|---|---|
| 0 | 4 | `Magic` | byte[4] | ASCII `"STMD"` |
| 4 | 2 | `FormatVersionMajor` | u16 | 当前 `1`；读取器**只接受 1** |
| 6 | 2 | `FormatVersionMinor` | u16 | 当前 `0`；同主版本内向前兼容 |
| 8 | 4 | `Flags` | u32 | 见 §2.1 |
| 12 | 4 | `HeaderSize` | u32 | 固定 `48`；不等即拒绝 |
| 16 | 4 | `SectionCount` | u32 | 区段条数，上限 4096 |
| 20 | 4 | `Reserved0` | u32 | 必须为 0 |
| 24 | 8 | `SectionTableOffset` | u64 | 区段表起始偏移（v1 = 48） |
| 32 | 4 | `MetadataSectionId` | u32 | 元数据区段的 `SectionId` |
| 36 | 4 | `Reserved1` | u32 | 必须为 0 |
| 40 | 8 | `Reserved2` | u64 | 必须为 0 |

### 2.1 `Flags`

| 位 | 名称 | 含义 |
|---|---|---|
| 0 | `HasEntryPoint` | 有入口点；此时必须存在 UInt32 类型元数据 `module.entryPC`，且其值 `< Code 区段长度` |
| 1–31 | — | 保留，必须为 0 |

> **格式版本 ≠ 模块版本**：`FormatVersionMajor/Minor` 描述**二进制布局**；
> `module.version` 元数据描述**模块自身**的版本。两者互不影响，不得互相校验。

---

## 3. 区段表

从 `SectionTableOffset` 起，连续 `SectionCount` 条，每条 **32 字节**：

| 偏移 | 长度 | 字段 | 类型 | 说明 |
|---|---|---|---|---|
| 0 | 4 | `SectionId` | u32 | 文件内唯一 |
| 4 | 2 | `SectionType` | u16 | 见 §3.1 |
| 6 | 2 | `Flags` | u16 | 见 §3.2 |
| 8 | 8 | `Offset` | u64 | 相对**文件起点**的绝对偏移 |
| 16 | 8 | `Size` | u64 | 负载字节数 |
| 24 | 4 | `Alignment` | u32 | 必须是 2 的幂；`Offset % Alignment == 0` |
| 28 | 4 | `Reserved` | u32 | 必须为 0 |

### 3.1 区段类型

| 值 | 名称 | v1 | 必需性 |
|---|---|---|---|
| 1 | `Metadata` | 支持 | **必需** |
| 2 | `FunctionIndex` | 支持 | **必需** |
| 3 | `Code` | 支持 | **必需** |
| 4 | `ConstantPool` | 支持 | **必需** |
| 5 | `ImportSymbols` | **不支持** | 若标记必需 → 拒绝；可选 → 忽略 |
| 6 | `ExportSymbols` | 信息性 | 忽略 |
| 7 | `Relocations` | 支持（解析并存） | 可选 |
| 8 | `DebugInfo` | 信息性 | 忽略 |
| `0x8000`–`0xFFFF` | 私有扩展 | 未知 | 未知类型 + 必需 → 拒绝；未知 + 可选 → 忽略 |

### 3.2 区段 `Flags`

| 位 | 名称 | 含义 |
|---|---|---|
| 0 | `Required` | 读取器不认识或无法处理时**必须拒绝整个文件**，不得静默忽略 |
| 1–15 | — | 保留 |

### 3.3 区段校验（读取器）

1. 区段表本身不得越界：`tableOffset >= HeaderSize` 且 `SectionCount × 32 <= FileLength - tableOffset`。
2. 每个区段：`Offset >= HeaderSize`（不得压在固定头内）；`Alignment` 为 2 的幂；`Offset % Alignment == 0`。
3. 每个区段必须完全落在文件内：`Offset <= FileLength` 且 `Size <= FileLength - Offset`。
4. **区段之间不得重叠**，且不得压在固定头或区段表上。
5. §3.1 列出的四个必需类型必须**各恰好出现一次**。
6. 未知区段类型带 `Required` 位 → 拒绝。

---

## 4. 元数据区段

负载布局：

| 偏移 | 长度 | 字段 | 说明 |
|---|---|---|---|
| 0 | 4 | `RecordCount` | 上限 65536 |
| 4 | 4 | `Reserved0` | 必须为 0 |
| 8 | 4 | `Reserved1` | 必须为 0 |
| 12 | 4 | `Reserved2` | 必须为 0 |
| 16 | — | 记录序列 | |

每条记录（定长头 **12 字节**）：

| 偏移 | 长度 | 字段 | 说明 |
|---|---|---|---|
| 0 | 4 | `KeyLength` | UTF-8 字节数，允许 `1..1024` |
| 4 | 1 | `ValueType` | 见 §4.1 |
| 5 | 1 | `Flags` | v1 必须为 0 |
| 6 | 2 | `Reserved` | 必须为 0 |
| 8 | 4 | `ValueLength` | 负载字节数 |
| 12 | `KeyLength` | `Key` | UTF-8 |
| … | `ValueLength` | `Value` | 依 `ValueType` 解释 |

### 4.1 值类型

| 值 | 名称 | `ValueLength` 约束 | 编码 |
|---|---|---|---|
| 1 | `String` | 0..1 MiB | UTF-8 字节 |
| 2 | `Boolean` | 必须 = 1 | `0x00` = false，`0x01` = true（其它值非法） |
| 3 | `Int32` | 必须 = 4 | 小端 i32 |
| 4 | `Int64` | 必须 = 8 | 小端 i64 |
| 5 | `UInt32` | 必须 = 4 | 小端 u32 |
| 6 | `UInt64` | 必须 = 8 | 小端 u64 |
| 7 | `ByteArray` | 0..1 MiB | 原始字节 |

### 4.2 Key 规则

- Key 必须非空、UTF-8 字节数 `1..1024`、**不含**控制字符（`< 0x20`）。
- **重复 Key：直接拒绝**（确定性规则 —— 不"取第一个"、不"取最后一个"、不合并）。写入器同样拒绝。
- **未知 Key：忽略**（这是可扩展性的基础）。新增元数据**不需要**改动固定文件头。
- 比较用**序数**（区分大小写）。

### 4.3 已知 Key

| Key | 类型 | 必需 | 含义 |
|---|---|---|---|
| `module.name` | String | **是** | 模块名，非空 |
| `module.version` | String | **是** | 模块自身版本（**不是**格式版本） |
| `compiler.version` | String | **是** | 产出该模块的编译器版本 |
| `module.description` | String | 否 | 描述 |
| `module.author` | String | 否 | 作者 |
| `module.license` | String | 否 | 许可证 |
| `compiler.target` | String | 否 | 目标执行环境标识（如 `ironst-vm/1`） |
| `module.debug` | Boolean | 否 | 是否含调试信息 |
| `module.requiredFeatures` | String | 否 | 逗号分隔的必需特性，见 §4.4 |
| `module.entryFunction` | String | 否 | 入口函数名（信息性） |
| `module.entryPC` | UInt32 | 条件必需 | 当 `Flags.HasEntryPoint` 置位时必需 |
| `module.sources` | String | 否 | 自定义 Key 示例：`;` 分隔的源文件名 |

### 4.4 `module.requiredFeatures`

逗号分隔。v1 支持的取值：`bytecode`、`constant-pool`、`relocations`。
出现**任何不支持的取值 → 拒绝**（旧读取器必须拒绝不认识的必需特性，而不是猜测）。

### 4.5 读取时的防御

- `KeyLength` / `ValueLength` 超限 → 拒绝。
- 记录负载超出元数据区段 → 拒绝（**不得**按声明的长度分配内存）。
- 必需 Key 缺失 → 拒绝。

---

## 5. 函数索引区段

负载布局：

| 偏移 | 长度 | 字段 |
|---|---|---|
| 0 | 4 | `EntryCount`（上限 1,000,000） |
| 4 | 12 | 保留 3×u32 = 0 |
| 16 | — | 条目序列 |

每条目（定长头 **48 字节**）：

| 偏移 | 长度 | 字段 | 说明 |
|---|---|---|---|
| 0 | 4 | `FunctionId` | 文件内唯一 |
| 4 | 4 | `Flags` | 见 §5.1 |
| 8 | 4 | `CodeSectionId` | 必须指向 `Code` 区段的 `SectionId` |
| 12 | 4 | `NameLength` | `1..1 MiB` |
| 16 | 4 | `SignatureLength` | `0..1 MiB` |
| 20 | 4 | `ParamCount` | 语言层参数个数 |
| 24 | 4 | `LocalCount` | 帧内局部槽位数（来自真实 `FRAME` 操作数） |
| 28 | 4 | `MaxStack` | **恒为 0**（现有 VM 为定长帧栈，无逐函数 max stack；字段保留） |
| 32 | 8 | `CodeOffset` | **Code 区段内**的相对偏移 |
| 40 | 8 | `CodeLength` | 必须 `> 0` |
| 48 | `NameLength` | `Name` | UTF-8 |
| … | `SignatureLength` | `Signature` | UTF-8，形如 `DINT (DINT, DINT)` |

### 5.1 `Flags`

| 位 | 名称 | 含义 |
|---|---|---|
| 0 | `Exported` | 模块公开（未来可被其它模块导入） |
| 1 | `Declaration` | 仅声明、无实质函数体（宿主函数声明等） |
| 2 | `EntryPoint` | 模块入口函数 |
| 3–31 | — | 保留 |

### 5.2 定位公式

```
文件内绝对偏移 = CodeSection.Offset + FunctionEntry.CodeOffset
```

### 5.3 校验

- `FunctionId` 唯一；`Name` 非空且唯一。
- `CodeLength > 0`。
- **溢出安全**：`CodeOffset <= CodeSize` 且 `CodeLength <= CodeSize - CodeOffset`。
- **区间不重叠**（同一 `CodeSectionId` 内）。
- `CodeSectionId` 必须指向存在的 `Code` 区段。

### 5.4 索引来源（关键不变量）

函数范围**不是**按源码行号或函数排列顺序推断的，而是编译器在
`BytecodeAssembler.DefFunc` 中发射完 `FRAME … LEAVE RET` 之后记录的 `PC`：

```
StartPC = DefFunc 进入时的 PC
EndPC   = 发射完 RET 之后的 PC（开区间）
CodeLength = EndPC - StartPC
```

因此以下两条**不变量**恒成立，并由测试直接验证（不依赖"函数连续排列"的假设）：

```
Code[StartPC]           == FRAME (0x24)
Code[EndPC - 2]         == LEAVE (0x25)
Code[EndPC - 1]         == RET   (0x80)
```

---

## 6. Code 区段

- 负载 = 现有编译器产出的**原始字节码**，与 `BytecodeModule.Code` 逐字节相同。
- 不重排、不重编码、不引入第二套指令集。
- 指令中的调用目标 / 跳转偏移 / 常量池序号 / 宿主 ID 都是**编译期已定值**（`.isb` 运行期不需要重定位段）。因此模块**不可直接执行**：跨模块合并需要改写这些定值，见 §9。

---

## 7. ConstantPool 区段

| 偏移 | 长度 | 字段 |
|---|---|---|
| 0 | 4 | `Count` |
| 4 | 4 | 保留 = 0 |
| 8 | — | 值序列 |

每个值与 `.isb` 的常量池编码**完全一致**（同一份 `BcValueSerializer` 实现）：

```
byte tag          // ValueTag: 1=Int64, 2=Float64, 3=String
byte padding[7]   // 0
payload           // Int64: 8B | Float64: 8B | String: i32 字节数 + UTF-8 + 对齐填充
```

字符串常量负载按 8 字节对齐填充；单个字符串上限 64 KiB。

---

## 8. Relocations 区段（可选）

负载 = `RelocationTable.Serialize()` 的既有编码（七字段记录：`Kind`、`Section`、`Offset`、
`SymbolName`、`Source`、`EncodingWidthBits`、`Addend`）。v1 **只解析与展示**，不应用。

`.isb` 没有重定位段，所以"把模块合并为可执行文件"的信息必须先存在于 `.stmod` —— 这正是本区段存在的理由。

### 8.1 宿主函数兼容性

**不把编译期宿主函数 ID 视为永久稳定**：

- 动态注册的宿主函数（`Print`、IEC 字符串函数）的 ID 来自编译期 `hostFunctions` 名→ID 表；
  重定位记录里是**模块局部的 ID**（`HostIdx` 记录的 `Addend`）。
- 加载时必须按**名字**在当前宿主环境中重新核对 ID；名字缺失或 ID 不同 → 拒绝。
  `ironst mod inspect` 会执行这项只读校验（`STModHostCompatibility.Check`）并打印结论。
- 静态 ABI ID（`HostCallIds` 常量）不进重定位表（既有 HOST-2 裁定），
  其兼容性由格式版本 + `compiler.target` 约束。

---

## 9. v1 未实现的能力（明确拒绝，而不是猜测）

| 能力 | v1 行为 |
|---|---|
| 直接执行 `.stmod` | `ironst run` 拒绝并给出指引 |
| 模块间调用 / 导入 | 不支持。`ImportSymbols` 若标记必需 → 拒绝；模块内未解析的外部函数在**编译期**就会失败（前端报未声明函数），不会产出"能通过格式校验但运行必错"的模块 |
| 模块链接为 `.isb` | 未实现，见 §9.1 |
| `ExportSymbols` / `DebugInfo` | 忽略（信息性） |

### 9.1 链接为 `.isb` 的阻碍与最小设计

**已具备**：Code、ConstantPool、函数索引、重定位记录都已随模块持久化。

**阻碍**：多模块合并需要改写 Code 字节流里的定值：

| 需要改写 | 依据 | 现状 |
|---|---|---|
| 调用/跳转目标（`Addr32`） | 重定位记录 | 记录已在模块中；缺"链接器" |
| 条件跳转相对偏移（`Off8`） | 重定位记录 | 同上 |
| 常量池序号（`ConstIdx`） | 重定位记录 | 合并池后需重编号 |
| 全局槽位（`GlobalIdx`） | 重定位记录 | 需按模块基址平移 |
| 宿主 ID（`HostIdx`） | 按名重解析 | 需按名而不是按 ID 合并 |

**最小可行设计**（下一步，不在本阶段范围）：

1. 链接器读入 N 个 `.stmod`，按 `Section` 顺序拼接 Code 与 ConstantPool；
2. 用每模块的 `Relocations` 逐条回填（目标地址 = 模块基址 + 符号已解析偏移）；
3. 宿主 `HostIdx` 按名在当前环境中重新解析；
4. 未解析符号 → 明确报错，**不得**留下原值让 VM 执行；
5. 产出 `.isb`，由现有 VM 执行 —— 不新增安全检查绕过路径。

---

## 10. 版本兼容规则

| 情况 | 读取器行为 |
|---|---|
| `FormatVersionMajor != 1` | 拒绝（"不支持的格式版本"） |
| `FormatVersionMinor` 更大 | 接受（次版本向前兼容语义：只增可选区段/可选 Key） |
| 未知**可选**区段 | 忽略并在 `inspect` 中列出 |
| 未知**必需**区段 | 拒绝 |
| 未知元数据 Key | 忽略 |
| `module.requiredFeatures` 含不支持项 | 拒绝 |

---

## 11. 实现位置

| 文件 | 职责 |
|---|---|
| `IronST.Runtime.Shared/Modules/STModFormat.cs` | 常量、枚举、内存模型、异常 |
| `IronST.Runtime.Shared/Modules/STModWriter.cs` | 序列化（含写侧校验） |
| `IronST.Runtime.Shared/Modules/STModReader.cs` | 反序列化 + 格式校验 + 宿主兼容性校验 |
| `IronST.Runtime.Shared/BcValueSerializer.cs` | 常量池编解码（`.isb` / `.stmod` 共用） |
| `IronST.Runtime.Shared/BytecodeAssembler.cs` | `FunctionRanges`：真实函数字节范围 |
| `IronST.BytecodeGen/ByteCodeGenerator.cs` | `CompileResult.FunctionLayout` |
| `IronST.CLI/Commands/ModCommand.cs` | `mod build` / `mod inspect` |
