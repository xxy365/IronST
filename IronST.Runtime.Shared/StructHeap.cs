// ═══════════════════════════════════════════════════════════════
//  StructHeap.cs — 结构体 Arena 分配器
// ═══════════════════════════════════════════════════════════════
//
//  ◆ 设计:
//    所有结构体字段存放在一个连续的 BcValue[] Arena 中。
//    BcValue(Struct) 的 IntValue = slotIndex (Arena 偏移)。
//    BcValue(Struct) 的 SubTag = typeId。
//    BcValue(Struct) 的 RefValue = null (零 GC 压力)。
//    bump allocator，不支持单个实例释放。
//
//  ◆ 内存布局:
//    Arena 是一个平坦的 BcValue[] 数组。
//    每个结构体实例占用 fieldCount 个连续槽位。
//    slotIndex 指向该实例在 Arena 中的起始位置。
//    field N 存放在 Arena[slotIndex + N]。
//
//  ◆ 示例:
//    RegisterType(1, 3);           // typeId=1, 3 个字段
//    int slot = Alloc(1);          // slot=0
//    SetField(slot, 0, new BcValue(42));  // field 0 = 42
//    SetField(slot, 1, new BcValue(3.14)); // field 1 = 3.14
//    GetField(slot, 0);            // → BcValue(42)
//
// ═══════════════════════════════════════════════════════════════
using System.Runtime.CompilerServices;

namespace IronST.Runtime.Shared;

public sealed class StructHeap
{
    private BcValue[] _arena;
    private readonly Dictionary<byte, int> _typeFieldCounts = new();
    private int _nextSlot;

    public StructHeap(int capacity)
    {
        _arena = new BcValue[capacity];
    }

    /// <summary>
    /// 注册结构体类型。
    /// </summary>
    /// <param name="typeId">类型 ID</param>
    /// <param name="fieldCount">字段数量 (VAR + VAR_INPUT + VAR_OUTPUT + VAR_TEMP)</param>
    public void RegisterType(byte typeId, int fieldCount)
    {
        _typeFieldCounts[typeId] = fieldCount;
    }

    /// <summary>
    /// 分配一个结构体实例，返回 Arena 中的起始 slotIndex。
    /// 所有字段初始化为 default(BcValue) (即 Void)。
    /// </summary>
    /// <param name="typeId">已注册的类型 ID</param>
    /// <returns>Arena 中的起始偏移</returns>
    public int Alloc(byte typeId)
    {
        if (!_typeFieldCounts.TryGetValue(typeId, out int fieldCount))
            throw new RuntimeErrorException(
                RuntimeErrorType.TypeError,
                0,
                $"StructHeap: unregistered typeId {typeId}"
            );

        int slot = _nextSlot;
        _nextSlot += fieldCount;

        if (_nextSlot > _arena.Length)
            throw new RuntimeErrorException(
                RuntimeErrorType.SlotOverflow,
                0,
                $"StructHeap: out of capacity (need {_nextSlot}, have {_arena.Length})"
            );

        return slot;
    }

    /// <summary>
    /// 读取结构体字段。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BcValue GetField(int slotIndex, byte fieldOffset)
    {
        int addr = slotIndex + fieldOffset;
        if ((uint)addr >= (uint)_arena.Length)
            throw new RuntimeErrorException(
                RuntimeErrorType.SlotOverflow,
                0,
                $"StructHeap.GetField: index {addr} out of range {_arena.Length}"
            );
        return _arena[addr];
    }

    /// <summary>
    /// 写入结构体字段。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetField(int slotIndex, byte fieldOffset, BcValue value)
    {
        int addr = slotIndex + fieldOffset;
        if ((uint)addr >= (uint)_arena.Length)
            throw new RuntimeErrorException(
                RuntimeErrorType.SlotOverflow,
                0,
                $"StructHeap.SetField: index {addr} out of range {_arena.Length}"
            );
        _arena[addr] = value;
    }

    /// <summary>
    /// 深拷贝结构体实例。
    /// </summary>
    public void DeepCopy(int srcSlot, int dstSlot, int fieldCount)
    {
        Array.Copy(_arena, srcSlot, _arena, dstSlot, fieldCount);
    }

    /// <summary>
    /// 热重载清空，重置 bump 指针和 Arena 内容。
    /// </summary>
    public void Reset()
    {
        Array.Clear(_arena, 0, _nextSlot);
        _nextSlot = 0;
    }
}