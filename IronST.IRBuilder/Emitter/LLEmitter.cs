using System.Globalization;
using System.Text;
using IronST.IRBuilder.IR;

namespace IronST.IRBuilder.Emitter;

/// <summary>将 IR 对象模型序列化为符合 LLVM 22 规范的 .ll 文本</summary>
public class LLEmitter
{
    private readonly StringBuilder _sb = new();
    private Dictionary<Value, string> _names = new();
    private int _unnamedCounter;

    /// <summary>发射整个模块为 .ll 文本</summary>
    public string Emit(Module module)
    {
        _sb.Clear();
        _names.Clear();

        // 模块级元数据
        if (module.SourceFilename != null)
            _sb.AppendLine($"source_filename = \"{EscStr(module.SourceFilename)}\"");
        if (module.TargetDataLayout != null)
            _sb.AppendLine($"target datalayout = \"{module.TargetDataLayout}\"");
        if (module.TargetTriple != null)
            _sb.AppendLine($"target triple = \"{module.TargetTriple}\"");

        if (_sb.Length > 0)
            _sb.AppendLine();

        // 命名类型
        foreach (var nt in module.NamedTypes)
        {
            _sb.Append($"%{nt.Name} = type ");
            EmitStructBody(nt.Body!);
            _sb.AppendLine();
        }
        if (module.NamedTypes.Count > 0)
            _sb.AppendLine();

        // 全局变量
        foreach (var gv in module.GlobalVariables)
        {
            EmitGlobalVar(gv);
            _sb.AppendLine();
        }

        // 函数声明
        foreach (var f in module.Functions.Where(f => f.IsDeclaration))
        {
            EmitFuncDecl(f);
            _sb.AppendLine();
        }

        // 函数定义
        foreach (var f in module.Functions.Where(f => !f.IsDeclaration))
        {
            EmitFuncDef(f);
            _sb.AppendLine();
        }

        // ---- 加在这里：输出 metadata 段 ----
        EmitMetadata(module.Metadata);

        return _sb.ToString().TrimEnd('\n', '\r') + "\n";
    }

    private void EmitMetadata(MetadataTable table)
    {
        if (table.All.Count == 0 && table.AllNamed.Count == 0)
            return;

        _sb.AppendLine();

        foreach (var (id, node) in table.All.OrderBy(kv => kv.Key))
        {
            _sb.Append($"!{id} = ");
            EmitMetadataNode(node);
            _sb.AppendLine();
        }

        foreach (var (name, node) in table.AllNamed)
        {
            _sb.Append($"!{name} = ");
            EmitMetadataNode(node);
            _sb.AppendLine();
        }
    }

    private void EmitMetadataNode(MetadataNode node)
    {
        switch (node)
        {
            case MDString s:
                _sb.Append($"!\"{EscStr(s.Value)}\"");
                break;
            case MDTuple t:
                _sb.Append("!{");
                for (int i = 0; i < t.Operands.Count; i++)
                {
                    if (i > 0)
                        _sb.Append(", ");
                    EmitMetadataOperand(t.Operands[i]);
                }
                _sb.Append('}');
                break;
            default:
                _sb.Append($"!\"<unsupported: {node.GetType().Name}>\"");
                break;
        }
    }

    // metadata 段
    private void EmitMetadataOperand(object? operand)
    {
        switch (operand)
        {
            case MetadataNode md:
                if (md.Id >= 0)
                    _sb.Append($"!{md.Id}");
                else
                    EmitMetadataNode(md);
                break;
            case int i:
                _sb.Append($"i32 {i}");
                break;
            case long l:
                _sb.Append($"i64 {l}");
                break;
            case string s:
                _sb.Append($"!\"{EscStr(s)}\"");
                break;
            case null:
                _sb.Append("null");
                break;
            default:
                _sb.Append(operand.ToString());
                break;
        }
    }

    // ================================================================
    // 全局变量
    // ================================================================

    private void EmitGlobalVar(GlobalVariable gv)
    {
        _sb.Append($"@{gv.Name} = ");
        if (gv.Linkage != LinkageType.External)
            _sb.Append(LinkageStr(gv.Linkage) + " ");
        if (gv.IsUnnamedAddr)
            _sb.Append("unnamed_addr ");
        if (gv.Visibility != VisibilityStyle.Default)
            _sb.Append(VisStr(gv.Visibility) + " ");
        if (gv.ThreadLocal != ThreadLocalMode.None)
            _sb.Append($"thread_local({ThreadLocalStr(gv.ThreadLocal)}) ");
        _sb.Append(gv.IsConstant ? "constant " : "global ");
        EmitType(gv.ValueType);
        _sb.Append(' ');
        if (gv.Initializer != null)
            EmitConstant(gv.Initializer);
        else
            _sb.Append("zeroinitializer");
        if (gv.Section != null)
            _sb.Append($", section \"{EscStr(gv.Section)}\"");
        if (gv.Alignment > 0)
            _sb.Append($", align {gv.Alignment}");
    }

    // ================================================================
    // 函数
    // ================================================================

    private void EmitFuncDecl(Function func)
    {
        _sb.Append("declare ");
        EmitFuncHeader(func, emitParamNames: false);
    }

    private void EmitFuncDef(Function func)
    {
        _names.Clear();
        _unnamedCounter = 0;
        AllocNames(func);

        _sb.Append("define ");
        EmitFuncHeader(func, emitParamNames: true);
        _sb.Append(" {");

        foreach (var bb in func.BasicBlocks)
        {
            _sb.AppendLine();
            EmitBasicBlock(bb);
        }

        _sb.AppendLine();
        _sb.Append('}');
    }

    private void EmitFuncHeader(Function func, bool emitParamNames)
    {
        if (func.Linkage != LinkageType.External)
            _sb.Append(LinkageStr(func.Linkage) + " ");
        if (func.Visibility != VisibilityStyle.Default)
            _sb.Append(VisStr(func.Visibility) + " ");
        if (func.CallingConvention != CallingConv.C)
            _sb.Append(CallingConvStr(func.CallingConvention) + " ");
        foreach (var attr in func.Attributes)
            _sb.Append(attr + " ");

        EmitType(func.FunctionType.ReturnType);
        _sb.Append($" @{func.Name}(");

        for (int i = 0; i < func.Parameters.Count; i++)
        {
            if (i > 0)
                _sb.Append(", ");
            EmitType(func.Parameters[i].Type);
            if (emitParamNames)
                _sb.Append($" %{_names[func.Parameters[i]]}");
        }
        if (func.FunctionType.IsVarArg)
        {
            if (func.Parameters.Count > 0)
                _sb.Append(", ");
            _sb.Append("...");
        }
        _sb.Append(')');

        if (func.Alignment.HasValue)
            _sb.Append($" align {func.Alignment.Value}");
    }

    // ================================================================
    // 值名称分配
    // ================================================================

    private void AllocNames(Function func)
    {
        foreach (var arg in func.Parameters)
            _names[arg] = !string.IsNullOrEmpty(arg.Name)
                ? arg.Name
                : (_unnamedCounter++).ToString();

        foreach (var bb in func.BasicBlocks)
        {
            _names[bb] = !string.IsNullOrEmpty(bb.Name) ? bb.Name : (_unnamedCounter++).ToString();
            foreach (var inst in bb.Instructions)
            {
                if (inst.ProducesResult)
                    _names[inst] = !string.IsNullOrEmpty(inst.Name)
                        ? inst.Name
                        : (_unnamedCounter++).ToString();
            }
        }
    }

    // ================================================================
    // 基本块与指令
    // ================================================================

    private void EmitBasicBlock(BasicBlock bb)
    {
        _sb.Append($"{_names[bb]}:");
        _sb.AppendLine();
        foreach (var inst in bb.Instructions)
        {
            _sb.Append("  ");
            EmitInstruction(inst);
            _sb.AppendLine();
        }
    }

    private void EmitInstruction(Instruction inst)
    {
        if (inst.ProducesResult)
            _sb.Append($"%{_names[inst]} = ");

        switch (inst)
        {
            case ReturnInst r:
                EmitRet(r);
                break;
            case BranchInst b:
                EmitBr(b);
                break;
            case SwitchInst s:
                EmitSwitch(s);
                break;
            case UnreachableInst:
                _sb.Append("unreachable");
                break;
            case BinaryOperator bo:
                EmitBinOp(bo);
                break;
            case ICmpInst ic:
                EmitICmp(ic);
                break;
            case FCmpInst fc:
                EmitFCmp(fc);
                break;
            case AllocaInst al:
                EmitAlloca(al);
                break;
            case LoadInst ld:
                EmitLoad(ld);
                break;
            case StoreInst st:
                EmitStore(st);
                break;
            case GetElementPtrInst gep:
                EmitGEP(gep);
                break;
            case CastInst ci:
                EmitCast(ci);
                break;
            case PHINode phi:
                EmitPhi(phi);
                break;
            case SelectInst sel:
                EmitSelect(sel);
                break;
            case CallInst call:
                EmitCall(call);
                break;
            case ExtractValueInst ev:
                EmitExtractValue(ev);
                break;
            case InsertValueInst iv:
                EmitInsertValue(iv);
                break;
            default:
                throw new NotSupportedException($"不支持的指令: {inst.GetType().Name}");
        }

        // 输出指令上的 metadata 附加项，如 , !dbg !5
        if (inst.MetadataAttachments != null)
        {
            foreach (var (kind, node) in inst.MetadataAttachments)
            {
                _sb.Append($", !{kind} !{node.Id}");
            }
        }
    }

    private void EmitRet(ReturnInst r)
    {
        if (r.ReturnValue != null)
        {
            _sb.Append("ret ");
            EmitTypedValue(r.ReturnValue);
        }
        else
            _sb.Append("ret void");
    }

    private void EmitBr(BranchInst b)
    {
        if (b.IsConditional)
        {
            _sb.Append("br ");
            EmitTypedValue(b.Condition!);
            _sb.Append($", label %{_names[b.TrueDest]}, label %{_names[b.FalseDest!]}");
        }
        else
            _sb.Append($"br label %{_names[b.TrueDest]}");
    }

    private void EmitSwitch(SwitchInst s)
    {
        _sb.Append("switch ");
        EmitTypedValue(s.Condition);
        _sb.Append($", label %{_names[s.DefaultDest]} [");
        foreach (var (v, d) in s.Cases)
        {
            _sb.AppendLine();
            _sb.Append($"    {v.Type.ToIRString()} {v.Value}, label %{_names[d]}");
        }
        _sb.AppendLine();
        _sb.Append("  ]");
    }

    private void EmitBinOp(BinaryOperator bo)
    {
        _sb.Append(BinOpStr(bo.Op));
        if (bo.NoSignedWrap)
            _sb.Append(" nsw");
        if (bo.NoUnsignedWrap)
            _sb.Append(" nuw");
        _sb.Append(' ');
        EmitType(bo.LHS.Type);
        _sb.Append(' ');
        EmitValueRef(bo.LHS);
        _sb.Append(", ");
        EmitValueRef(bo.RHS);
    }

    private void EmitICmp(ICmpInst ic)
    {
        _sb.Append($"icmp {ICmpStr(ic.Predicate)} ");
        EmitType(ic.LHS.Type);
        _sb.Append(' ');
        EmitValueRef(ic.LHS);
        _sb.Append(", ");
        EmitValueRef(ic.RHS);
    }

    private void EmitFCmp(FCmpInst fc)
    {
        _sb.Append($"fcmp {FCmpStr(fc.Predicate)} ");
        EmitType(fc.LHS.Type);
        _sb.Append(' ');
        EmitValueRef(fc.LHS);
        _sb.Append(", ");
        EmitValueRef(fc.RHS);
    }

    private void EmitAlloca(AllocaInst al)
    {
        _sb.Append("alloca ");
        EmitType(al.AllocatedType);
        if (al.ArraySize != null)
        {
            _sb.Append(", ");
            EmitType(al.ArraySize.Type);
            _sb.Append(' ');
            EmitValueRef(al.ArraySize);
        }
        if (al.Alignment > 0)
            _sb.Append($", align {al.Alignment}");
    }

    private void EmitLoad(LoadInst ld)
    {
        _sb.Append("load ");
        EmitType(ld.LoadedType);
        _sb.Append(", ");
        EmitTypedValue(ld.Pointer);
        if (ld.Alignment > 0)
            _sb.Append($", align {ld.Alignment}");
    }

    private void EmitStore(StoreInst st)
    {
        _sb.Append("store ");
        EmitTypedValue(st.StoredValue);
        _sb.Append(", ");
        EmitTypedValue(st.Pointer);
        if (st.Alignment > 0)
            _sb.Append($", align {st.Alignment}");
    }

    private void EmitGEP(GetElementPtrInst gep)
    {
        _sb.Append("getelementptr");
        if (gep.InBounds)
            _sb.Append(" inbounds");
        _sb.Append(' ');
        EmitType(gep.SourceElementType);
        _sb.Append(", ");
        EmitTypedValue(gep.Pointer);
        foreach (var idx in gep.Indices)
        {
            _sb.Append(", ");
            EmitTypedValue(idx);
        }
    }

    private void EmitCast(CastInst ci)
    {
        _sb.Append(CastOpStr(ci.Op));
        _sb.Append(' ');
        EmitTypedValue(ci.Source);
        _sb.Append(" to ");
        EmitType(ci.Type);
    }

    private void EmitPhi(PHINode phi)
    {
        _sb.Append("phi ");
        EmitType(phi.Type);
        for (int i = 0; i < phi.Incoming.Count; i++)
        {
            if (i > 0)
                _sb.Append(',');
            _sb.Append(" [ ");
            EmitValueRef(phi.Incoming[i].Value);
            _sb.Append($", %{_names[phi.Incoming[i].Block]} ]");
        }
    }

    private void EmitSelect(SelectInst sel)
    {
        _sb.Append("select ");
        EmitTypedValue(sel.Condition);
        _sb.Append(", ");
        EmitTypedValue(sel.TrueValue);
        _sb.Append(", ");
        EmitTypedValue(sel.FalseValue);
    }

    private void EmitCall(CallInst call)
    {
        if (call.IsTailCall)
            _sb.Append("tail ");
        _sb.Append("call ");
        EmitType(call.CalleeType.ReturnType);
        _sb.Append(' ');
        EmitValueRef(call.Callee);
        _sb.Append('(');
        for (int i = 0; i < call.ArgumentCount; i++)
        {
            if (i > 0)
                _sb.Append(", ");
            EmitTypedValue(call.GetArgument(i));
        }
        if (call.CalleeType.IsVarArg)
        {
            if (call.ArgumentCount > 0)
                _sb.Append(", ");
            _sb.Append("...");
        }
        _sb.Append(')');
    }

    private void EmitExtractValue(ExtractValueInst ev)
    {
        _sb.Append("extractvalue ");
        EmitType(ev.Aggregate.Type);
        _sb.Append(' ');
        EmitValueRef(ev.Aggregate);
        foreach (var idx in ev.Indices)
            _sb.Append($", {idx}");
    }

    private void EmitInsertValue(InsertValueInst iv)
    {
        _sb.Append("insertvalue ");
        EmitType(iv.Aggregate.Type);
        _sb.Append(' ');
        EmitValueRef(iv.Aggregate);
        _sb.Append(", ");
        EmitTypedValue(iv.InsertedValue);
        foreach (var idx in iv.Indices)
            _sb.Append($", {idx}");
    }

    // ================================================================
    // 辅助方法
    // ================================================================

    private void EmitType(IrType t) => _sb.Append(t.ToIRString());

    private void EmitTypedValue(Value v)
    {
        EmitType(v.Type);
        _sb.Append(' ');
        EmitValueRef(v);
    }

    private void EmitValueRef(Value v)
    {
        switch (v)
        {
            case Constant c:
                EmitConstant(c);
                break;
            case GlobalVariable gv:
                _sb.Append($"@{gv.Name}");
                break;
            case Function f:
                _sb.Append($"@{f.Name}");
                break;
            default:
                if (_names.TryGetValue(v, out var nm))
                    _sb.Append($"%{nm}");
                else
                    _sb.Append($"%{v.Name ?? "?"}");
                break;
        }
    }

    private void EmitConstant(Constant c)
    {
        switch (c)
        {
            case ConstantInt ci:
                _sb.Append(ci.Value);
                break;
            case ConstantFP cf:
                EmitFP(cf);
                break;
            case ConstantPointerNull:
            case ConstantNull:
                _sb.Append("null");
                break;
            case UndefValue:
                _sb.Append("undef");
                break;
            case PoisonValue:
                _sb.Append("poison");
                break;
            case ConstantAggregateZero:
                _sb.Append("zeroinitializer");
                break;
            case ConstantArray ca:
                EmitConstArray(ca);
                break;
            case ConstantStruct cs:
                EmitConstStruct(cs);
                break;
            case ConstantDataArray cda:
                EmitConstDataArray(cda);
                break;
            default:
                throw new NotSupportedException($"不支持的常量: {c.GetType().Name}");
        }
    }

    private void EmitFP(ConstantFP cf)
    {
        if (double.IsNaN(cf.Value))
            _sb.Append("0x" + BitConverter.DoubleToInt64Bits(cf.Value).ToString("X16"));
        else if (double.IsPositiveInfinity(cf.Value))
            _sb.Append("0x7FF0000000000000");
        else if (double.IsNegativeInfinity(cf.Value))
            _sb.Append("0xFFF0000000000000");
        else
            _sb.Append(cf.Value.ToString("G", CultureInfo.InvariantCulture));
    }

    private void EmitConstArray(ConstantArray ca)
    {
        _sb.Append('[');
        for (int i = 0; i < ca.Values.Count; i++)
        {
            if (i > 0)
                _sb.Append(", ");
            EmitType(ca.ElementType);
            _sb.Append(' ');
            EmitConstant(ca.Values[i]);
        }
        _sb.Append(']');
    }

    private void EmitConstStruct(ConstantStruct cs)
    {
        var st = (StructType)cs.Type!;
        _sb.Append('{');
        for (int i = 0; i < cs.Values.Count; i++)
        {
            if (i > 0)
                _sb.Append(", ");
            EmitType(st.ElementTypes[i]);
            _sb.Append(' ');
            EmitConstant(cs.Values[i]);
        }
        _sb.Append('}');
    }

    /// <summary>发射常量数据数组：c"..." 格式（LLVM 22 规范要求 c 前缀）</summary>
    private void EmitConstDataArray(ConstantDataArray cda)
    {
        _sb.Append("c\"");
        foreach (var b in cda.Data)
        {
            if (b >= 0x20 && b < 0x7F && b != (byte)'\\' && b != (byte)'"')
                _sb.Append((char)b);
            else
                _sb.Append($"\\{b:X2}");
        }
        _sb.Append('"');
    }

    private void EmitStructBody(StructType st)
    {
        _sb.Append("{ ");
        for (int i = 0; i < st.ElementTypes.Count; i++)
        {
            if (i > 0)
                _sb.Append(", ");
            _sb.Append(st.ElementTypes[i].ToIRString());
        }
        _sb.Append(" }");
    }

    private static string EscStr(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string LinkageStr(LinkageType l) =>
        l switch
        {
            LinkageType.External => "external",
            LinkageType.Internal => "internal",
            LinkageType.Private => "private",
            LinkageType.Weak or LinkageType.WeakAny => "weak",
            LinkageType.Appending => "appending",
            LinkageType.Linkonce => "linkonce",
            LinkageType.LinkonceODR => "linkonce_odr",
            LinkageType.WeakODR => "weak_odr",
            LinkageType.Common => "common",
            LinkageType.AvailableExternally => "available_externally",
            LinkageType.ExternalWeak => "extern_weak",
            _ => throw new NotSupportedException(),
        };

    private static string VisStr(VisibilityStyle v) =>
        v switch
        {
            VisibilityStyle.Default => "default",
            VisibilityStyle.Hidden => "hidden",
            VisibilityStyle.Protected => "protected",
            _ => throw new NotSupportedException(),
        };

    private static string CallingConvStr(CallingConv c) =>
        c switch
        {
            CallingConv.C => "ccc",
            CallingConv.Fast => "fastcc",
            CallingConv.Cold => "coldcc",
            CallingConv.GHC => "ghccc",
            CallingConv.Tail => "tailcc",
            CallingConv.Swift => "swiftcc",
            _ => $"cc {(int)c}",
        };

    private static string BinOpStr(BinaryOp o) =>
        o switch
        {
            BinaryOp.Add => "add",
            BinaryOp.FAdd => "fadd",
            BinaryOp.Sub => "sub",
            BinaryOp.FSub => "fsub",
            BinaryOp.Mul => "mul",
            BinaryOp.FMul => "fmul",
            BinaryOp.UDiv => "udiv",
            BinaryOp.SDiv => "sdiv",
            BinaryOp.FDiv => "fdiv",
            BinaryOp.URem => "urem",
            BinaryOp.SRem => "srem",
            BinaryOp.FRem => "frem",
            BinaryOp.Shl => "shl",
            BinaryOp.LShr => "lshr",
            BinaryOp.AShr => "ashr",
            BinaryOp.And => "and",
            BinaryOp.Or => "or",
            BinaryOp.Xor => "xor",
            _ => throw new NotSupportedException(),
        };

    private static string ICmpStr(ICmpPredicate p) =>
        p switch
        {
            ICmpPredicate.Eq => "eq",
            ICmpPredicate.Ne => "ne",
            ICmpPredicate.Ugt => "ugt",
            ICmpPredicate.Uge => "uge",
            ICmpPredicate.Ult => "ult",
            ICmpPredicate.Ule => "ule",
            ICmpPredicate.Sgt => "sgt",
            ICmpPredicate.Sge => "sge",
            ICmpPredicate.Slt => "slt",
            ICmpPredicate.Sle => "sle",
            _ => throw new NotSupportedException(),
        };

    private static string FCmpStr(FCmpPredicate p) =>
        p switch
        {
            FCmpPredicate.False => "false",
            FCmpPredicate.True => "true",
            FCmpPredicate.Oeq => "oeq",
            FCmpPredicate.Ogt => "ogt",
            FCmpPredicate.Oge => "oge",
            FCmpPredicate.Olt => "olt",
            FCmpPredicate.Ole => "ole",
            FCmpPredicate.One => "one",
            FCmpPredicate.Ord => "ord",
            FCmpPredicate.Ueq => "ueq",
            FCmpPredicate.Ugt => "ugt",
            FCmpPredicate.Uge => "uge",
            FCmpPredicate.Ult => "ult",
            FCmpPredicate.Ule => "ule",
            FCmpPredicate.Une => "une",
            FCmpPredicate.Uno => "uno",
            _ => throw new NotSupportedException(),
        };

    private static string CastOpStr(CastOp o) =>
        o switch
        {
            CastOp.Trunc => "trunc",
            CastOp.ZExt => "zext",
            CastOp.SExt => "sext",
            CastOp.FPTrunc => "fptrunc",
            CastOp.FPExt => "fpext",
            CastOp.FPToUI => "fptoui",
            CastOp.FPToSI => "fptosi",
            CastOp.UIToFP => "uitofp",
            CastOp.SIToFP => "sitofp",
            CastOp.PtrToInt => "ptrtoint",
            CastOp.IntToPtr => "inttoptr",
            CastOp.BitCast => "bitcast",
            CastOp.AddrSpaceCast => "addrspacecast",
            _ => throw new NotSupportedException(),
        };

    private static string ThreadLocalStr(ThreadLocalMode m) =>
        m switch
        {
            ThreadLocalMode.None => "",
            ThreadLocalMode.LocalDynamic => "localdynamic",
            ThreadLocalMode.InitialExec => "initialexec",
            ThreadLocalMode.LocalExec => "localexec",
            _ => throw new NotSupportedException(),
        };
}
