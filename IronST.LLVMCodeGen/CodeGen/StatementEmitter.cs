using IronST.IRBuilder.IR;
using IronST.Common.Types;
using IronST.Frontend.AST;
using IronST.LLVMCodeGen.Exceptions;

namespace IronST.LLVMCodeGen.CodeGen;

public sealed class StatementEmitter
{
    private readonly CodeGenContext _ctx;
    private readonly ExpressionEmitter _expr;

    public StatementEmitter(CodeGenContext ctx, ExpressionEmitter expr)
    {
        _ctx = ctx;
        _expr = expr;
    }

    /// <summary>
    /// 发射单条语句。
    /// </summary>
    public void Emit(StatementNode stmt)
    {
        if (!_ctx.IsInPrologue && stmt.Span.Line > 0)
            _ctx.Debug.SetLocation(stmt.Span.Line);

        switch (stmt) {
            case AssignmentNode a: EmitAssignment(a); break;
            case RefAssignmentNode ra: EmitRefAssignment(ra); break;
            case IfNode i: EmitIf(i); break;
            case ForNode f: EmitFor(f); break;
            case WhileNode w: EmitWhile(w); break;
            case RepeatNode r: EmitRepeat(r); break;
            case CaseNode c: EmitCase(c); break;
            case CallStatementNode call: EmitCallStatement(call); break;
            case ReturnNode: EmitReturn(); break;
            case ExitNode: EmitExit(); break;
            default:
                throw new CodeGenException(
                    $"未支持的语句类型: {stmt.GetType().Name}",
                    _ctx.Module.SourceFilename ?? "", stmt.Span.Line);
        }
    }

    /// <summary>
    /// 批量发射语句列表。
    /// </summary>
    public void EmitAll(IEnumerable<StatementNode>? statements)
    {
        if (statements == null) return;
        foreach (var stmt in statements)
            Emit(stmt);
    }

    // ════════════════════════════════════════════
    //  赋值
    // ════════════════════════════════════════════

    private void EmitAssignment(AssignmentNode assign)
    {
        var value = _expr.Emit(assign.Value);
        var ptr = _expr.EmitAddress(assign.Target);

        // 源⑥：按**目标变量类型**转换右值。
        // `avg : REAL := sum;`（sum 是 INT）以前把整数直接存进实数槽，
        // 读出来是垃圾值；`d : DINT := r;`（r 是 REAL）以前把实数直接存进整数槽。
        // 转换只在「整数 ↔ 实数」边界上发生（同侧位宽差异由 VM 的 64 位槽吸收）。
        value = _expr.ConvertToTargetType(value, assign.Value, assign.Target.ResolvedType);

        var store = _ctx.Builder.CreateStore(value, ptr);

        // 右值是字面量时不会产生任何 IR 指令，全靠这条 store 兜住行号，
        // 否则 "x := 0;" 这类语句根本没法下断点。
        if (_ctx.Debug.IsEnabled && !_ctx.IsInPrologue)
            _ctx.Debug.AttachLocation(store);
    }

    /// <summary>
    /// REF= 引用赋值：将 Value 的地址存入 Target（REFERENCE 变量）。
    /// </summary>
    private void EmitRefAssignment(RefAssignmentNode ra)
    {
        var targetPtr = _expr.EmitAddress(ra.Target);
        var valueAddr = _expr.EmitAddress(ra.Value);
        var store = _ctx.Builder.CreateStore(valueAddr, targetPtr);

        if (_ctx.Debug.IsEnabled && !_ctx.IsInPrologue)
            _ctx.Debug.AttachLocation(store);
    }

    /// <summary>
    /// 把当前语句的行号挂到指令上。
    ///
    /// IF / WHILE / FOR / REPEAT 的头部往往完全由字面量构成（IF 1 &lt; 2、
    /// FOR i := 1 TO 10），整个语句不产生任何能承载行号的表达式指令，
    /// 于是那些行根本下不了断点。条件分支本身是每个控制流结构必定产出的
    /// 指令，拿它兜底最稳妥。
    ///
    /// 调用时机有讲究：必须在 EmitAll(嵌套体) 之前，因为那时 _currentLine
    /// 还是本语句的行；子语句一发射就会把行号改写掉。
    /// </summary>
    private void AttachStmtLocation(Instruction inst)
    {
        if (_ctx.Debug.IsEnabled && !_ctx.IsInPrologue)
            _ctx.Debug.AttachLocation(inst);
    }

    // ════════════════════════════════════════════
    //  IF（展平 ELSIF 为链式条件分支）
    // ════════════════════════════════════════════

    private void EmitIf(IfNode ifStmt)
    {
        var func = _ctx.CurrentFunction;

        var thenBlock = new BasicBlock(_ctx.NameGen.NextBlock("if.then"));
        func.AppendBasicBlock(thenBlock);

        // 为每个 ELSIF 创建条件块和体块
        var elsifPairs = new List<(ExpressionNode Cond, BasicBlock CondBlock, BasicBlock BodyBlock)>();
        foreach (var (elsifCond, _) in ifStmt.ElsIfBranches) {
            var cb = new BasicBlock(_ctx.NameGen.NextBlock("elsif.cond"));
            var bb = new BasicBlock(_ctx.NameGen.NextBlock("elsif.then"));
            func.AppendBasicBlock(cb);
            func.AppendBasicBlock(bb);
            elsifPairs.Add((elsifCond, cb, bb));
        }

        BasicBlock? elseBlock = null;
        if (ifStmt.ElseBody is { Count: > 0 }) {
            elseBlock = new BasicBlock(_ctx.NameGen.NextBlock("if.else"));
            func.AppendBasicBlock(elseBlock);
        }

        var endBlock = new BasicBlock(_ctx.NameGen.NextBlock("if.end"));
        func.AppendBasicBlock(endBlock);

        // 初始条件分支
        var ifCond = _expr.Emit(ifStmt.Condition);
        var firstTarget = elsifPairs.Count > 0
            ? elsifPairs[0].CondBlock
            : (elseBlock ?? endBlock);
        AttachStmtLocation(_ctx.Builder.CreateCondBr(ifCond, thenBlock, firstTarget));

        // THEN 分支
        _ctx.Builder.InsertBlock = thenBlock;
        EmitAll(ifStmt.ThenBody);
        if (!BlockTerminated(thenBlock))
            _ctx.Builder.CreateBr(endBlock);

        // ELSIF 分支链
        for (int i = 0; i < elsifPairs.Count; i++) {
            var (elsifCondition, condBlock, bodyBlock) = elsifPairs[i];

            _ctx.Builder.InsertBlock = condBlock;

            // ELSIF 的条件是独立的一行。不重设的话 _currentLine 还停在上一个
            // 分支体上，条件块会被标成 THEN 体的行 —— 走 ELSIF 那条路时，高亮
            // 会先停在根本没执行的 THEN 体上。
            if (!_ctx.IsInPrologue && elsifCondition.Span.Line > 0)
                _ctx.Debug.SetLocation(elsifCondition.Span.Line);

            var eCondVal = _expr.Emit(elsifCondition);
            var nextTarget = (i + 1 < elsifPairs.Count)
                ? elsifPairs[i + 1].CondBlock
                : (elseBlock ?? endBlock);
            _ctx.Builder.CreateCondBr(eCondVal, bodyBlock, nextTarget);

            _ctx.Builder.InsertBlock = bodyBlock;
            EmitAll(ifStmt.ElsIfBranches[i].Body);
            if (!BlockTerminated(bodyBlock))
                _ctx.Builder.CreateBr(endBlock);
        }

        // ELSE 分支
        if (elseBlock != null) {
            _ctx.Builder.InsertBlock = elseBlock;
            EmitAll(ifStmt.ElseBody!);
            if (!BlockTerminated(elseBlock))
                _ctx.Builder.CreateBr(endBlock);
        }

        // 合并块
        _ctx.Builder.InsertBlock = endBlock;
    }

    // ════════════════════════════════════════════
    //  FOR
    // ════════════════════════════════════════════

    private void EmitFor(ForNode forStmt)
    {
        var func = _ctx.CurrentFunction;
        var counterAlloca = _ctx.GetAlloca(forStmt.Variable);
        var counterType = _ctx.GetVarType(forStmt.Variable);
        var counterIrType = _ctx.TypeMapper.Map(counterType);

        // 初始化
        var initVal = _expr.Emit(forStmt.From);
        initVal = _expr.EnsureType(initVal, forStmt.From.ResolvedType!, counterType);
        _ctx.Builder.CreateStore(initVal, counterAlloca);

        // 创建块
        var condBlock = new BasicBlock(_ctx.NameGen.NextBlock("for.cond"));
        var bodyBlock = new BasicBlock(_ctx.NameGen.NextBlock("for.body"));
        var incBlock = new BasicBlock(_ctx.NameGen.NextBlock("for.inc"));
        var endBlock = new BasicBlock(_ctx.NameGen.NextBlock("for.end"));

        func.AppendBasicBlock(condBlock);
        func.AppendBasicBlock(bodyBlock);
        func.AppendBasicBlock(incBlock);
        func.AppendBasicBlock(endBlock);

        _ctx.LoopEndBlocks.Push(endBlock);
        _ctx.Builder.CreateBr(condBlock);

        // 条件判断
        _ctx.Builder.InsertBlock = condBlock;
        var cur = _ctx.Builder.CreateLoad(counterIrType, counterAlloca, "for.cur");
        var limitVal = _expr.Emit(forStmt.To);
        limitVal = _expr.EnsureType(limitVal, forStmt.To.ResolvedType!, counterType);
        var cmp = _ctx.Builder.CreateICmp(ICmpPredicate.Sle, cur, limitVal, "for.cmp");
        AttachStmtLocation(_ctx.Builder.CreateCondBr(cmp, bodyBlock, endBlock));

        // 循环体
        _ctx.Builder.InsertBlock = bodyBlock;
        EmitAll(forStmt.Body);
        if (!BlockTerminated(bodyBlock))
            _ctx.Builder.CreateBr(incBlock);

        // 递增
        _ctx.Builder.InsertBlock = incBlock;
        var curVal = _ctx.Builder.CreateLoad(counterIrType, counterAlloca, "for.cur2");
        var stepVal = forStmt.By != null
            ? _expr.EnsureType(_expr.Emit(forStmt.By), forStmt.By.ResolvedType!, counterType)
            : (Value)new ConstantInt(counterIrType, 1);
        var nextVal = _ctx.Builder.CreateAdd(curVal, stepVal, "for.next");
        _ctx.Builder.CreateStore(nextVal, counterAlloca);
        _ctx.Builder.CreateBr(condBlock);

        _ctx.LoopEndBlocks.Pop();
        _ctx.Builder.InsertBlock = endBlock;
    }

    // ════════════════════════════════════════════
    //  WHILE
    // ════════════════════════════════════════════

    private void EmitWhile(WhileNode whileStmt)
    {
        var func = _ctx.CurrentFunction;

        var condBlock = new BasicBlock(_ctx.NameGen.NextBlock("while.cond"));
        var bodyBlock = new BasicBlock(_ctx.NameGen.NextBlock("while.body"));
        var endBlock = new BasicBlock(_ctx.NameGen.NextBlock("while.end"));

        func.AppendBasicBlock(condBlock);
        func.AppendBasicBlock(bodyBlock);
        func.AppendBasicBlock(endBlock);

        _ctx.LoopEndBlocks.Push(endBlock);
        _ctx.Builder.CreateBr(condBlock);

        _ctx.Builder.InsertBlock = condBlock;
        var condVal = _expr.Emit(whileStmt.Condition);
        AttachStmtLocation(_ctx.Builder.CreateCondBr(condVal, bodyBlock, endBlock));

        _ctx.Builder.InsertBlock = bodyBlock;
        EmitAll(whileStmt.Body);
        if (!BlockTerminated(bodyBlock))
            _ctx.Builder.CreateBr(condBlock);

        _ctx.LoopEndBlocks.Pop();
        _ctx.Builder.InsertBlock = endBlock;
    }

    // ════════════════════════════════════════════
    //  REPEAT ... UNTIL
    // ════════════════════════════════════════════

    private void EmitRepeat(RepeatNode repeatStmt)
    {
        var func = _ctx.CurrentFunction;

        var bodyBlock = new BasicBlock(_ctx.NameGen.NextBlock("repeat.body"));
        var endBlock = new BasicBlock(_ctx.NameGen.NextBlock("repeat.end"));

        func.AppendBasicBlock(bodyBlock);
        func.AppendBasicBlock(endBlock);

        _ctx.LoopEndBlocks.Push(endBlock);
        _ctx.Builder.CreateBr(bodyBlock);

        _ctx.Builder.InsertBlock = bodyBlock;
        EmitAll(repeatStmt.Body);

        if (repeatStmt.UntilCondition != null) {
            // 循环体已经发射完，_currentLine 早被内层语句改写；先拨回 REPEAT
            // 自己那一行，否则断点会挂到循环体最后一条语句上。
            if (_ctx.Debug.IsEnabled && repeatStmt.Span.Line > 0)
                _ctx.Debug.SetLocation(repeatStmt.Span.Line);

            var untilVal = _expr.Emit(repeatStmt.UntilCondition);
            AttachStmtLocation(_ctx.Builder.CreateCondBr(untilVal, endBlock, bodyBlock));
        } else {
            _ctx.Builder.CreateBr(bodyBlock); // 无条件 → 无限循环
        }

        _ctx.LoopEndBlocks.Pop();
        _ctx.Builder.InsertBlock = endBlock;
    }

    // ════════════════════════════════════════════
    //  CASE（单值 / 枚举走 switch；区间标签用范围判定，不再逐值展开）
    // ════════════════════════════════════════════

    private void EmitCase(CaseNode caseStmt)
    {
        var func = _ctx.CurrentFunction;
        var endBlock = new BasicBlock(_ctx.NameGen.NextBlock("case.end"));
        var defaultBlock = new BasicBlock(_ctx.NameGen.NextBlock("case.default"));

        var selectorVal = _expr.Emit(caseStmt.Selector);
        var selectorIrType = _ctx.TypeMapper.Map(caseStmt.Selector.ResolvedType!);

        // F12：区间标签**不再**逐值展开为 switch case（大区间 ⇒ 逐 case 线性 BEQ 链
        // 的分支距离超出 Off8）。每个含区间的分支分配一个「条件块」，按源码分支顺序
        // 做范围判定 `lo <= x <= hi`：命中 → 该分支体，否则 → 下一条件块（末者 else → default）。
        // 单值 / 枚举标签仍走 switch（编码不变）。**无区间标签时输出与旧实现逐字节一致**。
        int rangeBranchCount = 0;
        foreach (var branch in caseStmt.Branches) {
            foreach (var caseLabel in branch.Labels) {
                if (caseLabel.RangeStart.HasValue && caseLabel.RangeEnd.HasValue) {
                    rangeBranchCount++;
                    break;
                }
            }
        }

        var rangeCondBlocks = new List<BasicBlock>(rangeBranchCount);
        for (int i = 0; i < rangeBranchCount; i++) {
            var condBlock = new BasicBlock(_ctx.NameGen.NextBlock("case.range"));
            func.AppendBasicBlock(condBlock);
            rangeCondBlocks.Add(condBlock);
        }

        var switchInst = new SwitchInst(
            selectorVal, rangeCondBlocks.Count > 0 ? rangeCondBlocks[0] : defaultBlock);

        var branchPairs = new List<(List<StatementNode> Body, BasicBlock Block)>();
        var rangeChecks =
            new List<(BasicBlock Cond, BasicBlock Body, List<(long Lo, long Hi)> Ranges)>();
        int nextRange = 0;

        foreach (var branch in caseStmt.Branches) {
            var label = new BasicBlock(_ctx.NameGen.NextBlock("case.arm"));
            func.AppendBasicBlock(label);

            var ranges = new List<(long Lo, long Hi)>();
            foreach (var caseLabel in branch.Labels) {
                if (caseLabel.SingleValue.HasValue) {
                    switchInst.AddCase(
                        new ConstantInt(selectorIrType, caseLabel.SingleValue.Value), label);
                } else if (caseLabel.RangeStart.HasValue && caseLabel.RangeEnd.HasValue) {
                    ranges.Add((caseLabel.RangeStart.Value, caseLabel.RangeEnd.Value));
                } else if (caseLabel.EnumValue != null) {
                    // 枚举值标签：Color.Red / Red → 解析为枚举成员数值
                    long enumValue = ResolveEnumCaseValue(caseStmt, caseLabel);
                    switchInst.AddCase(new ConstantInt(selectorIrType, enumValue), label);
                }
            }

            if (ranges.Count > 0)
                rangeChecks.Add((rangeCondBlocks[nextRange++], label, ranges));

            branchPairs.Add((branch.Body, label));
        }

        func.AppendBasicBlock(defaultBlock);
        func.AppendBasicBlock(endBlock);

        _ctx.Builder.AddInstruction(switchInst);

        // 区间判定链：条件块内取该分支全部区间判定的 OR，命中跳该分支体。
        for (int i = 0; i < rangeChecks.Count; i++) {
            var (condBlock, bodyBlock, ranges) = rangeChecks[i];
            _ctx.Builder.InsertBlock = condBlock;

            Value? matched = null;
            foreach (var (lo, hi) in ranges) {
                var lower = _ctx.Builder.CreateICmp(
                    ICmpPredicate.Sge, selectorVal,
                    new ConstantInt(selectorIrType, lo), "case.ge");
                var upper = _ctx.Builder.CreateICmp(
                    ICmpPredicate.Sle, selectorVal,
                    new ConstantInt(selectorIrType, hi), "case.le");
                var inRange = _ctx.Builder.CreateAnd(lower, upper, "case.in");
                matched = matched is null
                    ? inRange
                    : _ctx.Builder.CreateOr(matched, inRange, "case.or");
            }

            var nextBlock = i + 1 < rangeChecks.Count
                ? rangeChecks[i + 1].Cond
                : defaultBlock;
            AttachStmtLocation(_ctx.Builder.CreateCondBr(matched!, bodyBlock, nextBlock));
        }

        foreach (var (body, block) in branchPairs) {
            _ctx.Builder.InsertBlock = block;
            EmitAll(body);
            if (!BlockTerminated(block))
                _ctx.Builder.CreateBr(endBlock);
        }

        _ctx.Builder.InsertBlock = defaultBlock;
        if (caseStmt.ElseBody is { Count: > 0 })
            EmitAll(caseStmt.ElseBody);
        if (!BlockTerminated(defaultBlock))
            _ctx.Builder.CreateBr(endBlock);

        _ctx.Builder.InsertBlock = endBlock;
    }

    /// <summary>
    /// 解析 CASE 枚举值标签（"Color.Red" 或 "Red"）为枚举成员数值。
    /// 优先使用显式成员值（EnumMemberValues），否则回退为成员索引。
    /// </summary>
    private static long ResolveEnumCaseValue(CaseNode caseStmt, CaseLabelNode caseLabel)
    {
        var enumType = caseStmt.Selector.ResolvedType;
        string member = caseLabel.EnumValue!;
        int dot = member.LastIndexOf('.');
        if (dot >= 0)
            member = member[(dot + 1)..];

        if (enumType?.EnumMemberValues != null
            && enumType.EnumMemberValues.TryGetValue(member, out long explicitValue))
            return explicitValue;

        if (enumType?.EnumValues != null)
        {
            for (int i = 0; i < enumType.EnumValues.Count; i++)
            {
                if (string.Equals(enumType.EnumValues[i], member, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        throw new CodeGenException(
            $"CASE 枚举标签 '{caseLabel.EnumValue}' 无法解析为枚举 '{enumType?.Name ?? "?"}' 的成员");
    }

    // ════════════════════════════════════════════
    //  函数调用（语句）
    // ════════════════════════════════════════════

    private void EmitCallStatement(CallStatementNode call)
    {
        var value = _expr.EmitCall(call.Name, call.Arguments, call.Span);

        // 无返回值的调用（如 Print(0)）实参全为字面量时，整条语句不产生任何
        // 可挂载行号的指令，会变成"下不了断点"。用调用结果本身兜底。
        if (_ctx.Debug.IsEnabled && !_ctx.IsInPrologue && value is Instruction inst)
            _ctx.Debug.AttachLocation(inst);
    }

    // ════════════════════════════════════════════
    //  RETURN
    // ════════════════════════════════════════════

    private void EmitReturn()
    {
        var func = _ctx.CurrentFunction;

        if (func.FunctionType.ReturnType == IrType.Void) {
            _ctx.Builder.CreateRetVoid();
        } else {
            var returnAlloca = _ctx.GetAlloca(func.Name!);
            var retVal = _ctx.Builder.CreateLoad(
                func.FunctionType.ReturnType, returnAlloca, "ret");
            _ctx.Builder.CreateRet(retVal);
        }
    }

    // ════════════════════════════════════════════
    //  EXIT
    // ════════════════════════════════════════════

    private void EmitExit()
    {
        if (_ctx.LoopEndBlocks.Count == 0)
            throw new CodeGenException("EXIT 语句不在循环内");

        var endBlock = _ctx.LoopEndBlocks.Peek();
        _ctx.Builder.CreateBr(endBlock);

        var unreachable = new BasicBlock(_ctx.NameGen.NextBlock("after.exit"));
        _ctx.CurrentFunction.AppendBasicBlock(unreachable);
        _ctx.Builder.InsertBlock = unreachable;
    }

    // ════════════════════════════════════════════
    //  辅助
    // ════════════════════════════════════════════

    private static bool BlockTerminated(BasicBlock block)
        => block.Terminator != null;
}