using IronST.IRBuilder.IR;
using IronST.Runtime.Shared;
// 消除 IronST.LLVMCodeGen.AST.BinaryOperator 的歧义
using IRBinaryOp = IronST.IRBuilder.IR.BinaryOperator;
using OpCode = IronST.Runtime.Shared.OpCode;

namespace IronST.BytecodeGen;

/// <summary>
/// IRBuilder Function -> BytecodeAssembler + DebugFunction。
/// 纯 C# 实现，消费 IRBuilder.IR 对象模型。
/// </summary>
internal sealed class FunctionEmitter
{
    private readonly BytecodeAssembler _asm;
    private readonly Function _func;
    private readonly DebugFunction _df;
    private readonly Dictionary<string, short> _globalIndex;
    private readonly string? _sourceFileName;
    private readonly Dictionary<int, (string file, int line)> _sourceMap;

    // 外部注入的 IR 指令索引 -> 源码位置映射
    private readonly Dictionary<string, List<(string file, int line)?>>? _irSourceMap;
    private int _instrIndex;

    /// <summary>
    /// 正在发射的那条 IR 指令的源码位置。
    ///
    /// <see cref="EmitInstr"/> 里有二十多处显式传 null —— 那些 PUSH/MOV/LDC/GETL
    /// 是同一条 IR 指令的辅助指令（参数准备、常量物化），本身没有独立位置。
    /// 但"没有自己的位置"在查行号时会被算成"就近靠前"的那条语句：IF 的合并块
    /// 紧跟着被跳过的 ELSIF 体，于是高亮会跳到没执行的 else 分支上。
    /// 兜底成当前指令的位置，这些辅助指令就跟着自己的语句走。
    /// </summary>
    private (string file, int line)? _currentSrc;

    // 寄存器分配（R[8]+ 线性递增）
    private readonly Dictionary<Value, ushort> _valReg = new();
    private byte _nextReg = BytecodeBuilder.R8;

    /// <summary>
    /// <see cref="_valReg"/> 里每个条目**是在哪个基本块里被写进寄存器的**
    /// （<c>null</c> = 函数序言，还没进入任何基本块）。
    /// 它是<see cref="InvalidateBlockLocalRegisters"/> 判定条目是否仍有依据的唯一依据。
    /// </summary>
    private readonly Dictionary<Value, BasicBlock?> _valRegBlock = new();

    /// <summary>当前正在发射的基本块（<see cref="EmitBody"/> 逐块推进）。</summary>
    private BasicBlock? _currentBlock;

    /// <summary>
    /// 函数入口块（<c>BasicBlocks[0]</c>，由 <c>FrameBuilder.BuildPrologue</c> 最先追加）。
    /// 它支配函数内所有块，故在它里面物化的寄存器条目可在任意块复用。
    /// </summary>
    private BasicBlock? _entryBlock;

    // 局部变量槽位
    private int _nextSlot;
    private readonly Dictionary<Value, short> _paramSlots = new();
    private readonly Dictionary<Value, short> _phiSlots = new();
    private readonly Dictionary<Value, short> _allocaSlots = new();

    /// <summary>
    /// 跨调用活跃值的帧槽。值在定义处写回槽，调用后按需从槽恢复。
    /// 槽位在 <see cref="Analyze"/> 里按 liveness 一次性分配。
    /// </summary>
    private readonly Dictionary<Value, short> _spillSlots = new();

    /// <summary>每个 clobber 指令之后仍然活跃的指令结果（CALL / HCALL）。</summary>
    private Dictionary<Instruction, HashSet<Value>>? _liveAcrossClobbers;

    // C8：寄存器回收（反向扫描死亡指令结果释放寄存器号）
    private readonly SortedSet<byte> _freeRegs = new();
    private Dictionary<Instruction, int>? _lastUseGlobal;
    private int _currentGlobalIndex = -1;
    private readonly List<byte> _currentTempRegs = new();

    /// <summary>
    /// 当前正在发射定义的 IR 指令。用于让 <see cref="EnsureReg"/> 区分
    /// “这是定义，必须分配目标寄存器”和“这是使用，可以从 spill 槽恢复”。
    /// </summary>
    private Instruction? _currentDef;

    private int _lrSlot = -1;

    // 基本块标签
    private readonly Dictionary<BasicBlock, string> _bbLabels = new();
    private int _labelCounter;

    // 函数元信息
    private readonly string _name;
    private readonly int _paramCount;
    private string _epilogueLabel = null!;

    // 结构体支持
    private readonly HashSet<Value> _structAllocas = new();
    private readonly Dictionary<Value, (Value basePtr, byte fieldIndex)> _structFieldPtrs = new();

    // A8：承载**全局槽号**的值（聚合全局变量本身，或由它派生的 GEP）。
    // 这类值不能走 GETR/SETR —— 那是帧内栈槽寻址；必须走 GETGI/SETGI。
    // 值映射到它指向的 pointee 类型，供聚合深拷贝计算槽数。
    private readonly Dictionary<Value, IrType> _globalAddresses = new();

    // A9：聚合 load 的临时栈副本槽。`load %P, ptr %gep` 没有现成栈槽，
    // 复制到一段临时槽后，下游（store / 实参 / 比较）可继续按“聚合值 = 栈基槽”处理。
    private readonly Dictionary<Value, short> _aggregateTempSlots = new();

    // A8：结构体比较辅助函数的全局聚合实参副本（辅助函数在自己的帧里看不到全局寻址）。
    private readonly Dictionary<(CallInst call, int index), (short baseSlot, int count)> _aggregateArgTemps = new();

    // 结构体类型注册回调
    private readonly Func<StructType, byte>? _getStructTypeId;

    public DebugFunction DebugFunction => _df;

    private readonly IReadOnlyDictionary<string, ushort>? _hostFunctions;
    private readonly bool _allowExternalCalls;

    // 内置数学 HCALL ID；只引用 HostCallIds，避免两处常量漂移。
    private const ushort HC_MATH_IMOD = HostCallIds.MATH_IMOD;
    private const ushort HC_MATH_UMOD = HostCallIds.MATH_UMOD;

    public FunctionEmitter(
        Function func,
        BytecodeAssembler asm,
        Dictionary<string, short> globalIndex,
        string? sourceFileName,
        Dictionary<int, (string file, int line)> sourceMap,
        IReadOnlyDictionary<string, ushort>? hostFunctions,
        Dictionary<string, List<(string file, int line)?>>? irSourceMap = null,
        Func<StructType, byte>? getStructTypeId = null,
        bool allowExternalCalls = false
    )
    {
        _func = func;
        _asm = asm;
        _globalIndex = globalIndex;
        _sourceFileName = sourceFileName;
        _sourceMap = sourceMap;
        _hostFunctions = hostFunctions;
        _irSourceMap = irSourceMap;
        _getStructTypeId = getStructTypeId;
        _allowExternalCalls = allowExternalCalls;
        _name = func.Name ?? "__func";
        _paramCount = func.Parameters.Count;
        _df = new DebugFunction { Name = _name, ParamCount = _paramCount };
    }

    // ═══════════════════════════════════════
    //  入口
    // ═══════════════════════════════════════

    public void Emit()
    {
        Analyze();
        _epilogueLabel = $"__{_name}_epilogue";
        _currentSrc = null;   // 别把上一个函数的最后一行带进本函数的序言

        // 参数在 A0–A3 / 帧上参数区里占用的**槽数**：标量 1 槽，多槽聚合按 FlatSlotCount。
        // 纯标量时 aSlotForDef == 参数个数 ⇒ 与扩展前完全一致（零行为变化）。
        //
        // **声明式（宿主 / 外部）函数除外**：它们没有函数体，参数永远不会被聚合展开，
        // 且其 IR 参数类型常是 STRING 之类的结构体（STRING_254 ⇒ FlatSlotCount 255）。
        // 对它们沿用「每参数一个 A 位」的旧规则，否则连宿主声明都编译不过。
        int aSlotForDef;
        if (_func.IsDeclaration) {
            aSlotForDef = Math.Min(_paramCount, 4);
        } else {
            aSlotForDef = 0;
            for (int i = 0; i < _paramCount; i++)
                aSlotForDef += FlatSlotCount(_func.Parameters[i].Type);
        }

        // 前 4 槽走 A0–A3；第 5 槽起走**调用方帧的参数区**。
        // （A0–A3 只有 4 个，这是寄存器文件的硬边界，与聚合无关。）
        int aRegForDef = Math.Min(aSlotForDef, 4);
        int stackArgs = aSlotForDef - aRegForDef;

        int extraLocals = _nextSlot - aRegForDef;

        _asm.DefFunc(
            _name,
            aRegForDef,
            extraLocals,
            () => {
                _df.Instructions.Add(DebugInstr.Frame((short)_nextSlot));
                // 参数槽从 0 起按 FlatSlotCount 连续分配，因此第 s 个参数槽的槽号就是 s。
                // 逐个 A 位回填：标量参数 i => SETL(A0+i, i)（与扩展前逐字节一致）；
                // 多槽聚合参数则占用连续的多个 A 位。
                for (int s = 0; s < aRegForDef; s++)
                    _df.Instructions.Add(
                        DebugInstr.Slot(OpCode.SETL, (byte)(BytecodeBuilder.A0 + s), (short)s)
                    );

                EmitStackArgPrologue(aRegForDef, stackArgs);

                EmitBody();

                _asm.Label(_epilogueLabel);
                _df.Labels[_epilogueLabel] = _df.Instructions.Count;
                _df.Instructions.Add(DebugInstr.Op(OpCode.LEAVE));
                _df.Instructions.Add(DebugInstr.Op(OpCode.RET));
            }
        );

        _df.TotalLocals = _nextSlot;
        DeriveSourceRange();
    }

    // ═══════════════════════════════════════
    //  >4 参数拆包
    // ═══════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════
    //  栈参数区序言（0B 调用约定扩展 · >4 槽参数走帧上参数区）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 把调用方帧参数区里的 <paramref name="stackArgs"/> 个槽读进本函数的局部参数槽。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>布局推导</b>：调用方压完 <c>k</c> 个栈参数后 <c>SP = SP0 - k</c>；<c>CALL</c> 本身
    /// 不改 SP（只把返回地址写进 R3）。被调用方的 <c>FRAME</c> 先压 LR（<c>SP0-k-1</c>）、
    /// 再压 FP（<c>SP0-k-2</c>）并把 FP 设为该处。于是第 <c>j</c> 个栈参数位于
    /// <c>FP + 2 + j</c>，用 <c>GETL tmp, -(3+j)</c> 读取 —— 因为 <c>GETL</c> 的
    /// 寻址是 <c>addr = FP - 1 - slot</c>。
    /// </para>
    /// <para>
    /// 该区间在 FP <b>之上</b>：与 LR/FP 保存槽（FP、FP+1）相邻但不重叠，也完全不碰
    /// 局部槽区（FP 之下）。这正是既有 PUSH/POP 路径坏掉的根因 —— 旧路径让被调用方
    /// 在 FRAME <b>之后</b> POP，而 FRAME 已把 SP 降到 FP-n，POP 读到的全是帧内数据。
    /// </para>
    /// </remarks>
    private void EmitStackArgPrologue(int aRegForDef, int stackArgs)
    {
        for (int j = 0; j < stackArgs; j++) {
            byte tmp = (byte)AllocReg();
            EmitInstr(DebugInstr.Slot(OpCode.GETL, tmp, (short)(-3 - j)), null);
            EmitInstr(DebugInstr.Slot(OpCode.SETL, tmp, (short)(aRegForDef + j)), null);
        }
    }

    // ═══════════════════════════════════════
    //  分析
    // ═══════════════════════════════════════

    private void Analyze()
    {
        // 基本块标签
        int bbIdx = 0;
        foreach (var bb in _func.BasicBlocks)
            _bbLabels[bb] = $"__{_name}_bb{bbIdx++}";

        // 参数槽位：每个参数按其**扁平槽数**占槽。
        // 标量仍占 1 槽 ⇒ 对既有程序零行为变化（纯标量时第 i 个参数的槽号仍是 i）；
        // 多槽聚合参数从此获得连续 N 槽 —— 这是调用约定多槽通道的落点
        // （调用方逐槽 PUSH，被调用方逐槽 POP 收取）。
        for (int i = 0; i < _paramCount; i++) {
            _paramSlots[_func.Parameters[i]] = (short)_nextSlot;
            _nextSlot += FlatSlotCount(_func.Parameters[i].Type);
        }

        // PHI 槽位
        foreach (var bb in _func.BasicBlocks)
            foreach (var inst in bb.Instructions)
                if (inst is PHINode)
                    _phiSlots[inst] = (short)_nextSlot++;

        // alloca 槽位（数组/结构体按元素/字段数分配扁平槽）
        foreach (var bb in _func.BasicBlocks)
            foreach (var inst in bb.Instructions)
                if (inst is AllocaInst ai) {
                    // 扁平槽数必须**递归**到标量叶节点：嵌套结构体 / 数组 of 结构体的
                    // 外层 ElementTypes.Count 会少算（曾致 %B = {%A, i32} 只分 2 槽，
                    // 正确应为 3，2026-10-15 实证 FRAME 104 而正确为 106）。
                    int count = FlatSlotCount(ai.AllocatedType);
                    // 槽号在 GETL/SETL 里是**有符号 16 位立即数**，越界会静默回绕到
                    // 别的变量 —— 属"静默产出坏字节码"，必须在写侧拦。
                    if (count < 1 || _nextSlot + count > short.MaxValue)
                        throw new InvalidOperationException(
                            $"Local frame overflow: alloca '{(ai.Name is { } an ? "%" + an : "(anonymous)")}' "
                                + $"needs {count} slot(s) at base {_nextSlot}, which exceeds the "
                                + $"{short.MaxValue}-slot limit of a single frame "
                                + "(GETL/SETL encode the slot as a signed 16-bit immediate).");
                    _allocaSlots[inst] = (short)_nextSlot;
                    _nextSlot += count;
                }

        // A9：聚合 load 的临时栈副本。源地址不是 alloca（数组元素、全局聚合、
        // 引用解引用）时，先复制到临时槽 —— 后续聚合 store / 实参 / 比较都
        // 沿用既有的“聚合值 = 栈基槽”表示，无需再区分地址空间。
        foreach (var bb in _func.BasicBlocks)
            foreach (var inst in bb.Instructions)
                if (inst is LoadInst li
                    && IsAggregateType(li.LoadedType)
                    && FlatSlotCount(li.LoadedType) > 1
                    && !_allocaSlots.ContainsKey(li.Pointer))
                {
                    int count = FlatSlotCount(li.LoadedType);
                    EnsureTempRegion(count, $"aggregate load '{li.Name ?? "(anonymous)"}'");
                    _aggregateTempSlots[li] = (short)_nextSlot;
                    _nextSlot += count;
                }

        // A8：结构体比较辅助函数的全局聚合实参。辅助函数在**自己的帧**里用
        // GETR 解引用，无法识别“这个地址是全局槽号”，故调用方先复制到栈临时区，
        // 再按普通指针实参传入（跨帧重定基逻辑不变）。
        foreach (var bb in _func.BasicBlocks)
            foreach (var inst in bb.Instructions)
                if (inst is CallInst call && IsStructCompareCall(call))
                {
                    for (int i = 0; i < call.ArgumentCount; i++)
                    {
                        if (!TryGetGlobalPointee(call.GetArgument(i), out var pointee))
                            continue;

                        int count = FlatSlotCount(pointee);
                        EnsureTempRegion(count, $"struct comparison operand {i}");
                        _aggregateArgTemps[(call, i)] = ((short)_nextSlot, count);
                        _nextSlot += count;
                    }
                }

        // LR 保存槽位（仅在函数体内有 call 时分配）
        foreach (var bb in _func.BasicBlocks)
            foreach (var inst in bb.Instructions)
                if (inst is CallInst) {
                    _lrSlot = _nextSlot++;
                    goto lrDone;
                }
    lrDone:
        ;

        // 调用点 spill：先做函数级 liveness，找出每个 CALL / HCALL 之后仍然
        // 活跃的指令结果，再为它们分配稳定的帧槽。
        //
        // 注意这里只分配“不可重物化”的指令结果。参数 / 常量 / alloca 基址
        // 在调用后可以重新发射装载指令，不需要槽。
        _liveAcrossClobbers = FunctionLiveness.ComputeLiveAcrossClobbers(_func);

        // 先收集候选，再按函数内指令顺序分配槽号。
        // liveness 返回的是 HashSet，枚举顺序依赖引用哈希；直接按它分配会让
        // 同一份源码在不同进程中产生不同的帧槽编号 / 字节码。槽号本身不影响
        // 语义，但确定性对增量编译与逐字节对拍很重要。
        var spillCandidates = new HashSet<Instruction>();
        foreach (var live in _liveAcrossClobbers.Values)
        {
            foreach (var value in live)
            {
                if (value is Instruction inst && !IsRematerializable(inst))
                    spillCandidates.Add(inst);
            }
        }

        foreach (var block in _func.BasicBlocks)
        {
            foreach (var inst in block.Instructions)
            {
                if (!spillCandidates.Contains(inst))
                    continue;

                if (_nextSlot >= short.MaxValue)
                    throw new InvalidOperationException(
                        $"Local frame overflow while allocating a spill slot for "
                            + $"'{inst.Name ?? "(anonymous)"}': the frame already has "
                            + $"{_nextSlot} slots (GETL/SETL encode slot numbers as signed "
                            + "16-bit immediates).");

                _spillSlots[inst] = (short)_nextSlot++;
            }
        }

        // C8：计算每条指令结果的最后使用全局索引（反向扫描）
        ComputeLastUseGlobal();
    }

    /// <summary>C8：计算每条指令结果在后续指令中的最后使用全局索引。</summary>
    private void ComputeLastUseGlobal()
    {
        var allInsts = new List<Instruction>();
        foreach (var bb in _func.BasicBlocks)
        {
            foreach (var inst in bb.Instructions)
            {
                allInsts.Add(inst);
            }
        }

        _lastUseGlobal = new Dictionary<Instruction, int>();
        foreach (var bb in _func.BasicBlocks)
        {
            foreach (var inst in bb.Instructions)
            {
                if (inst.ProducesResult)
                    _lastUseGlobal[inst] = -1;
            }
        }

        for (int gi = allInsts.Count - 1; gi >= 0; gi--)
        {
            var inst = allInsts[gi];
            foreach (var operand in inst.Operands)
            {
                if (operand is Instruction defInst && defInst.ProducesResult)
                {
                    if (_lastUseGlobal.TryGetValue(defInst, out int currentLast))
                    {
                        if (gi > currentLast)
                            _lastUseGlobal[defInst] = gi;
                    }
                }
            }
        }
    }

    /// <summary>C8：释放已死亡指令结果占用的寄存器号（扫描当前块后续指令）</summary>
    private void ReleaseDeadRegisters()
    {
        if (_valReg.Count == 0 || _currentBlock == null)
            return;

        // 收集当前块后续指令中使用的所有指令结果
        var usedInBlock = new HashSet<Value>();
        bool foundCurrent = false;
        foreach (var inst in _currentBlock.Instructions)
        {
            // 当前指令之后的指令才算“后续”
            if (ReferenceEquals(inst, _currentDef))
            {
                foundCurrent = true;
                continue; // 当前指令本身不算后续
            }
            if (!foundCurrent)
                continue;

            foreach (var operand in inst.Operands)
            {
                if (operand is Instruction defInst && defInst.ProducesResult)
                    usedInBlock.Add(defInst);
            }
        }

        List<Value> dead = null;
        foreach (var kv in _valReg)
        {
            var val = kv.Key;
            // 释放所有在后续指令中没有被使用的值（包括常量、指令结果等）
            if (!usedInBlock.Contains(val))
            {
                (dead ??= new List<Value>()).Add(val);
            }
        }

        if (dead == null)
            return;

        foreach (var val in dead)
        {
            if (_valReg.TryGetValue(val, out ushort reg))
            {
                _freeRegs.Add((byte)reg);
            }
            _valReg.Remove(val);
            _valRegBlock.Remove(val);
        }

        // C8：回收当前指令分配的临时寄存器（安全条件：该寄存器号不被任何仍活跃的 _valReg 值引用）
        // 临时寄存器只用于当前指令的辅助计算，不被后续指令直接引用（不在 _valReg 中），
        // 但为防止与已释放的死值寄存器号重用后被误读，只回收不在活跃 _valReg 注册号中的临时号
        var activeRegs = new HashSet<byte>(_valReg.Values.Select(r => (byte)r));
        foreach (byte tempReg in _currentTempRegs)
        {
            if (!activeRegs.Contains(tempReg))
            {
                _freeRegs.Add(tempReg);
            }
        }
        _currentTempRegs.Clear();
    }

    // ═══════════════════════════════════════
    //  源码位置读取
    // ═══════════════════════════════════════

    private (string file, int line)? ReadSourceLoc()
    {
        if (_irSourceMap == null)
            return null;
        if (!_irSourceMap.TryGetValue(_name, out var funcMap) || funcMap == null)
            return null;
        if (_instrIndex < funcMap.Count)
            return funcMap[_instrIndex];
        return null;
    }

    // ═══════════════════════════════════════
    //  源码范围推导
    // ═══════════════════════════════════════

    private void DeriveSourceRange()
    {
        string? file = null;
        int startLine = int.MaxValue,
            endLine = 0;
        foreach (var instr in _df.Instructions) {
            if (instr.SrcFile != null && instr.SrcLine > 0) {
                file ??= instr.SrcFile;
                if (instr.SrcLine < startLine)
                    startLine = instr.SrcLine;
                if (instr.SrcLine > endLine)
                    endLine = instr.SrcLine;
            }
        }
        if (file != null) {
            _df.SourceFile = file;
            _df.SourceStartLine = startLine;
            _df.SourceEndLine = endLine;
        }
    }

    // ═══════════════════════════════════════
    //  函数体发射
    // ═══════════════════════════════════════

    private void EmitBody()
    {
        _instrIndex = 0;
        _currentGlobalIndex = 0; // C8：全局指令序号从 0 开始
        _entryBlock = _func.BasicBlocks.Count > 0 ? _func.BasicBlocks[0] : null;
        foreach (var bb in _func.BasicBlocks) {
            // 块边界：丢弃「不是在本块里写进去的」可重物化寄存器条目。
            // 必须在发射本块任何指令**之前**做，见 InvalidateBlockLocalRegisters。
            InvalidateBlockLocalRegisters(bb);
            _currentBlock = bb;

            string bbLabel = _bbLabels[bb];
            _asm.Label(bbLabel);
            _df.Labels[bbLabel] = _df.Instructions.Count;

            foreach (var inst in bb.Instructions) {
                EmitInstruction(inst, bb);
                _instrIndex++;
            }
        }
        _currentBlock = null;
    }

    // ═══════════════════════════════════════
    //  EmitInstr: asm + debug 双写
    // ═══════════════════════════════════════

    private void EmitInstr(DebugInstr di, (string file, int line)? src)
    {
        // null = 这条是当前 IR 指令的辅助指令，跟着它的位置走
        src ??= _currentSrc;
        if (src is { } s) {
            _sourceMap[_asm.PC] = (s.file, s.line);
            di.SrcFile = s.file;
            di.SrcLine = s.line;
        }
        _df.Instructions.Add(di);
        EmitToAssembler(di);
    }

    // ═══════════════════════════════════════
    //  DebugInstr -> BytecodeAssembler 桥接
    // ═══════════════════════════════════════

    private void EmitToAssembler(DebugInstr di)
    {
        switch (di.Opcode) {
            // 1 字节
            case OpCode.NOP:
                _asm.Raw.NOP();
                break;
            case OpCode.HALT:
                _asm.Raw.HALT();
                break;
            case OpCode.BRK:
                _asm.Raw.BRK();
                break;
            case OpCode.LEAVE:
                _asm.Raw.LEAVE();
                break;
            case OpCode.RET:
                _asm.Raw.RET();
                break;

            // 2 字节
            case OpCode.SYSCALL:
                _asm.Raw.SYSCALL(di.Dst);
                break;
            case OpCode.HCALL:
                _asm.Raw.HCALL(unchecked((ushort)di.Imm16));
                break;
            case OpCode.LNIL:
                _asm.Raw.LNIL(di.Dst);
                break;
            case OpCode.LTRUE:
                _asm.Raw.LTRUE(di.Dst);
                break;
            case OpCode.LFALSE:
                _asm.Raw.LFALSE(di.Dst);
                break;
            case OpCode.PUSH:
                _asm.Raw.PUSH(di.Dst);
                break;
            case OpCode.POP:
                _asm.Raw.POP(di.Dst);
                break;
            case OpCode.IINC:
                _asm.Raw.IINC(di.Dst);
                break;
            case OpCode.IDEC:
                _asm.Raw.IDEC(di.Dst);
                break;
            case OpCode.JMPR:
                _asm.Raw.JMPR(di.Dst);
                break;
            case OpCode.CALLR:
                _asm.Raw.CALLR(di.Dst);
                break;
            case OpCode.VMCALL:
                _asm.Raw.VMCALL(di.Dst);
                break;

            // 3 字节：双寄存器
            case OpCode.MOV:
                _asm.Raw.MOV(di.Dst, di.Src1);
                break;
            case OpCode.SWAP:
                _asm.Raw.SWAP(di.Dst, di.Src1);
                break;
            case OpCode.NOT:
                _asm.Raw.NOT(di.Dst, di.Src1);
                break;
            case OpCode.INEG:
                _asm.Raw.INEG(di.Dst, di.Src1);
                break;
            case OpCode.FNEG:
                _asm.Raw.FNEG(di.Dst, di.Src1);
                break;
            case OpCode.TOINT:
                _asm.Raw.TOINT(di.Dst, di.Src1);
                break;
            case OpCode.TOFLT:
                _asm.Raw.TOFLT(di.Dst, di.Src1);
                break;
            case OpCode.TOSTR:
                _asm.Raw.TOSTR(di.Dst, di.Src1);
                break;
            case OpCode.TOBOOL:
                _asm.Raw.TOBOOL(di.Dst, di.Src1);
                break;
            case OpCode.TYPE:
                _asm.Raw.TYPE(di.Dst, di.Src1);
                break;
            case OpCode.GETR:
                _asm.Raw.GETR(di.Dst, di.Src1);
                break;
            case OpCode.SETR:
                _asm.Raw.SETR(di.Dst, di.Src1);
                break;
            case OpCode.BOUNDS:
                _asm.Raw.BOUNDS(di.Dst, di.Src1);
                break;
            case OpCode.NULLCK:
                _asm.Raw.NULLCK(di.Dst);
                break;
            case OpCode.GETGI:
                _asm.Raw.GETGI(di.Dst, di.Src1);
                break;
            case OpCode.SETGI:
                _asm.Raw.SETGI(di.Dst, di.Src1);
                break;
            case OpCode.CLZ:
                _asm.Raw.CLZ(di.Dst, di.Src1);
                break;

            // 3 字节：FRAME / PUSHN / POPN
            case OpCode.FRAME:
                _asm.Raw.FRAME(di.Imm16);
                break;
            case OpCode.PUSHN:
                _asm.Raw.PUSHN(di.Imm16);
                break;
            case OpCode.POPN:
                _asm.Raw.POPN(di.Imm16);
                break;

            // 常量池（统一 LDC）
            // 0B 步骤 3 · 切片 4：改走 EmitLdc 统一收口，发射同时登记 ConstIdx 记录。
            // 索引求值顺序与迁移前一致 —— 先 AddString/AddConst 追加池，再发射。
            case OpCode.LDC: {
                int idx = di.Str != null
                    ? _asm.AddString(di.Str)
                    : di.Imm32 == 1
                        ? _asm.AddConst(di.IntVal)
                        : _asm.AddConst(di.Float);
                _asm.EmitLdc(di.Dst, idx, SourceOf(di));
                break;
            }

            // 4 字节：三寄存器算术
            case OpCode.IADD:
                _asm.Raw.IADD(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.ISUB:
                _asm.Raw.ISUB(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.IMUL:
                _asm.Raw.IMUL(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.IDIV:
                _asm.Raw.IDIV(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.FADD:
                _asm.Raw.FADD(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.FSUB:
                _asm.Raw.FSUB(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.FMUL:
                _asm.Raw.FMUL(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.FDIV:
                _asm.Raw.FDIV(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.AND:
                _asm.Raw.AND(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.OR:
                _asm.Raw.OR(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.XOR:
                _asm.Raw.XOR(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.SHL:
                _asm.Raw.SHL(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.SHR:
                _asm.Raw.SHR(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.SAR:
                _asm.Raw.SAR(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.ROL:
                _asm.Raw.ROL(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.ROR:
                _asm.Raw.ROR(di.Dst, di.Src1, di.Src2);
                break;

            // 4 字节：立即数位运算
            case OpCode.ANDI:
                _asm.Raw.ANDI(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.ORI:
                _asm.Raw.ORI(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.XORI:
                _asm.Raw.XORI(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.SHLI:
                _asm.Raw.SHLI(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.SHRI:
                _asm.Raw.SHRI(di.Dst, di.Src1, di.Src2);
                break;

            // 4 字节：比较
            case OpCode.CMP:
                _asm.Raw.CMP(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.IEQ:
                _asm.Raw.IEQ(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.INE:
                _asm.Raw.INE(di.Dst, di.Src1, di.Src2);
                break;
            case OpCode.FEQ:
                _asm.Raw.FEQ(di.Dst, di.Src1, di.Src2);
                break;

            // 4 字节：slot
            case OpCode.GETL:
                _asm.Raw.GETL(di.Dst, di.Imm16);
                break;
            case OpCode.SETL:
                _asm.Raw.SETL(di.Dst, di.Imm16);
                break;
            case OpCode.GETG:
                _asm.Raw.GETG(di.Dst, di.Imm16);
                break;
            case OpCode.SETG:
                _asm.Raw.SETG(di.Dst, di.Imm16);
                break;

            // 4 字节：结构体
            case OpCode.SNEW:
                _asm.Raw.SNEW(di.Dst, di.Src1, unchecked((ushort)di.Imm16));
                break;
            case OpCode.SGET:
                _asm.Raw.SGET(di.Dst, di.Src1, (byte)di.Off8);
                break;
            case OpCode.SSET:
                _asm.Raw.SSET(di.Src1, di.Src2, (byte)di.Off8);
                break;

            // 4 字节：类型检查
            case OpCode.ISTYPE:
                _asm.Raw.ISTYPE(di.Dst, di.Src1, (byte)di.Off8);
                break;

            // 4 字节：条件分支（走标签补丁）
            case OpCode.BEQ:
                _asm.Beq(di.Src1, di.Src2, di.Label!);
                break;
            case OpCode.BNE:
                _asm.Bne(di.Src1, di.Src2, di.Label!);
                break;
            case OpCode.BLT:
                _asm.Blt(di.Src1, di.Src2, di.Label!);
                break;
            case OpCode.BGE:
                _asm.Bge(di.Src1, di.Src2, di.Label!);
                break;
            case OpCode.BGT:
                _asm.Bgt(di.Src1, di.Src2, di.Label!);
                break;
            case OpCode.BLE:
                _asm.Ble(di.Src1, di.Src2, di.Label!);
                break;

            // 符号跳转（自动 patch）
            case OpCode.BZ:
                _asm.Bz(di.Dst, di.Label!);
                break;
            case OpCode.BNZ:
                _asm.Bnz(di.Dst, di.Label!);
                break;

            // 5 字节：绝对/符号跳转
            case OpCode.JMP:
                if (di.Label != null)
                    _asm.Jmp(di.Label);
                else
                    _asm.Raw.JMP((uint)di.Imm32);
                break;

            // 函数调用
            case OpCode.CALL:
                if (di.CallTarget != null)
                    _asm.CallFunc(di.CallTarget);
                else
                    _asm.Raw.CALL((uint)di.Imm32);
                break;

            default:
                System.Diagnostics.Debug.WriteLine($"[EmitToAssembler] Unhandled: {di.Opcode}");
                break;
        }
    }

    // ═══════════════════════════════════════
    //  IR 指令分派（类型匹配）
    //  注意：IRBuilder 没有 FNegInst / UnaryOperator，
    //  fneg 在上游已处理（如转为 fsub 0.0, x），
    //  到达此处的 IR 不会包含 fneg。
    // ═══════════════════════════════════════

    private void EmitInstruction(Instruction inst, BasicBlock bb)
    {
        var src = ReadSourceLoc();
        _currentSrc = src;

        var previousDef = _currentDef;
        _currentDef = inst;
        try
        {
            switch (inst) {
            case IRBinaryOp bin:
                EmitBinaryOperator(bin, src);
                break;
            case ICmpInst icmp:
                EmitICmp(icmp, src);
                break;
            case FCmpInst fcmp:
                EmitFCmp(fcmp, src);
                break;
            case AllocaInst allocaInst:
                EmitAlloca(allocaInst, src);
                break;
            case LoadInst:
                EmitLoad(inst, src);
                break;
            case StoreInst:
                EmitStore(inst, src);
                break;
            case GetElementPtrInst:
                EmitGEP(inst, src);
                break;
            case BranchInst brInst:
                EmitBr(brInst, bb, src);
                break;
            case ReturnInst retInst:
                EmitRet(retInst, src);
                break;
            case SwitchInst swInst:
                EmitSwitch(swInst, bb, src);
                break;
            case CallInst callInst:
                EmitCall(callInst, src);
                break;
            case PHINode phi:
                EmitPhi(phi);
                break;
            case SelectInst:
                EmitSelect(inst, src);
                break;
            case ExtractValueInst:
                EmitExtractValue(inst, src);
                break;
            case InsertValueInst:
                EmitInsertValue(inst, src);
                break;
            case CastInst castInst:
                EmitCastInst(castInst, src);
                break;
            case UnreachableInst:
                break;
            default:
                System.Diagnostics.Debug.WriteLine($"[Emit] Unhandled: {inst.GetType().Name}");
                break;
            }

            // 跨调用活跃值在**定义处**写回 spill 槽。槽在定义支配的所有路径上
            // 都已写入，因此调用后从槽恢复不依赖“这条路径是否执行过 CALL”。
            if (inst.ProducesResult && _spillSlots.TryGetValue(inst, out short spillSlot))
            {
                if (!_valReg.TryGetValue(inst, out ushort spillReg))
                    throw new InvalidOperationException(
                        $"Spill value '{inst.Name ?? inst.GetType().Name}' was not materialized "
                            + "into a register at its definition; cannot write its spill slot.");

                EmitInstr(DebugInstr.Slot(OpCode.SETL, (byte)spillReg, spillSlot), null);
            }
        }
        finally
        {
            // C8：在指令发射后，先释放已死亡指令结果的寄存器（使用当前索引），再递增
            ReleaseDeadRegisters();
            _currentGlobalIndex++;
            _currentDef = previousDef;
        }
    }

    // ═══════════════════════════════════════
    //  算术 / 位运算
    // ═══════════════════════════════════════

    private void EmitBinaryOperator(IRBinaryOp bin, (string file, int line)? src)
    {
        switch (bin.Op) {
            case BinaryOp.Add:
                EmitBinDirect(bin, OpCode.IADD, src);
                break;
            case BinaryOp.Sub:
                EmitBinDirect(bin, OpCode.ISUB, src);
                break;
            case BinaryOp.Mul:
                EmitBinDirect(bin, OpCode.IMUL, src);
                break;
            case BinaryOp.SDiv:
                EmitBinDirect(bin, OpCode.IDIV, src);
                break;
            case BinaryOp.UDiv:
                EmitBinDirect(bin, OpCode.IDIV, src);
                break;
            case BinaryOp.SRem:
                EmitMathHcall(bin, HC_MATH_IMOD, src);
                break;
            case BinaryOp.URem:
                EmitMathHcall(bin, HC_MATH_UMOD, src);
                break;
            case BinaryOp.FAdd:
                EmitBinDirect(bin, OpCode.FADD, src);
                break;
            case BinaryOp.FSub:
                EmitBinDirect(bin, OpCode.FSUB, src);
                break;
            case BinaryOp.FMul:
                EmitBinDirect(bin, OpCode.FMUL, src);
                break;
            case BinaryOp.FDiv:
                EmitBinDirect(bin, OpCode.FDIV, src);
                break;
            case BinaryOp.And:
                EmitBinDirect(bin, OpCode.AND, src);
                break;
            case BinaryOp.Or:
                EmitBinDirect(bin, OpCode.OR, src);
                break;
            case BinaryOp.Xor:
                EmitBinDirect(bin, OpCode.XOR, src);
                break;
            case BinaryOp.Shl:
                EmitBinDirect(bin, OpCode.SHL, src);
                break;
            case BinaryOp.LShr:
                EmitBinDirect(bin, OpCode.SHR, src);
                break;
            case BinaryOp.AShr:
                EmitBinDirect(bin, OpCode.SAR, src);
                break;
            default:
                System.Diagnostics.Debug.WriteLine($"[Emit] Unhandled BinaryOp: {bin.Op}");
                break;
        }
    }

    private void EmitBinDirect(Instruction inst, OpCode op, (string file, int line)? src)
    {
        ushort dst = EnsureReg(inst);
        ushort s1 = EnsureReg(inst.Operands[0]);
        ushort s2 = EnsureReg(inst.Operands[1]);
        EmitInstr(DebugInstr.R3(op, (byte)dst, (byte)s1, (byte)s2), src);
    }

    /// <summary>
    /// 将二元运算通过 HCALL 发射（用于 IMOD/FMOD/FPOW 等已移除直接指令的运算）。
    /// 模式：MOV A0,rs1 → MOV A1,rs2 → HCALL id → MOV dst,A0
    /// </summary>
    private void EmitMathHcall(Instruction inst, ushort hcallId, (string file, int line)? src)
    {
        // HCALL 和普通 CALL 一样是 clobber 点：先读实参，调用后统一失效缓存，
        // 再从 A0 取回结果。结果寄存器必须在失效之后分配，避免它被当成
        // “跨调用活跃值”而从尚未写入的 spill 槽恢复。
        ushort s1 = EnsureReg(inst.Operands[0]);
        ushort s2 = EnsureReg(inst.Operands[1]);

        if (s1 != BytecodeBuilder.A0)
            EmitInstr(DebugInstr.R2(OpCode.MOV, BytecodeBuilder.A0, (byte)s1), null);
        if (s2 != BytecodeBuilder.A1)
            EmitInstr(DebugInstr.R2(OpCode.MOV, BytecodeBuilder.A1, (byte)s2), null);
        EmitInstr(DebugInstr.Hcall(hcallId), src);

        InvalidateRegistersAcrossCall();

        ushort dst = EnsureReg(inst);
        if (dst != BytecodeBuilder.A0)
            EmitInstr(DebugInstr.R2(OpCode.MOV, (byte)dst, BytecodeBuilder.A0), null);
    }

    // ═══════════════════════════════════════
    //  比较
    // ═══════════════════════════════════════

    private void EmitICmp(ICmpInst icmp, (string file, int line)? src)
    {
        ushort dst = EnsureReg(icmp);
        ushort s1 = EnsureReg(icmp.LHS);
        ushort s2 = EnsureReg(icmp.RHS);

        switch (icmp.Predicate) {
            case ICmpPredicate.Eq:
                EmitInstr(DebugInstr.R3(OpCode.IEQ, (byte)dst, (byte)s1, (byte)s2), src);
                break;
            case ICmpPredicate.Ne:
                EmitInstr(DebugInstr.R3(OpCode.INE, (byte)dst, (byte)s1, (byte)s2), src);
                break;
            case ICmpPredicate.Slt:
            case ICmpPredicate.Ult:
                // a < b: CMP → -1, 匹配 -1
                EmitCmpWithResult((byte)dst, (byte)s1, (byte)s2, matchNeg1: true, equal: true, src);
                break;
            case ICmpPredicate.Sle:
            case ICmpPredicate.Ule:
                // a <= b: CMP → -1 或 0（不是 1），匹配 ≠ 1
                EmitCmpWithResult((byte)dst, (byte)s1, (byte)s2, matchNeg1: false, equal: false, src);
                break;
            case ICmpPredicate.Sgt:
            case ICmpPredicate.Ugt:
                // a > b: CMP → 1，匹配 1
                EmitCmpWithResult((byte)dst, (byte)s1, (byte)s2, matchNeg1: false, equal: true, src);
                break;
            case ICmpPredicate.Sge:
            case ICmpPredicate.Uge:
                // a >= b: CMP → 0 或 1（不是 -1），匹配 ≠ -1
                EmitCmpWithResult((byte)dst, (byte)s1, (byte)s2, matchNeg1: true, equal: false, src);
                break;
        }
    }

    private void EmitFCmp(FCmpInst fcmp, (string file, int line)? src)
    {
        ushort dst = EnsureReg(fcmp);
        ushort s1 = EnsureReg(fcmp.LHS);
        ushort s2 = EnsureReg(fcmp.RHS);

        switch (fcmp.Predicate) {
            case FCmpPredicate.Oeq:
                EmitInstr(DebugInstr.R3(OpCode.FEQ, (byte)dst, (byte)s1, (byte)s2), src);
                break;
            case FCmpPredicate.One:
                // a != b: CMP → ≠ 0
                EmitCmpWithResult((byte)dst, (byte)s1, (byte)s2, matchNeg1: false, equal: false,
                    src, checkZero: true);
                break;
            case FCmpPredicate.Olt:
                EmitCmpWithResult((byte)dst, (byte)s1, (byte)s2, matchNeg1: true, equal: true, src);
                break;
            case FCmpPredicate.Ole:
                EmitCmpWithResult((byte)dst, (byte)s1, (byte)s2, matchNeg1: false, equal: false, src);
                break;
            case FCmpPredicate.Ogt:
                EmitCmpWithResult((byte)dst, (byte)s1, (byte)s2, matchNeg1: false, equal: true, src);
                break;
            case FCmpPredicate.Oge:
                EmitCmpWithResult((byte)dst, (byte)s1, (byte)s2, matchNeg1: true, equal: false, src);
                break;
            default:
                EmitInstr(DebugInstr.R3(OpCode.FEQ, (byte)dst, (byte)s1, (byte)s2), src);
                System.Diagnostics.Debug.WriteLine(
                    $"[EmitFCmp] Unhandled FCmpPredicate: {fcmp.Predicate}");
                break;
        }
    }

    /// <summary>
    /// 发射 CMP + 常量 + IEQ/INE，将排序比较转换为 0/1 布尔值。
    ///
    /// matchNeg1=true  → 匹配值为 -1
    /// matchNeg1=false → 匹配值为 1（或 0 当 checkZero=true）
    /// equal=true      → 使用 IEQ（结果 == 匹配值时为 1）
    /// equal=false     → 使用 INE（结果 != 匹配值时为 1）
    ///
    /// 语义对照：
    ///   a < b  → CMP(-1), IEQ(-1)  → 1
    ///   a <= b → CMP(1),  INE(1)   → 1（CMP ≠ 1 即 -1 或 0）
    ///   a > b  → CMP(1),  IEQ(1)   → 1
    ///   a >= b → CMP(-1), INE(-1)  → 1（CMP ≠ -1 即 0 或 1）
    ///   a != b → CMP(0),  INE(0)   → 1（checkZero=true）
    /// </summary>
    private void EmitCmpWithResult(
        byte dst, byte s1, byte s2,
        bool matchNeg1, bool equal,
        (string file, int line)? src,
        bool checkZero = false)
    {
        // CMP r_cmp, s1, s2 → Int64(-1/0/1)
        byte r_cmp = (byte)AllocReg();
        EmitInstr(DebugInstr.R3(OpCode.CMP, r_cmp, s1, s2), null);

        // 加载匹配常量
        byte r_const = (byte)AllocReg();
        if (checkZero) {
            // 匹配 0：用 LFALSE
            EmitInstr(DebugInstr.R1(OpCode.LFALSE, r_const), null);
        } else if (matchNeg1) {
            // 匹配 -1：用 LDC
            EmitInstr(DebugInstr.LdcInt(r_cmp + 2 > 255 ? r_const : r_const, -1L), null);
        } else {
            // 匹配 1：用 LTRUE
            EmitInstr(DebugInstr.R1(OpCode.LTRUE, r_const), null);
        }

        // IEQ 或 INE
        OpCode cmpOp = equal ? OpCode.IEQ : OpCode.INE;
        EmitInstr(DebugInstr.R3(cmpOp, dst, r_cmp, r_const), src);
    }

    // ═══════════════════════════════════════
    //  类型转换
    // ═══════════════════════════════════════

    private void EmitCastInst(CastInst cast, (string file, int line)? src)
    {
        switch (cast.Op) {
            case CastOp.SIToFP:
            case CastOp.UIToFP:
                EmitCastConvert(cast, OpCode.TOFLT, src);
                break;
            case CastOp.FPToSI:
            case CastOp.FPToUI:
                EmitCastConvert(cast, OpCode.TOINT, src);
                break;
            default:
                EmitCastNop(cast, src);
                break;
        }
    }

    private void EmitCastNop(Instruction inst, (string file, int line)? src)
    {
        ushort dst = EnsureReg(inst);
        ushort s = EnsureReg(inst.Operands[0]);
        if (dst != s)
            EmitInstr(DebugInstr.R2(OpCode.MOV, (byte)dst, (byte)s), src);
    }

    private void EmitCastConvert(Instruction inst, OpCode op, (string file, int line)? src)
    {
        ushort dst = EnsureReg(inst);
        ushort s = EnsureReg(inst.Operands[0]);
        EmitInstr(DebugInstr.R2(op, (byte)dst, (byte)s), src);
    }

    // ═══════════════════════════════════════
    //  内存（结构体感知）
    // ═══════════════════════════════════════

    private void EmitAlloca(AllocaInst allocaInst, (string file, int line)? src)
    {
        short slot = _allocaSlots[allocaInst];

        // EnsureReg 遇到 alloca 会分配寄存器并装入起始槽号（见其 alloca 分支）。
        // 这里**不再**补一条 EmitLoadInt —— 此前对同一寄存器、同一值连发两条 LDC，
        // 第二条把第一条原样覆盖，是死指令（2026-10-15 栈帧取证确认）。
        // 若寄存器尚未分配（正常路径：Emit 按 IR 顺序先到 alloca），
        // 由本方法首次分配并带上源位置，保住源码映射。
        // 结构体/数组采用扁平栈布局：字段/元素访问 = base + idx（GETR/SETR），无需 SNEW
        if (!_valReg.TryGetValue(allocaInst, out ushort dst))
        {
            dst = (ushort)AllocReg();
            BindReg(allocaInst, dst);
            EmitLoadInt((byte)dst, slot, src);
        }
    }

    /// <summary>
    /// IR 形态下的「引用解引用」：从 REFERENCE 类型的存储里 <c>load</c> 出指针值。
    /// </summary>
    /// <remarks>
    /// 在本后端里引用不是真实地址，而是**帧槽号**（见 <c>EmitAlloca</c> 的槽分配）。
    /// 因此「指针值」在 IR 里只可能来自两类地方：
    /// ① <c>REF=</c> 绑定时写下的槽号（常量/寄存器，不是 load）；
    /// ② 解引用时从引用变量（或其字段/数组元素）读出的 <c>load ptr</c>。
    /// 只有 ② 是「拿一个引用去访问目标」的时刻，也正是判空必须发生的时刻：
    /// 再往后做槽号算术，Void 已经被当成 0，槽 0 又是合法槽，就再也分不出空引用了。
    /// </remarks>
    private static bool IsReferenceDereference(Value value) =>
        value is LoadInst { LoadedType: PointerType };

    /// <summary>
    /// 在**解引用点**插入 <see cref="OpCode.NULLCK"/>（不做槽号算术之前）。
    /// </summary>
    /// <remarks>
    /// 逐点插入而不是在 <c>load ptr</c> 处统一插：<c>q := p</c> 这类“复制引用”是合法的，
    /// 空引用复制不该报错；只有“用引用去读取/写入目标”才应报错。
    /// </remarks>
    private void EmitNullReferenceCheck(Value pointer, (string file, int line)? src)
    {
        if (!IsReferenceDereference(pointer))
            return;

        ushort reg = EnsureReg(pointer);
        EmitInstr(DebugInstr.R1(OpCode.NULLCK, (byte)reg), src);
    }

    private void EmitLoad(Instruction inst, (string file, int line)? src)
    {
        ushort dst = EnsureReg(inst);
        Value ptr = inst.Operands[0];

        // 0) A8/A9：多槽聚合 load。寄存器持有该聚合的<b>起始槽号</b>（与 EmitAlloca 同一表示），
        //    不把整个对象塞进一个寄存器；后续的聚合 store / 调用实参据此逐槽搬运。
        //    源是 alloca 时直接取其槽号（既有行为）；否则（数组元素 / 全局聚合 /
        //    引用解引用）先复制到帧内临时区，让下游继续看到“栈基槽”。
        //    必须判在"标量 alloca"**之前** —— IsScalarAlloca 未排除 StructType，
        //    会把结构体误判为标量并只读一个槽（2026-10-15 根因）。
        //    FlatSlotCount == 1 的聚合（单字段结构体 / 单元素数组）一个槽就装得下，
        //    继续走下面的标量路径 —— 保持零行为变化，只处理真正装不下的情形。
        if (inst is LoadInst aggLoad
            && IsAggregateType(aggLoad.LoadedType)
            && FlatSlotCount(aggLoad.LoadedType) > 1)
        {
            if (_allocaSlots.TryGetValue(aggLoad.Pointer, out short aggSlot))
            {
                EmitLoadInt((byte)dst, aggSlot, src);
                return;
            }

            if (!_aggregateTempSlots.TryGetValue(aggLoad, out short tempBase))
                throw new InvalidOperationException(
                    $"Aggregate load of {aggLoad.LoadedType.ToIRString()} has no stack slot; "
                        + "the source must be an alloca or a pointer the emitter can copy "
                        + "into a frame temporary.");

            var copySrc = ResolveAggregateRef(aggLoad.Pointer, src);
            EmitAggregateCopyRefs(
                copySrc,
                new AggregateRef(AggregateSpace.Stack, true, tempBase, 0),
                FlatSlotCount(aggLoad.LoadedType),
                "aggregate load"
            );
            EmitLoadInt((byte)dst, tempBase, src);
            return;
        }

        // 1) 全局变量
        if (TryGetGlobalSlot(ptr, out short gIdx)) {
            RecordGlobalRef(ptr, gIdx, src);
            EmitInstr(DebugInstr.Slot(OpCode.GETG, (byte)dst, gIdx), src);
            return;
        }

        // 2) 结构体 alloca 直接加载
        if (_structAllocas.Contains(ptr)) {
            short slot = _allocaSlots[ptr];
            EmitInstr(DebugInstr.Slot(OpCode.GETL, (byte)dst, slot), src);
            return;
        }

        // 3) 标量 alloca
        if (_allocaSlots.TryGetValue(ptr, out short scalarSlot) && IsScalarAlloca(ptr)) {
            EmitInstr(DebugInstr.Slot(OpCode.GETL, (byte)dst, scalarSlot), src);
            return;
        }

        // 4) 结构体字段 GEP -> SGET
        if (_structFieldPtrs.TryGetValue(ptr, out var fi)) {
            short structSlot = _allocaSlots[fi.basePtr];
            ushort structReg = (ushort)AllocReg();
            EmitInstr(DebugInstr.Slot(OpCode.GETL, (byte)structReg, structSlot), null);
            EmitInstr(DebugInstr.Sget((byte)dst, (byte)structReg, fi.fieldIndex), src);
            return;
        }

        // 5) A8：全局聚合的字段 / 元素地址是**全局槽号**，必须走 GETGI；
        //    用 GETR 会按本帧 FP 去寻址栈，静默读到无关数据。
        if (_globalAddresses.ContainsKey(ptr)) {
            ushort globalAddr = EnsureReg(ptr);
            EmitInstr(DebugInstr.R2(OpCode.GETGI, (byte)dst, (byte)globalAddr), src);
            return;
        }

        // 6) 通用指针读取
        EmitNullReferenceCheck(ptr, src);
        ushort addr = EnsureReg(ptr);
        EmitInstr(DebugInstr.R2(OpCode.GETR, (byte)dst, (byte)addr), src);
    }

    private void EmitStore(Instruction inst, (string file, int line)? src)
    {
        Value val = inst.Operands[0];
        Value ptr = inst.Operands[1];

        // store 到结构体 alloca -> 忽略
        if (_structAllocas.Contains(ptr))
            return;

        // A8/A9：多槽聚合 store —— 源/目标各自解析为「栈基槽 / 全局槽号」后逐槽深拷贝。
        //     必须判在 EnsureReg(val) 之前 —— 聚合值不装入单个寄存器，无需为其占寄存器。
        //     目标可以是 alloca（立即数槽号）、数组元素 GEP（运行时地址）或
        //     全局聚合（GETGI/SETGI）。
        if (IsAggregateType(val.Type) && FlatSlotCount(val.Type) > 1)
        {
            int count = FlatSlotCount(val.Type);
            var dstRef = ResolveAggregateRef(ptr, src);

            // `store zeroinitializer`（如 FB 成员字段的隐式清零）：没有源存储区，直接写零槽。
            if (val is ConstantAggregateZero)
            {
                EmitAggregateZeroFill(dstRef, count);
                return;
            }

            var srcRef = ResolveAggregateValue(val);
            EmitAggregateCopyRefs(srcRef, dstRef, count, "aggregate store");
            return;
        }

        ushort valReg = EnsureReg(val);

        // 结构体字段 GEP -> SSET
        if (_structFieldPtrs.TryGetValue(ptr, out var fi)) {
            short structSlot = _allocaSlots[fi.basePtr];
            ushort structReg = (ushort)AllocReg();
            EmitInstr(DebugInstr.Slot(OpCode.GETL, (byte)structReg, structSlot), null);
            EmitInstr(DebugInstr.Sset((byte)valReg, (byte)structReg, fi.fieldIndex), src);
            return;
        }

        // 全局变量
        if (TryGetGlobalSlot(ptr, out short gIdx)) {
            RecordGlobalRef(ptr, gIdx, src);
            EmitInstr(DebugInstr.Slot(OpCode.SETG, (byte)valReg, gIdx), src);
            return;
        }

        // 标量 alloca
        if (_allocaSlots.TryGetValue(ptr, out short slot) && IsScalarAlloca(ptr)) {
            EmitInstr(DebugInstr.Slot(OpCode.SETL, (byte)valReg, slot), src);
            return;
        }

        // A8：全局聚合的字段 / 元素 -> SETGI（与 GETGI 对称）。
        if (_globalAddresses.ContainsKey(ptr)) {
            ushort globalAddr = EnsureReg(ptr);
            EmitInstr(DebugInstr.R2(OpCode.SETGI, (byte)valReg, (byte)globalAddr), src);
            return;
        }

        // 通用指针写入
        EmitNullReferenceCheck(ptr, src);
        ushort addr = EnsureReg(ptr);
        EmitInstr(DebugInstr.R2(OpCode.SETR, (byte)valReg, (byte)addr), src);
    }

    private void EmitGEP(Instruction inst, (string file, int line)? src)
    {
        ushort dst = EnsureReg(inst);
        Value basePtr = inst.Operands[0];

        // 结构体字段 GEP: (base, i32 0, i32 field_idx)
        if (
            inst.Operands.Count == 3
            && basePtr is AllocaInst
            && _structAllocas.Contains(basePtr)
            && inst.Operands[1] is ConstantInt ci1
            && ci1.Value == 0
            && inst.Operands[2] is ConstantInt ci2
        ) {
            byte fieldIdx = (byte)ci2.Value;
            _structFieldPtrs[inst] = (basePtr, fieldIdx);
            ushort baseReg = EnsureReg(basePtr);
            if (dst != baseReg)
                EmitInstr(DebugInstr.R2(OpCode.MOV, (byte)dst, (byte)baseReg), src);
            return;
        }

        // 数组/通用 GEP
        // p^.field / r^[i] 这类「解引用后再取址」先判空：一旦指针无效，
        // 下面的槽号加减会把 Void 静默当成 0，之后再也无从区分。
        EmitNullReferenceCheck(basePtr, src);
        ushort addr = EnsureReg(basePtr);

        // 单维数组边界检查：
        //   getelementptr [N x T], ptr %base, i32 0, i32 %idx
        //   → BOUNDS R{idx}, R{N}（越界抛 ArrayBounds 运行时异常）
        if (
            inst is GetElementPtrInst gepArr
            && gepArr.SourceElementType is ArrayType arrTy
            && arrTy.Length > 0
            && inst.Operands.Count == 3
        )
        {
            var idxVal = inst.Operands[2];
            ushort idxReg = idxVal is ConstantInt cIdxVal
                ? LoadConstReg(cIdxVal.Value)
                : EnsureReg(idxVal);
            ushort maxReg = LoadConstReg(arrTy.Length);
            EmitInstr(DebugInstr.R2(OpCode.BOUNDS, (byte)idxReg, (byte)maxReg), src);
        }

        // 容器类型随索引逐级下降（数组 → 元素；结构体 → 字段）。
        // 索引必须换算成**扁平槽偏移**，而不是直接当槽号加：
        //   结构体字段 → 它之前所有字段的扁平槽数之和（FlatFieldOffset）
        //   数组元素   → i × 元素扁平槽数（ElementSlotStep）
        // 单层均匀结构体 / 标量数组的步长恰为 1，结果与旧的 addr+index 一致
        // ⇒ 对既有代码零行为变化。
        IrType container = (inst as GetElementPtrInst)?.SourceElementType ?? IrType.Int32;

        for (int i = 1; i < inst.Operands.Count; i++) {
            var idx = inst.Operands[i];

            // **LLVM GEP 语义**：第一个 index 是 ptr → pointee 的「入口」步骤，
            // 对结构体和数组都**不产生偏移、也不下降容器**；
            // 从第二个 index 起才是对容器的字段 / 元素索引。
            // （曾把入口的 0 当成「%B 的字段 0」而使容器多降一级：
            //   GEP %B, p, 0, 1 求出 g.a 的偏移而非 g.b 的 2。）
            if (i == 1 && idx is ConstantInt entry && entry.Value == 0)
                continue;

            if (idx is ConstantInt zero && zero.Value == 0) {
                container = DescendContainer(container, 0);
                continue;
            }

            ushort idxReg;

            if (idx is ConstantInt ci) {
                // 编译期索引：折算成扁平槽偏移
                int offset = container is ArrayType
                    ? (int)ci.Value * ElementSlotStep(container)
                    : FlatFieldOffset(container, (int)ci.Value);

                container = DescendContainer(container, (int)ci.Value);

                if (offset != 0) {
                    idxReg = (ushort)AllocReg();
                    EmitLoadInt((byte)idxReg, offset, null);
                } else {
                    continue;   // 偏移为 0：等价于旧代码里被 continue 掉的那一级
                }
            } else {
                // 运行时索引：偏移 = idx × 元素扁平槽数（步长为 1 时不必乘）。
                // step 必须用**下降前**的容器算 —— 运行时索引此刻仍在对它寻址；
                // 先下降会把 [2 x %A] 变成 %A，从而把步长误算成 1。
                idxReg = EnsureReg(idx);
                int step = ElementSlotStep(container);
                container = DescendContainer(container, 0);

                if (step != 1) {
                    ushort stepReg = (ushort)AllocReg();
                    EmitLoadInt((byte)stepReg, step, null);
                    ushort prod = (ushort)AllocReg();
                    EmitInstr(DebugInstr.R3(OpCode.IMUL, (byte)prod, (byte)idxReg, (byte)stepReg), null);
                    idxReg = prod;
                }
            }

            ushort sum = (ushort)AllocReg();
            EmitInstr(DebugInstr.R3(OpCode.IADD, (byte)sum, (byte)addr, (byte)idxReg), null);
            addr = sum;
        }
        if (addr != dst)
            EmitInstr(DebugInstr.R2(OpCode.MOV, (byte)dst, (byte)addr), src);

        // A8：记录“这个地址是全局槽号”（基址是聚合全局或由它派生的 GEP）。
        // 后续的标量 load/store 据此选 GETGI/SETGI 而不是 GETR/SETR；
        // `container` 是本次 GEP 最终指向的 pointee 类型，供聚合深拷贝计算槽数。
        if (_globalAddresses.ContainsKey(basePtr))
            _globalAddresses[inst] = container;
    }

    // ═══════════════════════════════════════
    //  控制流
    //  BranchInst: Condition, TrueDest, FalseDest
    //  SwitchInst: Condition, DefaultDest, Cases
    // ═══════════════════════════════════════

    private void EmitBr(BranchInst br, BasicBlock currentBlock, (string file, int line)? src)
    {
        if (!br.IsConditional) {
            // 无条件跳转: br label %dest
            EmitPhiMovesFor(currentBlock, br.TrueDest);
            EmitInstr(DebugInstr.Jmp(_bbLabels[br.TrueDest]), src);
        } else {
            // 条件跳转: br i1 %cond, label %true, label %false
            ushort cond = EnsureReg(br.Condition!);
            EmitPhiMovesFor(currentBlock, br.TrueDest);
            EmitPhiMovesFor(currentBlock, br.FalseDest!);
            EmitInstr(DebugInstr.Bz((byte)cond, _bbLabels[br.FalseDest!]), src);
            EmitInstr(DebugInstr.Jmp(_bbLabels[br.TrueDest]), null);
        }
    }

    private void EmitRet(ReturnInst ret, (string file, int line)? src)
    {
        if (ret.ReturnValue != null) {
            ushort val = EnsureReg(ret.ReturnValue);
            if (val != BytecodeBuilder.A0)
                EmitInstr(DebugInstr.R2(OpCode.MOV, BytecodeBuilder.A0, (byte)val), src);
        }
        EmitInstr(DebugInstr.Jmp(_epilogueLabel), src);
    }

    private void EmitSwitch(SwitchInst sw, BasicBlock currentBlock, (string file, int line)? src)
    {
        ushort sel = EnsureReg(sw.Condition);
        string defaultLbl = _bbLabels[sw.DefaultDest];

        EmitPhiMovesFor(currentBlock, sw.DefaultDest);
        foreach (var (_, caseBB) in sw.Cases)
            EmitPhiMovesFor(currentBlock, caseBB);

        foreach (var (caseVal, caseBB) in sw.Cases) {
            ushort caseReg = EnsureReg(caseVal);
            EmitInstr(
                DebugInstr.Branch(OpCode.BEQ, (byte)sel, (byte)caseReg, _bbLabels[caseBB]),
                src
            );
        }
        EmitInstr(DebugInstr.Jmp(defaultLbl), src);
    }

    // ═══════════════════════════════════════
    //  跨帧引用重定基（§6.3.6）
    // ═══════════════════════════════════════

    /// <summary>
    /// 调用点的重定基常数 <c>K</c>：指针实参的槽号在交给被调方之前必须减去它。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 后端的「指针」是**创建它的帧**里的相对槽号（<c>GETR/SETR</c> 的寻址是
    /// <c>addr = FP - 1 - slot</c>）。跨帧时被调方用**自己的 FP** 解引用，于是落到
    /// 被调方帧内 —— 既不越界也不报错，**静默错值**。这就是 <c>VAR_OUTPUT</c> /
    /// <c>VAR_IN_OUT</c> 端到端失败（及同帧之外 <c>REFERENCE TO</c> 失效）的根因。
    /// </para>
    /// <para>
    /// <b>推导</b>：调用点 <c>SP = FP_调用方 - 帧大小 - stackArgs</c>；<c>FRAME</c> 先把
    /// LR、再把 FP 压栈（两槽），故
    /// <c>FP_被调方 = FP_调用方 - (帧大小 + stackArgs + 2)</c>。把槽号减同一个差值后，
    /// 被调方解引用即回落到调用方的同一槽：
    /// <c>FP_被调方 - 1 - (s - K) = FP_调用方 - 1 - s</c>。逐层调用各自再减自身常数 ⇒ 自动复合。
    /// </para>
    /// <para>
    /// 「帧大小」= <see cref="_nextSlot"/>（= <c>FRAME</c> 的操作数）。
    /// </para>
    /// <para>
    /// <b>前提</b>：调用点 <c>SP</c> 必须恰好是 <c>FP - _nextSlot - stackArgs</c>。这与
    /// 已验收的栈传参约定（<c>6f6774d</c>）是**同一个**假设（<c>PUSH/POPN</c> 自平衡）。
    /// </para>
    /// </remarks>
    private int CrossFrameRebaseConstant(int stackArgs) => _nextSlot + stackArgs + 2;

    /// <summary>
    /// 把 <paramref name="srcReg"/> 里的指针槽号减去 <paramref name="k"/>，返回承载结果的寄存器。
    /// </summary>
    /// <remarks>
    /// <b>必须写进新寄存器</b>：<paramref name="srcReg"/> 来自 <c>EnsureReg</c> 的缓存，可能被
    /// 同一实参复用（如 <c>f(v, v)</c>），原地修改会被二次扣减。
    /// </remarks>
    private byte RebasePointerReg(ushort srcReg, int k)
    {
        byte kReg = (byte)AllocReg();
        EmitLoadInt(kReg, k, null);
        EmitInstr(DebugInstr.R3(OpCode.ISUB, kReg, (byte)srcReg, kReg), null);
        return kReg;
    }

    // ═══════════════════════════════════════
    //  函数调用
    //  IRBuilder CallInst: Operands[0] = callee, Operands[1..] = arguments
    //  用 Callee / ArgumentCount / GetArgument(i) 访问
    // ═══════════════════════════════════════

    private void EmitCall(CallInst callInst, (string file, int line)? src)
    {
        Function? callee = callInst.Callee as Function;
        string calleeName = callee?.Name ?? "";
        int argCount = callInst.ArgumentCount;

        // CALL-NRE：实参不得为空。命名绑定路径（ExpressionEmitter.ResolveCallArguments）可能
        // 留下未解析的槽位（null），后续 GetArgument(i).Type / EnsureReg 会直接抛
        // NullReferenceException（编译器崩溃而非诊断，见 docs/阶段0B设计.md §6.3.9）。
        // 前端已在 CALL-ARITY 切片拒绝此类调用；此处作为后端防线给出明确的契约错误。
        for (int i = 0; i < argCount; i++) {
            if (callInst.GetArgument(i) is null)
                throw new InvalidOperationException(
                    $"Call to '{calleeName}' has a null argument at position {i} "
                        + $"(argument count {argCount}, callee parameter count "
                        + $"{callee?.Parameters.Count ?? 0}). Every formal parameter must be "
                        + "bound by the frontend; a null slot indicates an unresolved "
                        + "argument binding."
                );
        }

        ushort hostIndex = 0;
        bool isHost =
            _hostFunctions != null && _hostFunctions.TryGetValue(calleeName, out hostIndex);

        if (!isHost && callee != null && callee.IsDeclaration && !_allowExternalCalls) {
            throw new InvalidOperationException(
                $"External function '{calleeName}' has no host handler. "
                    + $"Add it to hostFunctions map (hostFunctions[\"{calleeName}\"] = index)."
            );
        }

        // 外部模块函数（声明、非宿主）：_allowExternalCalls 时继续走普通调用路径。
        // 由于该声明不会在本模块发射实现，CallFunc 会保留为未解析 Addr32，交由链接器。

        // 宿主（声明式）调用：参数按**一个 A 位**传递，与聚合扩展前完全一致。
        // 宿主侧从 A0 起读寄存器，且声明的 IR 参数类型常是 STRING 之类的结构体
        // （STRING_254 ⇒ FlatSlotCount 255），聚合槽位规则对它们不适用。
        if (isHost) {
            if (argCount > 4) {
                for (int i = argCount - 1; i >= 4; i--) {
                    ushort argReg = EnsureReg(callInst.GetArgument(i));
                    EmitInstr(DebugInstr.R1(OpCode.PUSH, (byte)argReg), null);
                }
            }
            for (int i = 0; i < argCount && i < 4; i++) {
                ushort argReg = EnsureReg(callInst.GetArgument(i));
                byte dest = (byte)(BytecodeBuilder.A0 + i);
                if (argReg != dest)
                    EmitInstr(DebugInstr.R2(OpCode.MOV, dest, (byte)argReg), null);
            }

            // 切片 5：动态宿主调用按稳定名称记录 HostIdx（静态 ABI ID 不进表，§5 HOST-2）。
            // 必须在 EmitInstr 之前记录 —— 此时 _asm.PC 即 HCALL 操作码位置。
            RecordHostRef(calleeName, hostIndex, src);
            EmitInstr(DebugInstr.Hcall(hostIndex), src);

            // 回收调用方为 >4 参数压入的栈参数区。与普通 CALL 的 POPN 对称：
            // 不回收时，同一函数内每次宿主调用都会让 SP 永久下移，循环内最终栈溢出。
            if (argCount > 4)
                EmitInstr(DebugInstr.Popn((short)(argCount - 4)), null);

            InvalidateRegistersAcrossCall();

            // HCALL 把返回值写入 R[4]（= A0）。此前该路径假定宿主函数一律 void、从不回填
            // 返回值；对「有返回值的宿主函数」（如字符串标准库）需要这一最小适配。
            if (callInst.ProducesResult) {
                ushort hostDst = EnsureReg(callInst);
                if (hostDst != BytecodeBuilder.A0)
                    EmitInstr(DebugInstr.R2(OpCode.MOV, (byte)hostDst, BytecodeBuilder.A0), null);
            }
            return;
        }

        // 参数按**槽**占用 A0–A3 / 帧上参数区；被调用方用同一规则回填，两侧严格对称。
        //   槽 0..3 → A0–A3（纯标量时与扩展前逐字节一致）
        //   槽 ≥4   → 调用方帧的参数区
        // 刻意不用旧 PUSH/POP 收参路径：FRAME 与 PUSH 共用同一段 SP，被调用方的帧会
        // 覆盖调用方刚压入的参数，LEAVE 后 SP 复原导致 POP 只读到帧内数据。
        var argBase = new List<int>(argCount);
        int aSlotTotal = 0;
        for (int i = 0; i < argCount; i++) {
            argBase.Add(aSlotTotal);
            aSlotTotal += FlatSlotCount(callInst.GetArgument(i).Type);
        }
        int stackArgs = Math.Max(0, aSlotTotal - 4);

        // 跨帧引用重定基（§6.3.6）：指针实参交给被调方前按 K 平移槽号。
        int rebaseK = CrossFrameRebaseConstant(stackArgs);

        // 需要重定基的实参：IR 类型为指针（VAR_OUTPUT / VAR_IN_OUT / REFERENCE TO 形参，
        // 以及 FB 的 `__instance`）。`__instance` 由调用方传入的是**调用方槽号**，必须与
        // 其它指针一同按同一 K 平移，否则 FB 会以本帧 FP 解引用而落到自己的帧内
        // （FB-INSTANCE-ADDR，§6.3.11）。纳入后 FB 成员的 GETR/SETR 精确命中调用方实例。
        bool NeedsRebase(Value arg) => arg.Type is PointerType;

        // ① 帧上参数区：按**槽号降序**压栈，使第 s 个槽落在 SP0-k+(s-4) ——
        //    正是被调用方序言期待的 FP+2+(s-4)。
        //    用 PUSH（而非 PUSHN）：PUSHN 只预留不改值，没有"按 SP 偏移写"的指令。
        if (stackArgs > 0) {
            for (int i = argCount - 1; i >= 0; i--) {
                Value arg = callInst.GetArgument(i);
                int n = FlatSlotCount(arg.Type);
                int baseSlot = argBase[i];
                if (baseSlot + n <= 4)
                    continue;                       // 整个参数都在 A 位里

                for (int j = n - 1; j >= 0; j--) {
                    int s = baseSlot + j;
                    if (s < 4)
                        continue;                   // 该槽走 A 位，不压栈

                    if (n <= 1) {
                        // 标量：值在寄存器里，直接压栈
                        ushort vReg = ResolveScalarArgumentReg(callInst, i);
                        if (NeedsRebase(arg))
                            vReg = RebasePointerReg(vReg, rebaseK);
                        EmitInstr(DebugInstr.R1(OpCode.PUSH, (byte)vReg), null);
                    } else {
                        if (!TryGetAggregateSlotBase(arg, out int srcBase))
                            throw new InvalidOperationException(
                                $"Aggregate argument '{(arg.Name is { } nm ? "%" + nm : "(anonymous)")}' of "
                                    + $"{arg.Type.ToIRString()} has no stack slot to pass by value; only "
                                    + "allocas, parameters and aggregates loaded from them are supported.");

                        byte tmp = (byte)AllocReg();
                        EmitInstr(DebugInstr.Slot(OpCode.GETL, tmp, (short)(srcBase + j)), null);
                        EmitInstr(DebugInstr.R1(OpCode.PUSH, tmp), null);
                    }
                }
            }
        }

        // ② 前 4 槽 → A0–A3
        for (int i = 0; i < argCount; i++) {
            int baseSlot = argBase[i];
            int n = FlatSlotCount(callInst.GetArgument(i).Type);
            if (baseSlot >= 4)
                break;                              // 其余已在上面压栈

            Value arg = callInst.GetArgument(i);
            if (n <= 1) {
                ushort argReg = ResolveScalarArgumentReg(callInst, i);
                if (NeedsRebase(arg))
                    argReg = RebasePointerReg(argReg, rebaseK);
                byte dest = (byte)(BytecodeBuilder.A0 + baseSlot);
                if (argReg != dest)
                    EmitInstr(DebugInstr.R2(OpCode.MOV, dest, (byte)argReg), null);
            } else {
                if (!TryGetAggregateSlotBase(arg, out int srcBase))
                    throw new InvalidOperationException(
                        $"Aggregate argument '{(arg.Name is { } nm ? "%" + nm : "(anonymous)")}' of type "
                            + $"{arg.Type.ToIRString()} has no stack slot to pass by value; only allocas, "
                            + "parameters and aggregates loaded from them are supported.");

                for (int j = 0; j < n && baseSlot + j < 4; j++) {
                    // 同样走 _nextReg 而非 _asm.AllocTemp()：两者寄存器池起点都是 R8，
                    // AllocTemp 从空池取到的 R8 是第一个值寄存器，会覆盖仍活跃的地址。
                    byte tmp = (byte)AllocReg();
                    EmitInstr(DebugInstr.Slot(OpCode.GETL, tmp, (short)(srcBase + j)), null);
                    EmitInstr(
                        DebugInstr.R2(OpCode.MOV, (byte)(BytecodeBuilder.A0 + baseSlot + j), tmp), null);
                }
            }
        }

        if (_lrSlot >= 0)
            EmitInstr(DebugInstr.Slot(OpCode.SETL, BytecodeBuilder.LR, (short)_lrSlot), null);
        EmitInstr(DebugInstr.Call(calleeName), src);
        if (_lrSlot >= 0)
            EmitInstr(DebugInstr.Slot(OpCode.GETL, BytecodeBuilder.LR, (short)_lrSlot), null);

        // ③ 回收帧上参数区：LEAVE 已把被调用方的 SP 复原到调用方压栈后的位置，
        //    这里只需把预留的 k 槽还回去。必须在下一次压栈 / 下一轮循环之前做，
        //    否则参数区会随调用次数线性泄漏。
        if (stackArgs > 0)
            EmitInstr(DebugInstr.Popn((short)stackArgs), null);

        // 控制权已转移：作废可恢复缓存。被调方寄存器与调用方物理重合；
        // 跨调用活跃的指令结果已在定义处写回 spill 槽，按需 GETL 恢复。
        InvalidateRegistersAcrossCall();

        // 返回值
        if (callInst.ProducesResult) {
            ushort dst = EnsureReg(callInst);
            if (dst != BytecodeBuilder.A0)
                EmitInstr(DebugInstr.R2(OpCode.MOV, (byte)dst, BytecodeBuilder.A0), null);
        }
    }

    /// <summary>
    /// 标量/指针实参的寄存器求值。
    /// </summary>
    /// <remarks>
    /// A8：结构体比较辅助函数拿到的全局聚合地址在其<b>自己的帧</b>里无法按全局槽寻址，
    /// 故调用方先把该聚合复制到帧内临时区，再把临时区的栈槽号传过去；
    /// 后续的跨帧重定基照常应用（临时区属于调用方帧，语义与局部结构体一致）。
    /// </remarks>
    private ushort ResolveScalarArgumentReg(CallInst call, int index)
    {
        if (!_aggregateArgTemps.TryGetValue((call, index), out var temp))
            return EnsureReg(call.GetArgument(index));

        var srcRef = ResolveAggregateRef(call.GetArgument(index), null);
        EmitAggregateCopyRefs(
            srcRef,
            new AggregateRef(AggregateSpace.Stack, true, temp.baseSlot, 0),
            temp.count,
            "struct comparison operand"
        );
        return LoadConstReg(temp.baseSlot);
    }

    // ═══════════════════════════════════════
    //  PHI
    //  IRBuilder PHINode: Incoming 列表 (Value, Block)
    // ═══════════════════════════════════════

    private void EmitPhi(PHINode phi)
    {
        short slot = _phiSlots[phi];
        ushort dst = EnsureReg(phi);
        EmitInstr(DebugInstr.Slot(OpCode.GETL, (byte)dst, slot), null);
    }

    private void EmitPhiMovesFor(BasicBlock from, BasicBlock to)
    {
        foreach (var inst in to.Instructions) {
            if (inst is not PHINode phi)
                break;

            for (int i = 0; i < phi.Incoming.Count; i++) {
                var entry = phi.Incoming[i];
                if (ReferenceEquals(entry.Block, from)) {
                    short slot = _phiSlots[phi];
                    ushort valReg = EnsureReg(entry.Value);
                    EmitInstr(DebugInstr.Slot(OpCode.SETL, (byte)valReg, slot), null);
                }
            }
        }
    }

    // ═══════════════════════════════════════
    //  Select / ExtractValue / InsertValue
    // ═══════════════════════════════════════

    private void EmitSelect(Instruction inst, (string file, int line)? src)
    {
        ushort dst = EnsureReg(inst);
        ushort cond = EnsureReg(inst.Operands[0]);
        ushort trueVal = EnsureReg(inst.Operands[1]);
        ushort falseVal = EnsureReg(inst.Operands[2]);

        string tLbl = NewLabel("sel_t");
        string dLbl = NewLabel("sel_d");

        EmitInstr(DebugInstr.R2(OpCode.MOV, (byte)dst, (byte)falseVal), null);
        EmitInstr(DebugInstr.Bnz((byte)cond, tLbl), null);
        EmitInstr(DebugInstr.Jmp(dLbl), null);

        _asm.Label(tLbl);
        _df.Labels[tLbl] = _df.Instructions.Count;
        EmitInstr(DebugInstr.R2(OpCode.MOV, (byte)dst, (byte)trueVal), src);

        _asm.Label(dLbl);
        _df.Labels[dLbl] = _df.Instructions.Count;
    }

    private void EmitExtractValue(Instruction inst, (string file, int line)? src)
    {
        ushort dst = EnsureReg(inst);
        ushort s = EnsureReg(inst.Operands[0]);
        ushort fIdx =
            inst.Operands.Count > 1 && inst.Operands[1] is ConstantInt ci
                ? (ushort)ci.Value
                : (ushort)0;
        EmitInstr(DebugInstr.Sget((byte)dst, (byte)s, (byte)fIdx), src);
    }

    private void EmitInsertValue(Instruction inst, (string file, int line)? src)
    {
        ushort dst = EnsureReg(inst);
        ushort baseVal = EnsureReg(inst.Operands[0]);
        ushort value = EnsureReg(inst.Operands[1]);
        ushort fIdx =
            inst.Operands.Count > 2 && inst.Operands[2] is ConstantInt ci
                ? (ushort)ci.Value
                : (ushort)0;
        if (dst != baseVal)
            EmitInstr(DebugInstr.R2(OpCode.MOV, (byte)dst, (byte)baseVal), null);
        EmitInstr(DebugInstr.Sset((byte)value, (byte)dst, (byte)fIdx), src);
    }

    // ═══════════════════════════════════════
    //  EnsureReg: 确保值在寄存器中
    // ═══════════════════════════════════════

    /// <summary>
    /// 登记一次寄存器物化：值 → 寄存器，并记下**它是在哪个基本块里被写入的**。
    /// </summary>
    /// <remarks>
    /// 所有写 <see cref="_valReg"/> 的地方都必须经此登记，否则
    /// <see cref="InvalidateBlockLocalRegisters"/> 会因缺少块信息而**判定不出**
    /// 该条目能否跨块复用（缺项按"不是本块写的"处理，只会多物化一次，不会读脏）。
    /// </remarks>
    private void BindReg(Value val, ushort reg)
    {
        _valReg[val] = reg;
        _valRegBlock[val] = _currentBlock;
    }

    private ushort EnsureReg(Value val)
    {
        if (_valReg.TryGetValue(val, out ushort cached))
            return cached;

        EnsureRegisterBudget(val);

        ushort reg;

        if (val is ConstantInt ci) {
            reg = (ushort)AllocReg();
            EmitLoadInt((byte)reg, ci.Value, null);
            BindReg(val, reg);
            return reg;
        }

        if (val is ConstantFP fp) {
            reg = (ushort)AllocReg();
            EmitInstr(DebugInstr.Ldc((byte)reg, fp.Value), null);
            BindReg(val, reg);
            return reg;
        }

        if (val is ConstantPointerNull or ConstantAggregateZero) {
            reg = (ushort)AllocReg();
            EmitInstr(DebugInstr.R1(OpCode.LNIL, (byte)reg), null);
            BindReg(val, reg);
            return reg;
        }

        // F11：字符串常量全局（IR 里以 @str.N 的 ptr 形式**直接作 STRING 值**使用，
        // 如 `call Print(ptr @str.2)` / `store ptr @str.1, ptr %s`）。字节码侧它是全局槽里的
        // 一个 BcValue(String)（由 ByteCodeGenerator 的初始化写入），此处发射 GETG 取回。
        // 此前这条落进兜底分支 —— 分配寄存器却不发射装载，值实为「未写入」。
        if (val is GlobalVariable strGlobal && IsStringConstantGlobal(strGlobal)
            && _globalIndex.TryGetValue(strGlobal.Name ?? "", out short strSlot))
        {
            reg = (ushort)AllocReg();
            RecordGlobalRef(strGlobal, strSlot, null);
            EmitInstr(DebugInstr.Slot(OpCode.GETG, (byte)reg, strSlot), null);
            BindReg(val, reg);
            return reg;
        }

        // A8：聚合全局的“值”是它的**起始全局槽号** —— 字段/元素地址据此求偏移。
        // 不能像标量那样只 GETG 首槽，否则字段读写会对错槽位或越界。
        // 标量全局的旧路径（fallback）刻意不改：标量 load/store 在 EmitLoad/EmitStore 里
        // 已分别走 GETG/SETG，此处不应重复发射。
        if (val is GlobalVariable aggGlobal && IsAggregateGlobal(aggGlobal))
        {
            reg = (ushort)AllocReg();
            EmitLoadInt((byte)reg, _globalIndex[aggGlobal.Name!], null);
            BindReg(val, reg);
            _globalAddresses[aggGlobal] = aggGlobal.ValueType;
            return reg;
        }

        if (_paramSlots.TryGetValue(val, out short paramSlot)) {
            reg = (ushort)AllocReg();
            EmitInstr(DebugInstr.Slot(OpCode.GETL, (byte)reg, paramSlot), null);
            BindReg(val, reg);
            return reg;
        }

        if (_allocaSlots.TryGetValue(val, out short allocaSlot)) {
            reg = (ushort)AllocReg();
            EmitLoadInt((byte)reg, allocaSlot, null);
            BindReg(val, reg);
            return reg;
        }

        // 跨调用活跃的指令结果：定义时已写回 spill 槽，调用后按需恢复。
        // 当前指令是“定义”本身时不能走这里 —— 目标寄存器还没有写入，
        // 必须由各发射器分配并生成真正的定义。
        if (!ReferenceEquals(val, _currentDef)
            && _spillSlots.TryGetValue(val, out short spillSlot))
        {
            reg = (ushort)AllocReg();
            EmitInstr(DebugInstr.Slot(OpCode.GETL, (byte)reg, spillSlot), null);
            BindReg(val, reg);
            return reg;
        }

        // 兜底分支：值既不是常量 / 参数 / alloca 基址，就是**没有 spill 槽的指令结果**
        // （不可重物化且不跨调用活跃）。这类条目永不被块边界作废
        // （见 InvalidateBlockLocalRegisters 的 CanReload 前置判断），
        // 此处记块只是保持"所有写入点都经 BindReg"这一约定，读不到也不影响正确性。
        reg = (ushort)AllocReg();
        BindReg(val, reg);
        return reg;
    }

    /// <summary>
    /// 基本块边界作废可恢复值的寄存器缓存。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="InvalidateRegistersAcrossCall"/> 处理的是**被调方覆写寄存器**这一条失效来源；
    /// 本条处理的是**另一条**：**物化点不在支配位置上**。
    /// 可重物化条目是**惰性**物化的 —— 谁先用到它，就在**那一点所在的基本块**里发射
    /// <c>LDC</c>/<c>GETL</c>/<c>LNIL</c>。若那一点落在条件块 / 兄弟分支 / 循环体里，
    /// 寄存器就只在"该块被执行"的路径上被写入；沿**未执行该物化**的路径进入后续块时，
    /// 复用同一条缓存 ⇒ 读到陈旧值（干净 VM 下为 0），并被当作**槽号 / 地址**使用
    /// ⇒ **静默写错位置**。这不是"寄存器被覆写"，因此
    /// <see cref="InvalidateRegistersAcrossCall"/> 拦不住它。
    /// </para>
    /// <para>
    /// 判据用**块**而不是支配树：每进入一个基本块，就丢弃所有"不是在**本块**或
    /// **入口块**里写进去的"可重物化条目。
    /// </para>
    /// <list type="bullet">
    /// <item>**本块**：块内的物化指令在同一块里先于其后的使用（顺序支配），保留。</item>
    /// <item>**入口块**：入口块支配函数内**所有**块，故其物化指令在任何路径上都已执行
    /// （<see cref="BasicBlock"/> 无后继接口，但这一条不需要 CFG 遍历）。保留它正是为了
    /// 让绝大多数程序（数组/结构体基址在 <c>alloca</c> 处即入口块物化）**字节码不变**。</item>
    /// <item>其它块：物化点可能不在支配位置上（条件块 / 兄弟分支 / 循环体），丢弃并重新物化。</item>
    /// </list>
    /// <para>
    /// 于每个使用点，其物化指令都落在**本块内、且在其之前** —— 无需额外的 CFG / 支配分析。
    /// 代价只发生在"惰性物化点不在入口块"的形状上（即本缺陷的形状），见提交说明的实测对照。
    /// </para>
    /// <para>
    /// <b>只取可恢复条目</b>：可重物化值，或具有稳定 spill 槽的指令结果。
    /// 前者重新发射装载指令，后者在定义处已写回槽、可通过 <c>GETL</c> 恢复。
    /// 没有 spill 槽的指令结果**不能**这样丢 —— 丢了 <see cref="EnsureReg"/>
    /// 会走到"分配一个从未写入的新寄存器"的分支（比读旧值更糟）；
    /// 它们由 SSA 支配关系保证，其定义块必定支配所有使用块，跨块复用本来就安全。
    /// </para>
    /// </remarks>
    private void InvalidateBlockLocalRegisters(BasicBlock entering)
    {
        if (_valReg.Count == 0)
            return;

        List<Value>? stale = null;
        foreach (var kv in _valReg)
        {
            if (!CanReload(kv.Key))
                continue;

            // 缺块信息（如序言里物化的）按"不是本块写的"处理 ⇒ 丢弃，只不过多物化一次。
            if (_valRegBlock.TryGetValue(kv.Key, out var defBlock) && defBlock is not null)
            {
                // 同块内已物化：其后的使用是同块顺序支配的。
                if (ReferenceEquals(defBlock, entering))
                    continue;
                // 入口块物化：入口块支配所有块，任何路径上都已执行。
                if (ReferenceEquals(defBlock, _entryBlock))
                    continue;
            }

            (stale ??= new List<Value>()).Add(kv.Key);
        }

        if (stale is null)
            return;
        foreach (var key in stale) {
            _valReg.Remove(key);
            _valRegBlock.Remove(key);
        }
    }

    /// <summary>
    /// 跨调用作废可恢复的寄存器缓存：可重物化值，以及具有稳定 spill 槽的指令结果。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每个函数的 <c>_nextReg</c> 都从 <c>R8</c> 起算，因此**被调方使用的寄存器与
    /// 调用方活跃寄存器物理重合**。调用约定按 caller-saved 处理：调用返回后，
    /// 调用方不能再信任任何 <c>R8+</c> 缓存。
    /// </para>
    /// <para>
    /// 可重物化条目（常量 / 参数 / alloca 基址 / 字符串全局）在下一次
    /// <see cref="EnsureReg"/> 时重新发射装载指令。指令结果若在调用后仍然活跃，
    /// <see cref="Analyze"/> 已按 liveness 为它分配 spill 槽，且定义处已用
    /// <c>SETL</c> 写回；这里直接丢弃缓存，<see cref="EnsureReg"/> 通过 <c>GETL</c> 恢复。
    /// </para>
    /// <para>
    /// **没有 spill 槽的指令结果不能作废** —— 它们不会跨调用活跃，丢弃只会让
    /// <see cref="EnsureReg"/> 分配到一个从未写入的寄存器。GEP 结果若没有槽也仍保留：
    /// <see cref="EmitGEP"/> 每次都从刷新的基址重新做 <c>MOV</c>/<c>IADD</c>，
    /// 因而会被间接修正。
    /// </para>
    /// </remarks>
    private void InvalidateRegistersAcrossCall()
    {
        if (_valReg.Count == 0)
            return;

        List<Value>? stale = null;
        foreach (var kv in _valReg)
        {
            if (CanReload(kv.Key))
                (stale ??= new List<Value>()).Add(kv.Key);
        }

        if (stale is null)
            return;
        foreach (var key in stale) {
            _valReg.Remove(key);
            _valRegBlock.Remove(key);
        }
    }

    /// <summary>该值能否在 <see cref="EnsureReg"/> 里重新发射装载指令得到。</summary>
    private bool IsRematerializable(Value val)
        => val is ConstantInt or ConstantFP or ConstantPointerNull or ConstantAggregateZero
           || val is AllocaInst
           || (val is GlobalVariable g && IsStringConstantGlobal(g))
           // A8：聚合全局的“值”是起始全局槽号，可用一条 LDC 重新物化。
           // 必须与字符串全局同列：全局变量不是 Instruction，不会进入 spill 候选，
           // 若沿用旧寄存器，一次 CALL 后读到的就是被调方残留的值（实测 SlotOverflow）。
           || (val is GlobalVariable agg && IsAggregateGlobal(agg))
           || _paramSlots.ContainsKey(val);

    /// <summary>
    /// 该值在寄存器被破坏后能否恢复：要么可重物化，要么有稳定的 spill 槽。
    /// </summary>
    private bool CanReload(Value val) =>
        IsRematerializable(val) || _spillSlots.ContainsKey(val);

    /// <summary>
    /// 字符串常量全局：`Module.AddGlobalString` 的产物（`IsConstant` + `ConstantDataArray`
    /// 初始化器）。F11。
    /// </summary>
    /// <remarks>
    /// 本仓库该组合只来自字符串字面量（`ExpressionEmitter.EmitStringConstant`），
    /// 故可安全地把它当作「全局槽里的一个 <see cref="BcValue"/>（`Tag=String`）」。
    /// 它可重物化 —— <see cref="EnsureReg"/> 每次重新发射 <c>GETG</c> 即得，
    /// 故必须与常量 / 参数 / alloca 基址同列，避免跨块 / 跨调用复用陈旧寄存器
    /// （设计档 §3.4）。
    /// </remarks>
    private static bool IsStringConstantGlobal(GlobalVariable gv) =>
        gv.IsConstant && gv.Initializer is ConstantDataArray;

    // ═══════════════════════════════════════
    //  辅助方法
    // ═══════════════════════════════════════

    /// <summary>
    /// 加载整数常量到寄存器。全部通过 LDC 从统一常量池加载。
    /// </summary>
    private void EmitLoadInt(byte reg, long value, (string file, int line)? src)
    {
        EmitInstr(DebugInstr.LdcInt(reg, value), src);
    }

    /// <summary>分配临时寄存器并加载常量。</summary>
    private ushort LoadConstReg(long value)
    {
        ushort reg = (ushort)AllocReg();
        EmitLoadInt((byte)reg, value, null);
        return reg;
    }

    private bool TryGetGlobalSlot(Value val, out short index)
    {
        index = 0;
        if (val is GlobalVariable g && _globalIndex.TryGetValue(g.Name ?? "", out index))
            return true;
        return false;
    }

    /// <summary>
    /// 发射 GETG/SETG <b>之前</b>记录一条全局引用（0B 步骤 3 · 切片 3）。
    /// </summary>
    /// <remarks>
    /// 立即数偏移由汇编器按"当前 PC + 2"计算，因此本方法必须先于 <c>EmitInstr</c> 调用。
    ///
    /// <para>
    /// 源位置沿用 <c>EmitInstr</c> 的同一套回退规则（<c>src ?? _currentSrc</c>），
    /// 使重定位记录与调试源映射指同一行；没有源信息时显式记为 Unknown。
    /// </para>
    /// </remarks>
    private void RecordGlobalRef(Value ptr, short slot, (string file, int line)? src)
    {
        string name = (ptr as GlobalVariable)?.Name ?? "";

        var location = (src ?? _currentSrc) is { } s
            ? new RelocationSourceLocation(s.file, s.line, -1)
            : RelocationSourceLocation.Unknown;

        _asm.RecordGlobalIdx(name, slot, location);
    }

    /// <summary>
    /// 把一次动态宿主调用登记为 <see cref="RelocationKind.HostIdx"/> 记录（切片 5）。
    /// </summary>
    /// <remarks>
    /// <b>只登记动态宿主</b>：路径由 <see cref="HostFunctionRegistry"/> 之类的名字→ID 映射
    /// 解析出 <paramref name="localId"/>，符号身份即 <paramref name="calleeName"/>。
    /// <c>HostCallIds</c> 常量（数学内置、Print 族）在发射点<b>只有 ID 没有名字</b>，
    /// 且属 ABI 冻结常量、无需重编号，故<b>不登记</b>（§5 <c>HOST-2</c> 裁定）。
    ///
    /// <para>
    /// 源位置沿用 <c>RecordGlobalRef</c> 同一套回退规则（<c>src ?? _currentSrc</c>），
    /// 使重定位记录与调试源映射指同一行；没有源信息时显式记为 Unknown（不伪造）。
    /// </para>
    /// </remarks>
    private void RecordHostRef(string calleeName, ushort localId, (string file, int line)? src)
    {
        var location = (src ?? _currentSrc) is { } s
            ? new RelocationSourceLocation(s.file, s.line, -1)
            : RelocationSourceLocation.Unknown;

        _asm.RecordHostIdx(calleeName, localId, location);
    }

    /// <summary>
    /// 从 <see cref="DebugInstr"/> 取源位置（0B 步骤 3 · 切片 4）。
    /// </summary>
    /// <remarks>
    /// <c>EmitInstr</c> 在发射前把 <c>src ?? _currentSrc</c> 写进 <c>di.SrcFile/SrcLine</c>
    /// （<c>FunctionEmitter.cs:263-267</c>），因此这里能取到与调试源映射同一行。
    ///
    /// <para>
    /// "有无源信息"的判据与 <c>DebugModule.cs:65</c> 完全一致
    /// （<c>SrcFile != null && SrcLine &gt; 0</c>），不另立规则 —— 两处判据若分叉，
    /// 就会出现"源映射有行、重定位记录没有"的不一致。
    /// </para>
    /// </remarks>
    private static RelocationSourceLocation SourceOf(DebugInstr di)
        => di.SrcFile != null && di.SrcLine > 0
            ? new RelocationSourceLocation(di.SrcFile, di.SrcLine, -1)
            : RelocationSourceLocation.Unknown;

    private bool IsScalarAlloca(Value ptr) =>
        ptr is AllocaInst ai && ai.AllocatedType is not ArrayType && !_structAllocas.Contains(ptr);

    /// <summary>
    /// IEC <c>STRING</c> / <c>WSTRING</c> 的 IR 表示 —— `TypeMapper` 生成的命名结构体
    /// <c>%STRING_n</c> / <c>%WSTRING_n</c>（<c>TypeMapping/TypeMapper.cs:46-78</c>）。
    /// </summary>
    /// <remarks>
    /// F11：字节码侧不照搬 <c>{i32,[n x i8]}</c> 的定长存储形状 —— 一个字符串值是<b>单个</b>槽里的
    /// <see cref="BcValue"/>（<c>Tag=String</c>）。故按名识别，使这类类型计 1 槽、且不按聚合处理。
    /// </remarks>
    private static bool IsStringNamedType(IrType type) =>
        type is NamedStructType n
        && (n.Name.StartsWith("STRING_", StringComparison.Ordinal)
            || n.Name.StartsWith("WSTRING_", StringComparison.Ordinal));

    /// <summary>聚合类型（在扁平栈上占多个槽、无法装入单个寄存器）。</summary>
    private static bool IsAggregateType(IrType type) =>
        type is ArrayType or StructType
        || (type is NamedStructType n && !IsStringNamedType(n));

    /// <summary>
    /// 聚合在扁平栈上占用的<b>槽数</b>（递归到标量叶节点，每个叶节点一槽）。
    /// </summary>
    /// <remarks>
    /// <b>必须递归</b>：<c>%B = type { %A, i32 }</c>（其中 <c>%A = type { i32, i32 }</c>）
    /// 的扁平槽数是 <b>3</b>，而 <c>Body.ElementTypes.Count</c> 只有 2 ——
    /// 用后者会让 <c>g.b</c> 落到错误槽位（2026-10-15 实证）。
    /// <para>
    /// <b>字符串命名结构体例外</b>（F11）：<c>%STRING_n</c> / <c>%WSTRING_n</c> 计 <b>1</b> 槽 ——
    /// 整个值是一个 <see cref="BcValue"/>（<c>Tag=String</c>），不展开成 <c>1 + n</c> 个槽。
    /// </para>
    /// </remarks>
    internal static int FlatSlotCount(IrType type) => type switch
    {
        NamedStructType n when IsStringNamedType(n) => 1,
        ArrayType a => a.Length * FlatSlotCount(a.ElementType),
        StructType st => st.ElementTypes.Sum(FlatSlotCount),
        NamedStructType { Body: { } body } => FlatSlotCount(body),
        _ => 1,
    };

    /// <summary>
    /// GEP 里容器「每个元素」对应的<b>扁平槽步长</b>。
    /// 结构体的索引是**字段号**、按 <see cref="FlatFieldOffset"/> 折算，故这里给 1。
    /// </summary>
    private static int ElementSlotStep(IrType container) => container switch
    {
        ArrayType a => FlatSlotCount(a.ElementType),
        StructType => 1,
        NamedStructType { Body: { } b } => ElementSlotStep(b),
        _ => 1,
    };

    /// <summary>
    /// 结构体第 <paramref name="fieldIndex"/> 个字段的<b>扁平槽偏移</b>
    /// （= 它之前所有字段的扁平槽数之和）。
    /// </summary>
    /// <remarks>
    /// 单层均匀结构体（每个字段一槽）结果等于字段号本身 —— 与原来的
    /// <c>addr + idx</c> 一致，故对既有代码零行为变化；
    /// 非均匀 / 嵌套时才产生差值：<c>%B = {%A, i32}</c> 的字段 1 偏移是
    /// <b>2</b>（因为 %A 占 2 槽），不是 1 —— 这正是 2026-10-15 实证中
    /// <c>g.a.y</c> 与 <c>g.b</c> 互相覆盖的根因。
    /// </remarks>
    private static int FlatFieldOffset(IrType container, int fieldIndex)
    {
        if (container is NamedStructType { Body: { } body })
            return FlatFieldOffset(body, fieldIndex);

        if (container is StructType st)
            return st.ElementTypes.Take(fieldIndex).Sum(FlatSlotCount);

        // 非结构体容器（数组 / 标量）：索引本身就是槽偏移
        return fieldIndex;
    }

    /// <summary>GEP 索引下降一级后的容器类型。</summary>
    private static IrType DescendContainer(IrType container, int index) => container switch
    {
        ArrayType a => a.ElementType,
        StructType st => (uint)index < (uint)st.ElementTypes.Count ? st.ElementTypes[index] : st,
        NamedStructType { Body: { } b } => DescendContainer(b, index),
        _ => container,
    };

    /// <summary>
    /// 求聚合值的<b>源基槽</b>。四种来源：
    /// ① 函数参数（含聚合按值实参，槽位见 <c>_paramSlots</c>）；
    /// ② alloca 本身；③ 聚合 <c>load</c> 的结果（取其被加载指针的槽）；
    /// ④ A9：聚合 load 的帧内临时副本（源不是 alloca 时）。
    /// </summary>
    private bool TryGetAggregateSlotBase(Value val, out int baseSlot)
    {
        if (_paramSlots.TryGetValue(val, out short paramSlot)) { baseSlot = paramSlot; return true; }
        if (_allocaSlots.TryGetValue(val, out short allocaSlot)) { baseSlot = allocaSlot; return true; }
        if (_aggregateTempSlots.TryGetValue(val, out short tempSlot)) { baseSlot = tempSlot; return true; }
        if (val is LoadInst li && _allocaSlots.TryGetValue(li.Pointer, out short loadedSlot))
        {
            baseSlot = loadedSlot;
            return true;
        }
        baseSlot = 0;
        return false;
    }

    // ═══════════════════════════════════════
    //  A8/A9：聚合存储位置（栈槽 / 全局槽）
    // ═══════════════════════════════════════

    private enum AggregateSpace { Stack, Global }

    /// <summary>
    /// 一个聚合对象的存储位置：<see cref="Space"/> 决定用 GETR/SETR 还是 GETGI/SETGI；
    /// <see cref="IsStatic"/> 为真时槽号在编译期已知（<see cref="Slot"/>），
    /// 否则槽号在 <see cref="Reg"/> 里（运行时地址）。
    /// </summary>
    private readonly record struct AggregateRef(
        AggregateSpace Space, bool IsStatic, int Slot, ushort Reg);

    /// <summary>聚合全局变量：类型是聚合且已分配全局槽。</summary>
    private bool IsAggregateGlobal(Value val) =>
        val is GlobalVariable gv
        && IsAggregateType(gv.ValueType)
        && _globalIndex.ContainsKey(gv.Name ?? "");

    /// <summary>
    /// 把指针值解析为聚合存储位置。
    /// 顺序：聚合全局变量 → 由全局聚合派生的地址（GETGI 空间）→
    /// 编译期已知的栈槽 → 运行时栈地址。
    /// </summary>
    private AggregateRef ResolveAggregateRef(Value ptr, (string file, int line)? src)
    {
        if (IsAggregateGlobal(ptr))
            return new AggregateRef(
                AggregateSpace.Global, true, _globalIndex[((GlobalVariable)ptr).Name!], 0);

        if (_globalAddresses.ContainsKey(ptr))
            return new AggregateRef(AggregateSpace.Global, false, 0, EnsureReg(ptr));

        if (_allocaSlots.TryGetValue(ptr, out short stackSlot))
            return new AggregateRef(AggregateSpace.Stack, true, stackSlot, 0);

        EmitNullReferenceCheck(ptr, src);
        return new AggregateRef(AggregateSpace.Stack, false, 0, EnsureReg(ptr));
    }

    /// <summary>把聚合<b>值</b>解析为存储位置（参数 / alloca / 聚合 load 结果 / 全局）。</summary>
    private AggregateRef ResolveAggregateValue(Value val)
    {
        if (TryGetAggregateSlotBase(val, out int stackBase))
            return new AggregateRef(AggregateSpace.Stack, true, stackBase, 0);

        if (IsAggregateGlobal(val))
            return new AggregateRef(
                AggregateSpace.Global, true, _globalIndex[((GlobalVariable)val).Name!], 0);

        if (_globalAddresses.ContainsKey(val))
            return new AggregateRef(AggregateSpace.Global, false, 0, EnsureReg(val));

        throw new InvalidOperationException(
            $"Aggregate value '{val.Name ?? "(anonymous)"}' of {val.Type.ToIRString()} has no "
                + "stack slot or global slot; only allocas, parameters, aggregate loads and "
                + "global aggregates are supported as aggregate sources.");
    }

    /// <summary>
    /// 聚合深拷贝。两侧都是编译期已知的栈槽时走既有的 <c>GETL/SETL</c> 立即数路径
    /// （字节码与扩展前完全一致）；否则用游标寄存器逐槽读写。
    /// </summary>
    /// <remarks>
    /// 游标寄存器必须是<b>新的</b>寄存器：直接 <c>IINC</c> 缓存里的地址寄存器
    /// 会让同一地址在后续使用点偏掉。
    /// </remarks>
    private void EmitAggregateCopyRefs(AggregateRef src, AggregateRef dst, int count, string what)
    {
        if (src is { Space: AggregateSpace.Stack, IsStatic: true }
            && dst is { Space: AggregateSpace.Stack, IsStatic: true })
        {
            EmitAggregateCopy(src.Slot, dst.Slot, count, what);
            return;
        }

        ushort srcCursor = 0;
        ushort dstCursor = 0;

        if (!(src.Space == AggregateSpace.Stack && src.IsStatic))
            srcCursor = MaterializeAggregateCursor(src);
        if (!(dst.Space == AggregateSpace.Stack && dst.IsStatic))
            dstCursor = MaterializeAggregateCursor(dst);

        EnsureRegisterBudget(null);
        ushort tmp = (ushort)AllocReg();

        for (int i = 0; i < count; i++)
        {
            if (src.Space == AggregateSpace.Global)
                EmitInstr(DebugInstr.R2(OpCode.GETGI, (byte)tmp, (byte)srcCursor), null);
            else if (src.IsStatic)
                EmitInstr(DebugInstr.Slot(OpCode.GETL, (byte)tmp, (short)(src.Slot + i)), null);
            else
                EmitInstr(DebugInstr.R2(OpCode.GETR, (byte)tmp, (byte)srcCursor), null);

            if (dst.Space == AggregateSpace.Global)
                EmitInstr(DebugInstr.R2(OpCode.SETGI, (byte)tmp, (byte)dstCursor), null);
            else if (dst.IsStatic)
                EmitInstr(DebugInstr.Slot(OpCode.SETL, (byte)tmp, (short)(dst.Slot + i)), null);
            else
                EmitInstr(DebugInstr.R2(OpCode.SETR, (byte)tmp, (byte)dstCursor), null);

            if (srcCursor != 0)
                EmitInstr(DebugInstr.R1(OpCode.IINC, (byte)srcCursor), null);
            if (dstCursor != 0)
                EmitInstr(DebugInstr.R1(OpCode.IINC, (byte)dstCursor), null);
        }
    }

    /// <summary>把一个聚合目标区段的每个槽写成整数 0（`store zeroinitializer`）。</summary>
    private void EmitAggregateZeroFill(AggregateRef dst, int count)
    {
        EnsureRegisterBudget(null);
        ushort zero = (ushort)AllocReg();
        EmitLoadInt((byte)zero, 0, null);
        ushort cursor = 0;

        if (!(dst.Space == AggregateSpace.Stack && dst.IsStatic))
            cursor = MaterializeAggregateCursor(dst);

        for (int i = 0; i < count; i++)
        {
            if (dst.Space == AggregateSpace.Global)
                EmitInstr(DebugInstr.R2(OpCode.SETGI, (byte)zero, (byte)cursor), null);
            else if (dst.IsStatic)
                EmitInstr(DebugInstr.Slot(OpCode.SETL, (byte)zero, (short)(dst.Slot + i)), null);
            else
                EmitInstr(DebugInstr.R2(OpCode.SETR, (byte)zero, (byte)cursor), null);

            if (cursor != 0)
                EmitInstr(DebugInstr.R1(OpCode.IINC, (byte)cursor), null);
        }
    }

    private ushort MaterializeAggregateCursor(AggregateRef r)
    {
        EnsureRegisterBudget(null);
        ushort cursor = (ushort)AllocReg();

        if (r.IsStatic)
        {
            EmitLoadInt((byte)cursor, r.Slot, null);
        }
        else
        {
            EmitInstr(DebugInstr.R2(OpCode.MOV, (byte)cursor, (byte)r.Reg), null);
        }

        return cursor;
    }

    /// <summary>为帧内临时区预留 <paramref name="count"/> 个槽（不写入 <c>_nextSlot</c>）。</summary>
    private void EnsureTempRegion(int count, string what)
    {
        if (count < 1 || _nextSlot + count > short.MaxValue)
            throw new InvalidOperationException(
                $"Local frame overflow: {what} needs {count} slot(s) at base {_nextSlot}, "
                    + $"which exceeds the {short.MaxValue}-slot limit of a single frame.");
    }

    /// <summary>C8：分配一个临时寄存器（优先复用已释放的号，再递增）。</summary>
    private byte AllocReg()
    {
        byte reg;
        if (_freeRegs.Count > 0)
        {
            reg = _freeRegs.Min;
            _freeRegs.Remove(reg);
        }
        else
        {
            reg = _nextReg++;
        }
        EnsureRegisterBudget(null);
        _currentTempRegs.Add(reg);
        return reg;
    }

    private void EnsureRegisterBudget(Value? val)
    {
        if (_nextReg <= 250)
            return;

        string what = val is null
            ? "aggregate copy"
            : $"{val.GetType().Name} '{val.Name ?? "(anonymous)"}'"
                + $": _nextReg reached {_nextReg}";

        throw new InvalidOperationException(
            $"Out of registers while materializing {what}. "
                + "The register file is byte-addressed and wrapping to 0 would silently "
                + "clobber R0 (the zero register).");
    }

    /// <summary>结构体比较辅助函数（A1/F5 生成的 <c>__st_cmp_N</c>）。</summary>
    private static bool IsStructCompareCall(CallInst call) =>
        (call.Callee as Function)?.Name is { } name
        && name.StartsWith("__st_cmp_", StringComparison.Ordinal);

    /// <summary>
    /// 指针值是否指向全局聚合（直接是聚合全局，或由它派生）。
    /// 只用于<b>静态可判定</b>的形状（GEP 链的根是全局变量），分析与发射两侧共用。
    /// </summary>
    private static bool TryGetGlobalPointee(Value val, out IrType pointee)
    {
        switch (val)
        {
            case GlobalVariable gv when IsAggregateType(gv.ValueType):
                pointee = gv.ValueType;
                return true;

            case GetElementPtrInst gep when TryGetGlobalPointee(gep.Operands[0], out var baseType):
                pointee = DescendPointee(baseType, gep);
                return true;

            default:
                pointee = IrType.Int32;
                return false;
        }
    }

    /// <summary>把 GEP 的索引作用到 pointee 类型上（与 <see cref="EmitGEP"/> 的下降规则一致）。</summary>
    private static IrType DescendPointee(IrType container, GetElementPtrInst gep)
    {
        for (int i = 1; i < gep.Operands.Count; i++)
        {
            var idx = gep.Operands[i];
            if (i == 1 && idx is ConstantInt entry && entry.Value == 0)
                continue;

            int fieldIndex = idx is ConstantInt ci ? (int)ci.Value : 0;
            container = DescendContainer(container, fieldIndex);
        }
        return container;
    }

    /// <summary>
    /// 发射聚合的<b>逐槽深拷贝</b>（src 基槽 → dst 基槽，共 <paramref name="count"/> 槽）。
    /// </summary>
    /// <remarks>
    /// 用 <c>GETL</c>/<c>SETL</c> 的立即数槽号，<b>不引入新指令、不改 ISA</b>。
    /// IR 的 <c>load</c>/<c>store</c> 聚合是<b>整个对象</b>的值语义，逐槽拷贝即忠实实现；
    /// 这与任何 ST 语言语义裁定无关 —— "<c>load %Point</c> 却只搬一个槽"
    /// 本身就是 IR 语义违背（docs/阶段0B设计.md §1.4.2 分层表 / §6.3.4 <c>T-COV-1/2/3</c>）。
    /// </remarks>
    private void EmitAggregateCopy(int srcBase, int dstBase, int count, string what)
    {
        if (srcBase + count > short.MaxValue || dstBase + count > short.MaxValue)
            throw new InvalidOperationException(
                $"Aggregate copy of {count} slot(s) would exceed the {short.MaxValue}-slot "
                    + $"limit (src base {srcBase}, dst base {dstBase}) for {what}; "
                    + "GETL/SETL encode the slot as a signed 16-bit immediate.");

        // 临时寄存器必须走**发射器自己的** _nextReg，不能用 _asm.AllocTemp()：
        // 后者的寄存器池起点也是 R8（BytecodeAssembler.cs:107 `_nextTemp = 8`），
        // 与 FunctionEmitter._nextReg 完全重合 —— 从空池取到的正是 R8，也就是
        // 第一个值寄存器，会把仍活跃的地址/值静默覆盖。
        byte tmp = (byte)AllocReg();
        for (int i = 0; i < count; i++)
        {
            EmitInstr(DebugInstr.Slot(OpCode.GETL, tmp, (short)(srcBase + i)), null);
            EmitInstr(DebugInstr.Slot(OpCode.SETL, tmp, (short)(dstBase + i)), null);
        }
    }

    private string NewLabel(string hint) => $"__{_name}_{hint}_{_labelCounter++}";
}