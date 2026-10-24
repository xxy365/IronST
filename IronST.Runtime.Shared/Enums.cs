// ═══════════════════════════════════════════════════════════════
//  Enums.cs — 所有枚举定义
// ═══════════════════════════════════════════════════════════════

namespace IronST.Runtime.Shared;

public enum ValueTag : byte
{
    Void = 0,
    Int64 = 1,
    Float64 = 2,
    String = 3,
    Struct = 5,
}

public enum VMState
{
    Init,
    Running,
    Paused,
    Halted,
}

public enum RuntimeErrorType
{
    TypeError,
    DivideByZero,
    SlotOverflow,
    StackOverflow,
    StackUnderflow,
    IllegalOpcode,
    ArrayBounds,

    /// <summary>
    /// 指令预算耗尽（<see cref="VMOptions.MaxInstructions"/>）。
    /// 与取消的区别：取消是外部主动叫停（退出码 130），预算是程序跑得比允许的更多，
    /// 属于运行期错误（退出码 1），必须把错误类型和上限告诉使用者。
    /// </summary>
    InstructionBudgetExceeded,

    /// <summary>
    /// 空引用解引用（<c>REFERENCE</c> 未绑定或为 NULL）。
    /// 由 <see cref="OpCode.NULLCK"/> 在解引用点抛出，PC / 源位置由 VM 错误边界回填。
    /// </summary>
    NullReference,
}

/// <summary>IronST Bytecode ISA — 完整操作码 (ISA §6)</summary>
public enum OpCode : byte
{
    // ── 系统控制 0x00–0x03 ──────────────────────────────────
    NOP = 0x00,
    HALT = 0x01,
    BRK = 0x02,
    SYSCALL = 0x03,

    // ── 常量加载 0x10–0x19 ──────────────────────────────────
    LNIL = 0x10,
    LTRUE = 0x11,
    LFALSE = 0x12,
    LDC = 0x14,
    MOV = 0x18,
    SWAP = 0x19,

    // ── 栈操作 0x20–0x25 ───────────────────────────────────
    PUSH = 0x20,
    POP = 0x21,
    PUSHN = 0x22,
    POPN = 0x23,
    FRAME = 0x24,
    LEAVE = 0x25,

    // ── 变量访问 0x30–0x37 ─────────────────────────────────
    GETL = 0x30,
    SETL = 0x31,
    GETG = 0x32,
    SETG = 0x33,
    GETR = 0x34,
    SETR = 0x35,
    GETGI = 0x36,
    SETGI = 0x37,

    // ── 算术 0x50–0x5C ─────────────────────────────────────
    IADD = 0x50,
    ISUB = 0x51,
    IMUL = 0x52,
    IDIV = 0x53,
    FADD = 0x55,
    FSUB = 0x56,
    FMUL = 0x57,
    FDIV = 0x58,
    INEG = 0x59,
    FNEG = 0x5A,
    IINC = 0x5B,
    IDEC = 0x5C,

    // ── 位运算 0x60–0x6E ──────────────────────────────────
    AND = 0x60,
    OR = 0x61,
    XOR = 0x62,
    SHL = 0x63,
    SHR = 0x64,
    SAR = 0x65,
    NOT = 0x66,
    ROL = 0x67,
    ROR = 0x68,
    ANDI = 0x69,
    ORI = 0x6A,
    XORI = 0x6B,
    SHLI = 0x6C,
    SHRI = 0x6D,
    CLZ = 0x6E,

    // ── 比较 0x70–0x7F ────────────────────────────────────
    CMP = 0x70,
    IEQ = 0x76,
    INE = 0x77,
    FEQ = 0x7C,
    TYPE = 0x7E,
    ISTYPE = 0x7F,

    // ── 控制流 0x80–0x8E ──────────────────────────────────
    RET = 0x80,
    JMPR = 0x81,
    CALLR = 0x82,
    JMP = 0x85,
    CALL = 0x86,
    BZ = 0x87,
    BNZ = 0x88,
    BEQ = 0x89,
    BNE = 0x8A,
    BLT = 0x8B,
    BGE = 0x8C,
    BGT = 0x8D,
    BLE = 0x8E,

    // ── 结构体 0x90–0x92 ──────────────────────────────────
    SNEW = 0x90, // Struct New
    SGET = 0x91, // Struct Get
    SSET = 0x92, // Struct Set

    // ── 数组边界 0x94 ─────────────────────────────────────
    BOUNDS = 0x94, // Bounds check: 0 <= R[idx] < R[max]，否则 ArrayBounds 异常

    // ── 引用判空 0x95 ─────────────────────────────────────
    // 引用在字节码里是「槽号」，槽 0 也是合法槽，所以不能把数值 0 当 NULL ——
    // NULLCK 按 BcValue.Tag 判定：未绑定/失效的引用是 Void（零初始化），合法指针是 Int64。
    NULLCK = 0x95, // Null reference check: R[r] 为 Void 时抛 NullReference

    // ── 类型转换 0xA0–0xA3 ────────────────────────────────
    TOINT = 0xA0,
    TOFLT = 0xA1,
    TOSTR = 0xA2,
    TOBOOL = 0xA3,

    // ── 宿主互操作 0xB0 ──────────────────────────────────
    HCALL = 0xB0,

    // ── VM 互操作 0xC0–0xC1 ───────────────────────────────
    VMCALL = 0xC0,
    VMRET = 0xC1,
}

/// <summary>
/// SYSCALL 内建功能号。
/// 参数通过 R[4]–R[7] 传递，返回值存入 R[4]。
/// </summary>
public enum SyscallFunc : byte
{
    /// <summary>停机。</summary>
    EXIT = 0x00,

    /// <summary>异常中止 (R[4]=code)。抛出 RuntimeError。</summary>
    ABORT = 0x10,
}

/// <summary>
/// HCALL 宿主方法 ID 常量。
/// 参数通过 R[4]–R[7] 传递，返回值存入 R[4]。
/// </summary>
public static class HostCallIds
{
    // ── I/O ─────────────────────────────────────────────────
    public const ushort PRINT_STR = 0x0001;
    public const ushort PRINT_INT = 0x0002;
    public const ushort PRINT_CHAR = 0x0003;
    public const ushort READ_INT = 0x0004;
    public const ushort READ_STR = 0x0005;

    // ── 时间 ────────────────────────────────────────────────
    public const ushort TIME = 0x000B;
    public const ushort SLEEP = 0x000C;
    public const ushort CLOCK = 0x000D;

    // ── 工具 ────────────────────────────────────────────────
    public const ushort RAND = 0x000E;
    public const ushort MEMSIZE = 0x000F;

    // ── 字符串 ──────────────────────────────────────────────
    //  注入映射（见 StringStandardLibrary.Functions）：
    //  LEN→STR_LEN, CONCAT→STR_CONCAT, FIND→STR_FIND, REPLACE→STR_REPLACE,
    //  MID→STR_SUBSTR（同义：起点 + 长度取子串），
    //  LEFT/RIGHT/INSERT/DELETE 为本轮新增。
    public const ushort STR_CONCAT = 0x0020;
    public const ushort STR_LEN = 0x0021;
    public const ushort STR_SUBSTR = 0x0022;
    public const ushort STR_FIND = 0x0023;
    public const ushort STR_REPLACE = 0x0024;
    public const ushort STR_LEFT = 0x0025;
    public const ushort STR_RIGHT = 0x0026;
    public const ushort STR_INSERT = 0x0027;
    public const ushort STR_DELETE = 0x0028;

    // ── 数学 ────────────────────────────────────────────────
    public const ushort MATH_FMOD = 0x0030;
    public const ushort MATH_IMOD = 0x0031;
    public const ushort MATH_FPOW = 0x0032;
    public const ushort MATH_SQRT = 0x0033;
    public const ushort MATH_SIN = 0x0034;
    public const ushort MATH_COS = 0x0035;
    public const ushort MATH_TAN = 0x0036;
    public const ushort MATH_LOG = 0x0037;
    public const ushort MATH_ABS = 0x0038;

    // 无符号整数余数。不能复用 MATH_IMOD：同一个 ID 无法区分
    // LLVM srem（有符号）与 urem（无符号）的语义。
    public const ushort MATH_UMOD = 0x0039;
}