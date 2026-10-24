# IronST.IRBuilder 接口摘要

纯 C# LLVM IR 工具链，零第三方依赖，目标 LLVM 22（opaque pointer）。
功能：构建 IR 对象模型、发射 .ll 文本、解析 .ll 文本。

---

## 命名空间

- 核心 IR：`IronST.IRBuilder.IR`
- 发射器：`IronST.IRBuilder.Emitter`
- 解析器：`IronST.IRBuilder.Parser`
- Opt 运行器：`IronST.IRBuilder.OptRunner`

---

## 类型系统（IrType）

| 构造方式 | LLVM 表示 | 说明 |
|----------|-----------|------|
| `IrType.Void` | `void` | void |
| `IrType.Int1` | `i1` | 1 位整数 |
| `IrType.Int8` | `i8` | 8 位整数 |
| `IrType.Int32` | `i32` | 32 位整数 |
| `IrType.Int64` | `i64` | 64 位整数 |
| `IrType.Float` | `float` | 32 位浮点 |
| `IrType.Double` | `double` | 64 位浮点 |
| `IrType.Pointer` | `ptr` | opaque pointer（LLVM 22 强制） |
| `new IntegerType(n)` | `iN` | 任意位宽整数 |
| `new FloatType(kind)` | `half/bfloat/float/...` | 浮点类型 |
| `new PointerType(addrSpace)` | `ptr addrspace(N)` | 带地址空间指针 |
| `new FunctionType(ret, params[], varArg)` | `i32(i32, i32)` | 函数类型 |
| `new ArrayType(elem, len)` | `[10 x i32]` | 数组 |
| `new VectorType(count, elem, scalable)` | `<4 x float>` | 向量（可伸缩） |
| `new StructType(fields[], packed)` | `{i32, float}` | 结构体 |
| `new NamedStructType(name)` | `%Name` | 命名结构体（需注册 Body） |

---

## 常量（Constant）

| 构造方式 | LLVM 表示 |
|----------|-----------|
| `new ConstantInt(type, value)` | `i32 42` |
| `new ConstantFP(type, value)` | `float 3.14` |
| `new ConstantPointerNull()` | `null` |
| `UndefValue.Get(type)` | `undef` |
| `PoisonValue.Get(type)` | `poison` |
| `new ConstantAggregateZero(type)` | `zeroinitializer` |
| `new ConstantArray(arrayType, elements[])` | `[i32 1, i32 2]` |
| `new ConstantStruct(structType, elements[])` | `{i32 1, float 2.0}` |
| `ConstantDataArray.FromString("hello")` | `c"hello\00"`（自动追加 null） |

---

## 指令（Instruction）

所有指令继承自 `Instruction` → `Value`。

### 终结指令
- `new ReturnInst()` → `ret void`
- `new ReturnInst(value)` → `ret i32 %v`
- `new BranchInst(destBB)` → `br label %dest`
- `new BranchInst(cond, trueBB, falseBB)` → `br i1 %c, label %t, label %f`
- `new SwitchInst(cond, defaultBB)` + `.AddCase(const, bb)` → `switch`
- `new UnreachableInst()` → `unreachable`

### 二元运算（BinaryOperator.CreateXxx）
| 方法 | LLVM | 属性 |
|------|------|------|
| `CreateAdd(l, r, name)` | `add` | `.NoSignedWrap` / `.NoUnsignedWrap` |
| `CreateSub(l, r, name)` | `sub` | 同上 |
| `CreateMul(l, r, name)` | `mul` | 同上 |
| `CreateFAdd(l, r, name)` | `fadd` | — |
| `CreateFSub(l, r, name)` | `fsub` | — |
| `CreateFMul(l, r, name)` | `fmul` | — |
| `CreateSDiv(l, r, name)` | `sdiv` | — |
| `CreateUDiv(l, r, name)` | `udiv` | — |
| `CreateSRem(l, r, name)` | `srem` | — |
| `CreateFDiv(l, r, name)` | `fdiv` | — |
| `CreateFRem(l, r, name)` | `frem` | — |
| `CreateShl(l, r, name)` | `shl` | — |
| `CreateAShr(l, r, name)` | `ashr` | — |
| `CreateLShr(l, r, name)` | `lshr` | — |
| `CreateAnd(l, r, name)` | `and` | — |
| `CreateOr(l, r, name)` | `or` | — |
| `CreateXor(l, r, name)` | `xor` | — |

### 比较
- `new ICmpInst(pred, l, r, name)` → `icmp`
  谓词：`Eq/Ne/Sgt/Sge/Slt/Sle/Ugt/Uge/Ult/Ule`
- `new FCmpInst(pred, l, r, name)` → `fcmp`
  谓词：`False/Oeq/Ogt/Oge/Olt/Ole/One/Ord/Ueq/Ugt/Uge/Ult/Ule/Une/Uno/True`

### 内存
- `new AllocaInst(type, arraySize?, name?, align)` → `alloca`
- `new LoadInst(type, ptr, name?, align)` → `load`
- `new StoreInst(value, ptr, align)` → `store`
- `new GetElementPtrInst(elemType, ptr, indices[], name?, inBounds)` → `getelementptr`

### 类型转换（CastInst.CreateXxx）
- `CreateTrunc` / `CreateZExt` / `CreateSExt` / `CreateFPTrunc` / `CreateFPExt`
- `CreateFPToUI` / `CreateFPToSI` / `CreateUIToFP` / `CreateSIToFP`
- `CreatePtrToInt` / `CreateIntToPtr` / `CreateBitCast` / `CreateAddrSpaceCast`

### 其他
- `new PHINode(type, name)` + `.AddIncoming(value, block)` → `phi`
- `new SelectInst(cond, trueVal, falseVal, name)` → `select`
- `new CallInst(funcType, callee, args[], name)` → `call`，`.IsTailCall`
- `new ExtractValueInst(agg, indices[], name)` → `extractvalue`
- `new InsertValueInst(agg, value, indices[], name)` → `insertvalue`

---

## IR 对象模型

### Module（顶层容器）
| 属性 | 类型 | 说明 |
|------|------|------|
| `SourceFilename` | `string?` | 源文件名 |
| `TargetTriple` | `string?` | 目标三元组 |
| `TargetDataLayout` | `string?` | 数据布局 |
| `Functions` | `List<Function>` | 函数列表 |
| `GlobalVariables` | `List<GlobalVariable>` | 全局变量 |
| `NamedTypes` | `List<NamedStructType>` | 命名类型 |

方法：
- `AddFunction(func)` / `AddGlobalVariable(gv)` / `AddNamedType(name, body)`
- `AddGlobalString(name, str)` — 自动生成字符串常量并追加 null

### Function
- `Name` : `string?`（裸名，无 @ 前缀）
- `FunctionType` : 函数签名
- `Linkage` : `LinkageType`（默认 External）
- `IsDeclaration` : `bool`（无 BasicBlocks 时 = true）
- `Parameters` : `IReadOnlyList<Argument>`
- `BasicBlocks` : `List<BasicBlock>`
- `AppendBasicBlock(bb)` — 添加基本块

### Argument / BasicBlock / GlobalVariable
- `Argument` : `Index`、`ParentFunction`、`Name`
- `BasicBlock` : `Name`、`Instructions`、`Terminator`、`Parent`，方法：`AddInstruction` / `InsertBefore` / `InsertAfter` / `RemoveInstruction`
- `GlobalVariable` : `Name`（裸名）、`ValueType`（值类型）、`IsConstant`、`Linkage`、`Initializer`、`Alignment`、`IsUnnamedAddr`

### NamedStructType
- `Name` : `string`（无 % 前缀）
- `Body` : `StructType?`（需 Module.AddNamedType 注册）

---

## IRBuilder（便捷指令构建器）

`new IRBuilder(BasicBlock block)`，`.InsertBlock` 可写（切换插入点）。

| 方法 | 说明 |
|------|------|
| `CreateRet(v)` / `CreateRetVoid()` | return |
| `CreateBr(dest)` / `CreateCondBr(cond, t, f)` | branch |
| `CreateAdd` / `CreateSub` / `CreateMul` / `CreateSDiv` | 整数运算 |
| `CreateFAdd` / `CreateFSub` / `CreateFMul` | 浮点运算 |
| `CreateICmp(pred, l, r, n)` / `CreateFCmp(...)` | 比较 |
| `CreateAlloca(type, arrSize?, n, align)` | alloca |
| `CreateLoad(type, ptr, n, align)` | load |
| `CreateStore(val, ptr, align)` | store |
| `CreateGEP(elemType, ptr, indices, n, inBounds)` | getelementptr |
| `CreateTrunc` / `CreateZExt` / `CreateSExt` / `CreateBitCast` | 类型转换 |
| `CreateCall(ft, callee, args, n)` | call |
| `CreateSelect(cond, t, f, n)` | select |
| `CreatePhi(type, n)` | phi |

所有 `n` 为可选名字 `string?`。

---

## LLEmitter（发射器）

`using IronST.IRBuilder.Emitter;`

```csharp
new LLEmitter().Emit(Module) → string
```

输出符合 LLVM 22 规范的 .ll 文本。强制 opaque pointer，自动为无名值编号。

---

## LLParser（解析器）

`using IronST.IRBuilder.Parser;`

```csharp
new LLParser().Parse(string source) → Module
```

支持：函数声明/定义、所有核心指令、基本块、phi、switch、call、gep、cast、常量表达式、命名类型、全局变量、模块元数据、前向引用。

不支持（**注意这些不要用**）：元数据节点、属性组、别名、@llvm. 内置函数、comdat、module asm。

---

## OptRunner（调用外部 opt）

`using IronST.IRBuilder.OptRunner;`

通过 stdin/stdout 管道调用外部 `opt`，全程不写磁盘。

```csharp
var runner = new OptRunner();
runner.OptPath = null; // null = 自动查找
string optimized = runner.RunAsText(ir, "-S -O2");
byte[] bitcode = runner.RunAsBitcode(ir, "-O2");
```

| 属性/方法 | 说明 |
|-----------|------|
| `OptPath` | `string?`，手动指定 opt 路径 |
| `RunAsText(ir, args)` | 返回 .ll 文本（args 应含 `-S`） |
| `RunAsBitcode(ir, args)` | 返回 .bc 二进制 |
| `TimeoutMs` | 默认 60000ms |
| `ResolveOptPath()` | 自动查找顺序：OptPath → 入口程序集 `tools/opt/{rid}` → 当前程序集 → 工作目录 → `~/.ironst/` → PATH |

RID 自动检测：`win-x64 / linux-x64 / linux-arm64 / osx-arm64 / osx-x64`

---

## 关键枚举（精简）

| 枚举 | 成员 |
|------|------|
| `LinkageType` | `External` / `Internal` / `Private` / `Weak` / `Linkonce` |
| `ICmpPredicate` | `Eq` / `Ne` / `Sgt` / `Sge` / `Slt` / `Sle` / `Ugt` / `Uge` / `Ult` / `Ule` |
| `FCmpPredicate` | `False` / `Oeq` / `Ogt` / `Oge` / `Olt` / `Ole` / `One` / `Ord` / `Ueq` / `Ugt` / `Uge` / `Ult` / `Ule` / `Une` / `Uno` / `True` |
| `CastOp` | `Trunc` / `ZExt` / `SExt` / `FPTrunc` / `FPExt` / `FPToUI` / `FPToSI` / `UIToFP` / `SIToFP` / `PtrToInt` / `IntToPtr` / `BitCast` / `AddrSpaceCast` |
| `BinaryOp` | `Add` / `FAdd` / `Sub` / `FSub` / `Mul` / `FMul` / `SDiv` / `UDiv` / `SRem` / `FDiv` / `FRem` / `Shl` / `LShr` / `AShr` / `And` / `Or` / `Xor` |

---

## 重要提醒（防幻觉）

1. **名称无前缀**：所有 `Name` 都是裸名，不包含 `%` 或 `@` 前缀。
2. **指针类型**：LLVM 22 强制 opaque pointer，一律用 `IrType.Pointer` 或 `new PointerType(addrSpace)`，不再有 `i32*`。
3. **LLParser 限制**：不支持元数据节点、属性组、alias、comdat、module asm。**不要生成包含这些的输入给 Parser**。
4. **OptRunner 依赖**：`opt` 二进制需要存在，可通过 `tools/download-opt` 脚本下载，或自行安装 LLVM 17+。
5. **常量字符串**：`AddGlobalString` 和 `ConstantDataArray.FromString` 自动追加 `\00` 终止符。
6. **指令插入**：用 `IRBuilder` 时通过 `.InsertBlock` 切换插入点；手动添加用 `AddInstruction` / `InsertBefore` / `InsertAfter`。
