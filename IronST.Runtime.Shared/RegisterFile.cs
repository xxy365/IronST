// ═══════════════════════════════════════════════════════════════
//  RegisterFile.cs — 256 寄存器，R[0] (ZERO) 硬连线为 Void
// ═══════════════════════════════════════════════════════════════
using System.Runtime.CompilerServices;

namespace IronST.Runtime.Shared;

public sealed class RegisterFile
{
    private readonly BcValue[] _r;

    public RegisterFile(int stackSize)
    {
        _r = new BcValue[256];
        _r[1] = new BcValue((long)stackSize);
        _r[2] = new BcValue((long)stackSize);
    }

    public BcValue this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => index == 0 ? BcValue.Void : _r[index];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set { if (index != 0) _r[index] = value; }
    }

    public ref long SP
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _r[1].IntValue;
    }

    public ref long FP
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _r[2].IntValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncInt(int index) { if (index != 0) _r[index].IntValue++; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DecInt(int index) { if (index != 0) _r[index].IntValue--; }
}
