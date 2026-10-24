# IronST.LLVMCodeGen 接口摘要

IEC 61131-3 ST 语言 → LLVM IR 代码生成器，基于 `IronST.IRBuilder.IR` 对象模型。目标：x86_64-pc-windows-msvc。

---

## 命名空间

| 命名空间 | 职责 |
|----------|------|
| `IronST.LLVMCodeGen.CodeGen` | 代码生成核心（上下文、表达式、语句、序言、内置函数） |
| `IronST.LLVMCodeGen.TypeMapping` | IEC 类型 → LLVM IR 类型映射 |
| `IronST.LLVMCodeGen.Debug` | 调试信息发射接口 |
| `IronST.LLVMCodeGen.Exceptions` | 代码生成异常 |

---

## 代码生成流水线（IRGenerator）

入口：`new IRGenerator(debug?).Generate(AnalysisResult) → Module`

**三遍处理顺序：**

| 遍次 | 动作 | 说明 |
|------|------|------|
| 1 | 注册类型声明（TYPE）、FB 结构体类型、全局变量（VAR_GLOBAL） | 建立类型映射表 |
| 2 | 创建所有函数声明（Function/Program/FunctionBlock） | 建立前向引用 |
| 3 | 发射函数体（含 FB ACTION 的独立函数） | 实际生成 IR |

**POU 类型处理差异：**

| POU 类型 | 函数签名 | 特殊逻辑 |
|----------|----------|----------|
| `FunctionNode` | 返回值类型 + 形参（Input 传值，Output/InOut 传指针） | 返回值绑定到函数名 alloca |
| `ProgramNode` | `void()` 无参数 | 无返回值 |
| `FunctionBlockNode` | `void(ptr __instance, ...)` | 第一个参数为 FB 实例指针；FB ACTION 为独立函数 `{FBName}.{ActionName}`，签名与 FB 相同 |

**全局变量注册（VAR_GLOBAL）：**
- 遍历 `analysis.Variables`，过滤 `VarBlockKind == "VAR_GLOBAL"`
- 创建 `GlobalVariable`（Linkage=External，IsConstant=false），零初始化
- 通过 `ctx.RegisterGlobalVariable(name, gv)` 注册

---

## CodeGenContext（代码生成上下文）

`new CodeGenContext(Module, TypeMapper, IDebugEmitter)`

### 公共属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Module` | `Module` | IR 模块容器 |
| `TypeMapper` | `TypeMapper` | IEC→IR 类型映射器 |
| `NameGen` | `NameGenerator` | 唯一名称生成器 |
| `Debug` | `IDebugEmitter` | 调试信息发射器 |
| `CurrentFunction` | `Function` | 当前正在发射的函数（可写） |
| `Builder` | `IRBuilder` | 当前 IR 构建器（可写） |
| `IsInPrologue` | `bool` | 是否在序言阶段（可写） |
| `LoopEndBlocks` | `Stack<BasicBlock>` | 循环结束块栈（供 EXIT 使用） |

### 六大注册表

| 注册表 | 键类型 | 值类型 | 大小写 | 方法 |
|--------|--------|--------|--------|------|
| Alloca 映射 | `string`（变量名） | `Value` | 区分 | `BindAlloca` / `GetAlloca` / `TryGetAlloca` / `ClearAllocaMap` |
| 变量类型映射 | `string`（变量名） | `IECType` | 区分 | `BindVarType` / `GetVarType` / `TryGetVarType` |
| 用户自定义类型 | `string`（类型名） | `IECType` | **不区分** | `RegisterTypeDef` / `GetTypeDef`（可返回 null） |
| 函数注册表 | `string`（函数名） | `Function` | 区分 | `RegisterFunction` / `ResolveFunction` / `TryResolveFunction` |
| 函数形参表 | `string`（函数名） | `List<VariableSymbol>` | **不区分** | `RegisterFormalParams` / `GetFormalParams`（找不到返回空列表） |
| FB 实例类型 | `string`（FB 名） | `IECType` | **不区分** | `RegisterFBInstanceType` / `GetFBInstanceType`（可返回 null） |
| 全局字符串 | `string`（如 `str.1`） | `GlobalVariable` | 区分 | `RegisterGlobalString` / `ResolveGlobalString` |
| 全局变量 | `string`（变量名） | `GlobalVariable` | **不区分** | `RegisterGlobalVariable` / `GetGlobalVariable` / `TryGetGlobalVariable` |

### 静态工具方法

```csharp
static int DefaultAlign(IrType type)
```

| 类型 | 对齐 |
|------|------|
| `Int1` / `Int8` | 1 |
| `IntegerType` (BitWidth ≤ 16) | 2 |
| `Int32` / `Float` | 4 |
| `Int64` / `Double` / `Pointer` | 8 |
| 其他 | 4 |

---

## FrameBuilder（序言构建器）

静态工具类，为函数体构建 entry 块及变量分配。

```csharp
static IRBuild BuildPrologue(ctx, func, varBlocks, returnType, functionName, isFunctionBlock)
```

**序言处理顺序（按 VarBlock 源码顺序）：**

| VarScope | 行为 |
|----------|------|
| 返回值 | `alloca` → 绑定到 `functionName`（仅非 void 函数） |
| FB 实例 | 第一个参数绑定到 `__instance`（仅 `isFunctionBlock=true`） |
| `Input` | `alloca` → `store param → alloca`（传值语义） |
| `Output` / `InOut` | 直接绑定参数指针（传地址语义） |
| `Local` / `Temp`（非 FB） | `alloca` 绑定 |
| `Local` / `Temp`（FB 内） | 通过 GEP 从 `__instance` 指针访问 FB 成员 |
| `Global` / `External` | 从 `ctx.GetGlobalVariable` 获取并绑定 |

**类型解析链：**
```
DataTypeNode → SimpleDataTypeNode → LookupBuiltin(name) || GetTypeDef(name)
                ArrayDataTypeNode → MakeArray(element, dims)
                ReferenceDataTypeNode → MakeReference(type, ReferencedTypeName)
                (含 STRING[n] / WSTRING[n] 特殊处理)
```

---

## ExpressionEmitter（表达式发射器）

`new ExpressionEmitter(CodeGenContext ctx)`

### 公共入口

| 方法 | 功能 |
|------|------|
| `Emit(ExpressionNode)` | 发射表达式，返回 `Value` |
| `EmitAddress(ExpressionNode)` | 计算左值地址（仅支持 `VariableExprNode`） |
| `EmitCall(name, arguments, span)` | 发射函数调用表达式 |
| `EnsureType(value, fromType, toType)` | 类型转换（公开供外部使用） |

### 支持的表达式类型（Emit 内部 dispatch）

| AST 节点 | 方法 | 说明 |
|----------|------|------|
| `LiteralExprNode` | `EmitLiteral` | Integer/BasedInteger → `ConstantInt`；Real → `ConstantFP`；Boolean → `i1`；String → 全局字符串；Time/Date/TOD/DateTime → `i64 ConstantInt` |
| `TypedLiteralExprNode` | `EmitTypedLiteral` | `TypeName#RawValue` 格式 |
| `VariableExprNode` | `EmitVariableLoad` | 先检查枚举常量；单段无后缀直接 load；多段/后缀计算地址后 load |
| `BinaryExprNode` | `EmitBinaryExpr` | 比较/算术/逻辑/幂运算 |
| `UnaryExprNode` | `EmitUnaryExpr` | Neg（整数=Sub, 浮点=FSub）/ Not（Xor i1） |
| `FunctionCallExprNode` | `EmitFunctionCallExpr` | FB 实例调用 → 内置函数 → 用户函数 |

### 变量地址计算（ComputeVariableAddress）

1. 获取第一段变量的 alloca 指针及类型
2. 应用第一段的后缀（解引用 `^` / 数组下标 `[]`）
3. 遍历后续段：通过 `GetFieldIndex` + GEP 访问结构体成员，再应用后缀

**数组下标处理：** 若维度下界非 0，自动插入 `Sub(idx, lowerBound)` 偏移调整。

### 二元运算支持矩阵

| 运算 | 整数 | 浮点 | 布尔 |
|------|------|------|------|
| Add/Sub/Mul/Div/Mod | ✓ | ✓ | — |
| And/Or/Xor | ✓ | — | ✓ |
| Power (`**`) | → `pow` (cast to double → cast back) | → `pow` | — |
| Eq/Ne/Lt/Gt/Lte/Ggte | → `icmp` | → `fcmp` | → `icmp` |

**枚举成员常量：** 当 `ResolvedType` 为 ENUM 或第一段为已注册枚举类型名时，直接返回 `ConstantInt(i32, memberValue)`。

### 类型转换规则（EnsureType）

| 源 → 目标 | 操作 |
|-----------|------|
| 整数 → 整数（宽） | `SExt`（有符号）/ `ZExt`（无符号） |
| 整数 → 整数（窄） | `Trunc` |
| 整数 → 浮点 | `SIToFP`（有符号）/ `UIToFP`（无符号） |
| 浮点 → 整数 | `FPToSI`（有符号）/ `FPToUI`（无符号） |
| 浮点 → 浮点（宽） | `FPExt` |
| 浮点 → 浮点（窄） | `FPTrunc` |

### 函数调用参数解析

- **位置参数**：按顺序匹配形参；Output/InOut 形参传地址（`EmitAddress`）
- **命名参数**：按 `Name` 匹配形参，未匹配的位置为 null
- **FB 实例调用**：自动将实例指针作为第一个参数插入

---

## StatementEmitter（语句发射器）

`new StatementEmitter(CodeGenContext ctx, ExpressionEmitter expr)`

### 方法

| 方法 | 功能 |
|------|------|
| `Emit(StatementNode)` | 发射单条语句 |
| `EmitAll(IEnumerable<StatementNode>?)` | 批量发射（null 安全） |

### 支持的语句类型

| AST 节点 | 方法 | IR 结构 |
|----------|------|---------|
| `AssignmentNode` | `EmitAssignment` | `Emit(value)` → `EmitAddress(target)` → `store` |
| `RefAssignmentNode` | `EmitRefAssignment` | `EmitAddress(target)` ← `EmitAddress(value)` → `store` |
| `IfNode` | `EmitIf` | 展平 ELSIF 为链式 `condBr`：cond→then / elsif.cond→elsif.then / else / end |
| `ForNode` | `EmitFor` | init → cond(Sle) → body → inc(Add) → cond / end。步长默认 1 |
| `WhileNode` | `EmitWhile` | cond → body → cond / end |
| `RepeatNode` | `EmitRepeat` | body → until(condBr: end/body)。无 UNTIL 条件则无限循环 |
| `CaseNode` | `EmitCase` | `SwitchInst`；范围标签展开为多个 case；枚举标签经 `ResolveEnumCaseValue` 解析为成员值 |
| `CallStatementNode` | `EmitCallStatement` | 委托给 `ExpressionEmitter.EmitCall` |
| `ReturnNode` | `EmitReturn` | void→`ret void`；非 void→load 返回值 alloca → `ret` |
| `ExitNode` | `EmitExit` | `br` 到 `LoopEndBlocks.Peek()`；插入 unreachable 块续接 |

**循环结束块栈：** `LoopEndBlocks` 在进入 FOR/WHILE/REPEAT 时 Push，退出时 Pop。EXIT 语句必须在循环内，否则抛 `CodeGenException`。

---

## NameGenerator（名称生成器）

`new NameGenerator()`

| 方法 | 说明 |
|------|------|
| `NextValue(hint?)` | 生成唯一值名：`hint1, hint2, ...` 或 `v1, v2, ...` |
| `NextBlock(hint)` | 生成唯一块名：`hint1, hint2, ...`（hint 默认 `"bb"`，按 hint 独立计数） |
| `ResetFunction()` | 清零计数器（每次发射函数体前调用） |

---

## 异常体系

### CodeGenException

```csharp
new CodeGenException(message)
new CodeGenException(message, sourceFile, sourceLine)  // 格式：[file:line] message
new CodeGenException(message, innerException)
```

属性：`SourceFile?`, `SourceLine`

### TypeMappingException（继承 CodeGenException）

```csharp
new TypeMappingException(message)
new TypeMappingException(iecTypeName, reason)  // 格式：无法映射 IEC 类型 'name': reason
```

属性：`IecTypeName?`

---

## 调试信息（IDebugEmitter）

```csharp
interface IDebugEmitter
{
    bool IsEnabled { get; }
    void Initialize(Module module, string fileName, string directory);
    void EnterFunction(Function func, string name, string sourceFile, int startLine);
    void SetLocation(int line);
    void ClearLocation();
    void AttachLocation(Instruction inst);
    void Finalize();
}
```

`NullDebugEmitter`：空实现，`IsEnabled = false`，所有方法无操作。

**调试位置绑定规则：**
- 仅在 `IsEnabled && !IsInPrologue` 时，对 `Instruction` 类型的 Value 调用 `AttachLocation`
- 序言阶段 `ClearLocation()`，发射语句前 `SetLocation(line)`

---

## 内置函数（BuiltinFunctions）

统一识别与分发中枢。

```csharp
static bool IsBuiltin(string name)
static Value? TryEmit(ctx, exprEmitter, name, arguments, span)
```

返回 `null` 表示非内置函数，应回退到常规函数调用路径。

### 分类总览

| 类别 | 函数列表 | 分发目标 |
|------|----------|----------|
| 类型转换 | `BOOL, SINT, INT, DINT, LINT, USINT, UINT, UDINT, ULINT, BYTE, WORD, DWORD, LWORD, REAL, LREAL, TIME, DATE, TOD, DT` | `TypeConversionFunctions.Emit` |
| 数学 | `ABS, SQRT, LN, LOG, EXP, SIN, COS, TAN, ASIN, ACOS, ATAN, ATAN2, EXPT` | `MathFunctions.Emit` |
| 字符串 | `LEN, LEFT, RIGHT, MID, CONCAT, INSERT, DELETE, REPLACE, FIND` | `StringFunctions.Emit` |

### TypeConversionFunctions

```csharp
static Value Emit(ctx, exprEmitter, name, arguments, span)
```

- **BOOL**：整数/布尔 → `icmp ne %arg, 0`；浮点 → `fcmp one %arg, 0.0`
- **其他类型**：委托给 `exprEmitter.EnsureType(argVal, argType, toType)`

### MathFunctions

**ST 名 → C extern 名映射：**

| ST | C extern |
|----|----------|
| `SQRT` | `sqrt` |
| `LN` | `log` |
| `LOG` | `log10` |
| `EXP` | `exp` |
| `SIN/COS/TAN` | `sin/cos/tan` |
| `ASIN/ACOS/ATAN` | `asin/acos/atan` |

**实现策略：**

| 函数 | 策略 |
|------|------|
| `ABS` | 整数：内联 `select (x < 0), (0 - x), x`；浮点：调用 `fabs` |
| `SQRT/LN/LOG/EXP/SIN/COS/TAN/ASIN/ACOS/ATAN` | 参数提升到 `LREAL` → 调用对应 C extern 函数（返回 `double`） |
| `ATAN2(y, x)` | 两参数提升到 `LREAL` → 调用 `atan2` |
| `EXPT(base, exp)` | 两参数提升到 `LREAL` → 调用 `pow` |

所有 extern 函数声明通过 `EnsureExternDecl` 缓存到 `ctx.RegisterFunction`，避免重复声明。

### StringFunctions

字符串 IR 表示：`{i32, [N x i8]}` 结构体，字段 0 为长度（i32），字段 1 为字符数组。

**内部工具方法：**

| 方法 | 功能 |
|------|------|
| `GetStringIrType(ctx, expr)` | 获取表达式的字符串 IR 结构类型 |
| `EnsureStringPointer(ctx, exprEmitter, expr)` | 确保表达式求值为字符串指针 |
| `GetStringLength(ctx, ptr, strIrType)` | GEP(field 0) → load i32 |
| `SetStringLength(ctx, ptr, strIrType, len)` | GEP(field 0) → store i32 |
| `GetStringDataPtr(ctx, ptr, strIrType)` | GEP(field 0, field 0) 获取字符数组指针 |
| `SelectMin(ctx, a, b, name)` | select(a <= b, a, b) |
| `EmitMemcpy(ctx, dst, src, len)` | 逐字节循环拷贝内联（i8 循环） |
| `EnsureMemcmpDeclaration(ctx)` | 声明/缓存 `i32 memcmp(ptr, ptr, i64)` extern 函数 |

**各函数签名与行为：**

| 函数 | 签名 | 返回 | 说明 |
|------|------|------|------|
| `LEN` | `LEN(input)` | `INT` | 字符串长度 |
| `LEFT` | `LEFT(input, length)` | `STRING` | 取左侧 min(n, srcLen) 字符 |
| `RIGHT` | `RIGHT(input, length)` | `STRING` | 取右侧 min(n, srcLen) 字符 |
| `MID` | `MID(input, length, start)` | `STRING` | 从 start(1-based) 取 min(length, avail) 字符 |
| `CONCAT` | `CONCAT(in1, in2)` | `STRING` | 拼接两字符串 |
| `FIND` | `FIND(in1, in2)` | `INT` | 查找子串位置（1-based，0=未找到），内联循环 + memcmp |
| `INSERT` | `INSERT(in1, in2, position)` | `STRING` | 在 position(1-based) 处插入 in2 |
| `DELETE` | `DELETE(input, length, start)` | `STRING` | 从 start(1-based) 删除 length 个字符 |
| `REPLACE` | `REPLACE(in1, in2, length, start)` | `STRING` | 从 start(1-based) 替换 length 个字符为 in2 |

---

## 关键设计约束与注意事项

1. **Alloca 映射作用域**：每次发射函数体前 `ClearAllocaMap()`，仅当前函数的变量有效
2. **FB 成员访问**：VAR/TEMP 成员通过 GEP 从 `__instance` 参数访问，不是独立 alloca
3. **全局变量**：`VAR_GLOBAL` 通过 `GlobalVariable` + `ctx.RegisterGlobalVariable` 注册，`FrameBuilder` 在序言中绑定到本地 alloca 映射
4. **函数形参传递**：Input 传值（IR 值类型），Output/InOut 传指针（IR `ptr`），语句发射器中对 Output/InOut 参数调用 `EmitAddress`
5. **终结指令兜底**：`EnsureFunctionTerminator` 在每个函数体发射后检查最后一块是否有终结指令，无则补 `ret void` / `ret load(返回值)` / `ret zeroinitializer`
6. **CASE 枚举值**：枚举标签（`Color.Red` / `Red`）经 `ResolveEnumCaseValue` 解析为整型 case 值（优先显式值、回退成员索引）
7. **调试信息**：序言阶段不附着行号（`IsInPrologue=true` 时 `AttachDebug` 跳过）
8. **所有函数名/变量名无前缀**：遵循 `IronST.IRBuilder` 约定，裸名存储

---

## 重要提醒（防幻觉）

1. **MID 参数顺序**：代码注释写的是 `MID(input, length, start)`，`startPos` 读自 `args[2]`。**文档记录了代码实际顺序，但请注意此顺序可能与 IEC 61131-3 标准的 `MID(input, start, length)` 不一致，需确认实际意图。**
2. **DELETE/REPLACE 参数顺序**：实现为 `DELETE(input, length, start)`、`REPLACE(in1, in2, length, start)`，`start` 为 **1-based**（与 IEC 常见的 `(start, length)` 顺序不同，以本实现为准）。
3. **CONCAT 仅两参数**：代码中仅处理 `args[0]` 和 `args[1]`。**不要假设支持可变参数。**
4. **CASE 枚举标签**：已实现（`ResolveEnumCaseValue`）；标签必须能解析为枚举成员，否则抛 `CodeGenException`。
5. **TypeMapping 细节未提供**：`TypeMapper` 的具体实现（`Map`、`GetFieldIndex`、`GetFieldType`）不在提供的文档中，**不要假设其映射规则。**
6. **IECType 完整 API 未提供**：`IECType` 的 `LookupBuiltin`、`MakeStruct`、`MakeArray`、`MakeReference`、`MakeString` 等工厂方法被使用但定义未提供，**不要假设未见的成员。**
7. **FB ACTION 与主函数共享形参**：ACTION 函数与 FB 函数使用相同的 `formalParams`，通过 `ctx.RegisterFormalParams(actName, formalParams)` 注册。
8. **字符串 copy 为内联循环**：`EmitMemcpy` 是逐字节循环拷贝，不是调用 C `memcpy`。