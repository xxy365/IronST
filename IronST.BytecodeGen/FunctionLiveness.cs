using IronST.IRBuilder.IR;

namespace IronST.BytecodeGen;

/// <summary>
/// 函数级 SSA liveness，供调用点 spill 使用。
///
/// <para>
/// 只关心**指令结果**：参数 / 常量 / alloca 基址可以重新物化，不需要 spill。
/// 对每个会 clobber <c>R8+</c> 的指令（普通 CALL、宿主 HCALL、数学 HCALL），
/// 记录该点之后仍然活跃的指令结果。后端据此为这些值分配帧槽，并在定义处
/// 写回槽（而不是在调用点临时 PUSH/POP）。
/// </para>
/// </summary>
internal static class FunctionLiveness
{
    /// <summary>
    /// 返回 <c>clobber 指令 -> live-after 集合</c>。
    /// </summary>
    public static Dictionary<Instruction, HashSet<Value>> ComputeLiveAcrossClobbers(Function function)
    {
        var result = new Dictionary<Instruction, HashSet<Value>>();
        var blocks = function.BasicBlocks;
        if (blocks.Count == 0)
            return result;

        var defs = new Dictionary<BasicBlock, HashSet<Value>>();
        var uses = new Dictionary<BasicBlock, HashSet<Value>>();

        foreach (var block in blocks)
        {
            var defined = new HashSet<Value>();
            var used = new HashSet<Value>();

            foreach (var inst in block.Instructions)
            {
                // PHI 的 use 在逻辑上属于前驱边；这里沿用标准的
                // “块级 use/def + PHI 操作数作为 block use”近似。
                // 它可能多算一条边，但不会漏算，spill 的保守方向是安全的。
                foreach (var operand in ValueUses(inst))
                {
                    if (!defined.Contains(operand))
                        used.Add(operand);
                }

                if (inst.ProducesResult)
                    defined.Add(inst);
            }

            defs[block] = defined;
            uses[block] = used;
        }

        var successors = blocks.ToDictionary(
            b => b,
            b => (IReadOnlyList<BasicBlock>)Successors(b).ToList()
        );

        var liveIn = blocks.ToDictionary(b => b, _ => new HashSet<Value>());
        var liveOut = blocks.ToDictionary(b => b, _ => new HashSet<Value>());

        bool changed;
        do
        {
            changed = false;
            // 逆序只是让收敛更快，语义与顺序无关。
            for (int bi = blocks.Count - 1; bi >= 0; bi--)
            {
                var block = blocks[bi];
                var outSet = new HashSet<Value>();

                foreach (var succ in successors[block])
                {
                    if (liveIn.TryGetValue(succ, out var succIn))
                        outSet.UnionWith(succIn);
                }

                var inSet = new HashSet<Value>(uses[block]);
                foreach (var value in outSet)
                {
                    if (!defs[block].Contains(value))
                        inSet.Add(value);
                }

                if (!liveOut[block].SetEquals(outSet) || !liveIn[block].SetEquals(inSet))
                {
                    liveOut[block] = outSet;
                    liveIn[block] = inSet;
                    changed = true;
                }
            }
        } while (changed);

        // 反向扫描每个块，在 clobber 指令处截取 live-after。
        foreach (var block in blocks)
        {
            var live = new HashSet<Value>(liveOut[block]);

            for (int i = block.Instructions.Count - 1; i >= 0; i--)
            {
                var inst = block.Instructions[i];

                if (IsClobberPoint(inst))
                    result[inst] = new HashSet<Value>(live);

                if (inst.ProducesResult)
                    live.Remove(inst);

                foreach (var operand in ValueUses(inst))
                    live.Add(operand);
            }
        }

        return result;
    }

    /// <summary>
    /// 会破坏 caller-saved <c>R8+</c> 的指令。
    /// </summary>
    private static bool IsClobberPoint(Instruction inst) =>
        inst is CallInst
        || (inst is BinaryOperator bin
            && bin.Op is BinaryOp.SRem or BinaryOp.URem);

    /// <summary>
    /// 指令值操作数；过滤掉 PHI 的 BasicBlock、CALL 的 Function 等非值项。
    /// </summary>
    private static IEnumerable<Value> ValueUses(Instruction inst)
    {
        if (inst is PHINode phi)
        {
            foreach (var (value, _) in phi.Incoming)
            {
                if (value is Instruction)
                    yield return value;
            }
            yield break;
        }

        foreach (var operand in inst.Operands)
        {
            if (operand is Instruction)
                yield return operand;
        }
    }

    private static IEnumerable<BasicBlock> Successors(BasicBlock block)
    {
        switch (block.Terminator)
        {
            case BranchInst br:
                yield return br.TrueDest;
                if (br.IsConditional && br.FalseDest is not null)
                    yield return br.FalseDest;
                break;

            case SwitchInst sw:
                yield return sw.DefaultDest;
                foreach (var (_, dest) in sw.Cases)
                    yield return dest;
                break;
        }
    }
}
