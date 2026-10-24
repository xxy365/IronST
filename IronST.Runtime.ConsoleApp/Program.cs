// ═══════════════════════════════════════════════════════════════
//  Program.cs — IronST 交互式演示
//
//  用户输入 1 → 构造字节码并保存到 script.irst
//  用户输入 2 → 加载 script.irst 并执行 (调用 C# 宿主方法)
//  用户输入 3 → 直接执行 (不保存文件，内存中 Build → Run)
//  用户输入 0 → 退出
// ═══════════════════════════════════════════════════════════════
using IronST.Runtime;
using IronST.Runtime.Shared;

const string ScriptPath = "script.irst";

// ── HCALL ID 定义 ──────────────────────────────────────────
// 系统 HCALL（手册约定的标准 ID）
const ushort HC_PRINT_STR = 0x0001;
const ushort HC_PRINT_INT = 0x0002;
const ushort HC_PRINT_CHAR = 0x0003;
const ushort HC_READ_INT = 0x0004;
const ushort HC_READ_STR = 0x0005;

// 自定义 HCALL（避开系统 ID 范围 0x0001~0x003F）
const ushort HC_GET_SCORE = 0x0100;
const ushort HC_GET_LEVEL = 0x0101;
const ushort HC_FORMAT_RESULT = 0x0102;
const ushort HC_SAVE_DATA = 0x0103;
const ushort HC_PRINT_FIB = 0x0110;
const ushort HC_PRINT_FACT = 0x0111;

while (true) {
    Console.WriteLine();
    Console.WriteLine("═══════════════════════════════════════");
    Console.WriteLine("  IronST Interactive Demo");
    Console.WriteLine("═══════════════════════════════════════");
    Console.WriteLine("  1 — 构造字节码并保存到文件");
    Console.WriteLine("  2 — 从文件加载并执行");
    Console.WriteLine("  3 — 直接执行 (内存)");
    Console.WriteLine("  0 — 退出");
    Console.WriteLine("═══════════════════════════════════════");
    Console.Write("  选择: ");

    string? input = Console.ReadLine()?.Trim();

    switch (input) {
        case "1":
            CompileScript();
            break;
        case "2":
            ExecuteFromFile();
            break;
        case "3":
            ExecuteDirect();
            break;
        case "0":
            Console.WriteLine("  Bye.");
            return;
        default:
            Console.WriteLine("  无效输入");
            break;
    }
}

// ══════════════════════════════════════════════════════════════
//  功能 1: 构造字节码 → 保存到 .irst 文件
// ══════════════════════════════════════════════════════════════

void CompileScript()
{
    Console.WriteLine("\n  [编译] 构造字节码...\n");

    var asm = new BytecodeAssembler();

    // ── 声明全局变量 ─────────────────────────────────────
    asm.DeclareGlobal("user_name");
    asm.DeclareGlobal("score");
    asm.DeclareGlobal("level");
    asm.DeclareGlobal("result");

    // ═══ 主程序 ══════════════════════════════════════════

    // ① 读取用户输入 (HCALL READ_STR，新架构统一走 HCALL)
    asm.Print("  请输入你的名字: ");
    asm.Raw.HCALL(HC_READ_STR);             // A0 = 用户输入
    asm.SetGlobal(BytecodeBuilder.A0, "user_name");

    // ② 调用宿主方法: get_score(name) → score
    byte nameReg = asm.AllocTemp();
    asm.GetGlobal(nameReg, "user_name");
    asm.Raw.MOV(BytecodeBuilder.A0, nameReg);  // A0 = 用户名
    asm.FreeTemp(nameReg);
    asm.Raw.HCALL(HC_GET_SCORE);               // get_score → A0 = score
    asm.SetGlobal(BytecodeBuilder.A0, "score");

    // ③ 调用宿主方法: get_level(score) → level
    asm.Raw.HCALL(HC_GET_LEVEL);               // A0 已是 score → A0 = level
    asm.SetGlobal(BytecodeBuilder.A0, "level");

    // ④ 计算: result = score * level (IronST 内部计算)
    byte sc = asm.AllocTemp();
    byte lv = asm.AllocTemp();
    asm.GetGlobal(sc, "score");
    asm.GetGlobal(lv, "level");
    byte product = asm.IMul(sc, lv);            // product = score * level
    asm.SetGlobal(product, "result");
    asm.FreeTemp(sc);
    asm.FreeTemp(lv);
    asm.FreeTemp(product);

    // ⑤ 调用宿主方法: format_result(name, score, level, result)
    nameReg = asm.AllocTemp();
    asm.GetGlobal(nameReg, "user_name");
    asm.Raw.MOV(BytecodeBuilder.A0, nameReg);   // A0 = name

    sc = asm.AllocTemp();
    asm.GetGlobal(sc, "score");
    asm.Raw.MOV(BytecodeBuilder.A1, sc);         // A1 = score

    lv = asm.AllocTemp();
    asm.GetGlobal(lv, "level");
    asm.Raw.MOV(BytecodeBuilder.A2, lv);         // A2 = level

    asm.Raw.HCALL(HC_FORMAT_RESULT);              // format_result → A0 = 格式化字符串
    asm.PrintStr(BytecodeBuilder.A0);             // 打印结果

    asm.FreeTemp(nameReg);
    asm.FreeTemp(sc);
    asm.FreeTemp(lv);

    // ⑥ 调用宿主方法: save_data(name, result)
    nameReg = asm.AllocTemp();
    asm.GetGlobal(nameReg, "user_name");
    asm.Raw.MOV(BytecodeBuilder.A0, nameReg);

    byte res = asm.AllocTemp();
    asm.GetGlobal(res, "result");
    asm.Raw.MOV(BytecodeBuilder.A1, res);

    asm.Raw.HCALL(HC_SAVE_DATA);                  // save_data
    asm.FreeTemp(nameReg);
    asm.FreeTemp(res);

    // ⑦ 退出
    asm.Exit();

    // ── 保存到文件 ──────────────────────────────────────
    asm.SaveTo(ScriptPath);

    Console.WriteLine($"  [编译] 完成! 已保存到 {ScriptPath}");
    Console.WriteLine($"  [编译] 文件大小: {new FileInfo(ScriptPath).Length} bytes");
}

// ══════════════════════════════════════════════════════════════
//  功能 2: 从 .irst 文件加载并执行
// ══════════════════════════════════════════════════════════════

void ExecuteFromFile()
{
    if (!File.Exists(ScriptPath)) {
        Console.WriteLine($"\n  [错误] 文件 {ScriptPath} 不存在，请先选择 1 编译。\n");
        return;
    }

    Console.WriteLine($"\n  [执行] 从 {ScriptPath} 加载...\n");

    byte[] bytecode = File.ReadAllBytes(ScriptPath);
    var module = BytecodeModule.Load(bytecode);

    RunModule(module);
}

// ══════════════════════════════════════════════════════════════
//  功能 3: 直接在内存中构造并执行 (不保存文件)
//  使用 BytecodeBuilder 底层 API 展示手动字节码构造
// ══════════════════════════════════════════════════════════════

void ExecuteDirect()
{
    Console.WriteLine("\n  [直接执行] 构造并运行...\n");

    var b = new BytecodeBuilder();

    // ── 主程序 ──────────────────────────────────────────

    // 计算 fibonacci(10)
    b.LDC(BytecodeBuilder.A0, b.AddConst(10L));    // 常量 10 → 常量池，LDC 加载
    b.CALL(0);                                      // 占位，patch 到 fib 函数
    int callOffset = b.PC - 5;

    // 打印 fibonacci 结果 (调用 C# 宿主方法)
    b.HCALL(HC_PRINT_FIB);

    // 计算 factorial(8) 并打印
    b.LDC(BytecodeBuilder.A0, b.AddConst(8L));     // 常量 8 → 常量池
    b.CALL(0);                                      // 占位，patch 到 factorial
    int callOffset2 = b.PC - 5;
    b.HCALL(HC_PRINT_FACT);

    b.SYSCALL(0x00);                                // EXIT

    // ── fibonacci(n) ─────────────────────────────────────
    // FRAME(3): local[0]=n, local[1]=LR, local[2]=fib(n-1)
    int fibStart = b.PC;
    b.FRAME(3);
    b.SETL(BytecodeBuilder.A0, 0);                 // local[0] = n

    // if n <= 1: return n
    b.GETL(10, 0);
    b.LDC(11, b.AddConst(1L));                     // 替代原 LDI8(11, 1)
    b.BGT(10, 11, 4);                              // n > 1 → 跳到递归
                                                   // 跳过 GETL [4字节] 到 LEAVE
    b.GETL(BytecodeBuilder.A0, 0);
    b.LEAVE();
    b.RET();

    // 递归: fib(n) = fib(n-1) + fib(n-2)
    b.SETL(BytecodeBuilder.LR, 1);                 // 保存 LR

    // fib(n-1)
    b.GETL(10, 0);
    b.IDEC(10);
    b.MOV(BytecodeBuilder.A0, 10);
    b.CALL((uint)fibStart);                         // 递归调用
    b.SETL(BytecodeBuilder.A0, 2);                 // local[2] = fib(n-1)

    // fib(n-2)
    b.GETL(10, 0);
    b.LDC(11, b.AddConst(2L));                     // 替代原 LDI8(11, 2)
    b.ISUB(10, 10, 11);
    b.MOV(BytecodeBuilder.A0, 10);
    b.CALL((uint)fibStart);                         // 递归调用

    // result = fib(n-1) + fib(n-2)
    b.GETL(10, 2);
    b.IADD(BytecodeBuilder.A0, 10, BytecodeBuilder.A0);

    b.GETL(BytecodeBuilder.LR, 1);                 // 恢复 LR
    b.LEAVE();
    b.RET();

    // ── factorial(n) ─────────────────────────────────────
    // FRAME(2): local[0]=n, local[1]=LR
    int factStart = b.PC;
    b.FRAME(2);
    b.SETL(BytecodeBuilder.A0, 0);                 // local[0] = n

    // if n <= 1: return 1
    b.GETL(10, 0);
    b.LDC(11, b.AddConst(1L));                     // 替代原 LDI8(11, 1)
    b.BGT(10, 11, 6);                              // n > 1 → 跳到递归
                                                   // 跳过 LDC [6字节] 到 LEAVE（LDC 比原 LDI8 多 3 字节，偏移从 4→6）
    b.LDC(BytecodeBuilder.A0, b.AddConst(1L));     // base case: return 1
    b.LEAVE();
    b.RET();

    // 递归: fact(n) = n * fact(n-1)
    b.SETL(BytecodeBuilder.LR, 1);

    b.GETL(10, 0);
    b.IDEC(10);
    b.MOV(BytecodeBuilder.A0, 10);
    b.CALL((uint)factStart);

    b.GETL(BytecodeBuilder.LR, 1);

    b.MOV(10, BytecodeBuilder.A0);
    b.GETL(11, 0);
    b.IMUL(BytecodeBuilder.A0, 11, 10);
    b.LEAVE();
    b.RET();

    // ── Patch CALL 地址 ──────────────────────────────────
    b.PatchUInt32(callOffset + 1, (uint)fibStart);
    b.PatchUInt32(callOffset2 + 1, (uint)factStart);

    // ── 直接运行 (不经过序列化) ──────────────────────────
    var module = b.Build();
    RunModule(module);
}

// ══════════════════════════════════════════════════════════════
//  共用: 创建 HostBridge + VM 并运行
// ══════════════════════════════════════════════════════════════

void RunModule(BytecodeModule module)
{
    // ── 注册宿主方法 ─────────────────────────────────────
    var bridge = new HostBridge();

    // ── 系统 I/O 宿主方法（原代码通过 SYSCALL 做 I/O，
    //    新架构统一走 HCALL，需在 HostBridge 注册）─────────

    bridge.Register(HC_PRINT_STR, ctx => {
        Console.Write(ctx.ReadReg(4).StringValue ?? "");
        return BcValue.Void;
    });

    bridge.Register(HC_PRINT_INT, ctx => {
        Console.Write(ctx.ReadReg(4).IntValue);
        return BcValue.Void;
    });

    bridge.Register(HC_PRINT_CHAR, ctx => {
        Console.Write((char)ctx.ReadReg(4).IntValue);
        return BcValue.Void;
    });

    bridge.Register(HC_READ_INT, ctx => {
        if (long.TryParse(Console.ReadLine(), out long val))
            return new BcValue(val);
        return BcValue.Void;
    });

    bridge.Register(HC_READ_STR, ctx => {
        return new BcValue(Console.ReadLine() ?? "");
    });

    // ── 自定义宿主方法 ──────────────────────────────────

    // HC_GET_SCORE: get_score(name) → 根据名字计算一个分数
    bridge.Register(HC_GET_SCORE, ctx => {
        string name = ctx.ReadReg(4).StringValue ?? "unknown";
        long score = 0;
        foreach (char c in name)
            score += c;
        score = score % 1000;
        Console.WriteLine($"  [C#] get_score(\"{name}\") = {score}");
        return new BcValue(score);
    });

    // HC_GET_LEVEL: get_level(score) → 根据分数算等级
    bridge.Register(HC_GET_LEVEL, ctx => {
        long score = ctx.ReadReg(4).IntValue;
        long level = score switch
        {
            >= 800 => 5,
            >= 600 => 4,
            >= 400 => 3,
            >= 200 => 2,
            _ => 1
        };
        Console.WriteLine($"  [C#] get_level({score}) = {level}");
        return new BcValue(level);
    });

    // HC_FORMAT_RESULT: format_result(name, score, level) → 格式化字符串
    bridge.Register(HC_FORMAT_RESULT, ctx => {
        string name = ctx.ReadReg(4).StringValue ?? "?";
        long score = ctx.ReadReg(5).IntValue;
        long level = ctx.ReadReg(6).IntValue;
        string msg = $"  [C#] 玩家 {name}: 分数={score}, 等级={level}";
        return new BcValue(msg);
    });

    // HC_SAVE_DATA: save_data(name, result) → 模拟保存数据
    bridge.Register(HC_SAVE_DATA, ctx => {
        string name = ctx.ReadReg(4).StringValue ?? "?";
        long result = ctx.ReadReg(5).IntValue;
        Console.WriteLine($"  [C#] save_data(\"{name}\", {result}) → 已保存到数据库");
        return BcValue.Void;
    });

    // HC_PRINT_FIB: 打印 fibonacci 结果 (直接执行模式)
    bridge.Register(HC_PRINT_FIB, ctx => {
        long fib = ctx.ReadReg(4).IntValue;
        Console.WriteLine($"  [C#] fibonacci(10) = {fib}");
        return BcValue.Void;
    });

    // HC_PRINT_FACT: 打印 factorial 结果 (直接执行模式)
    bridge.Register(HC_PRINT_FACT, ctx => {
        long fact = ctx.ReadReg(4).IntValue;
        Console.WriteLine($"  [C#] factorial(8) = {fact}");
        return BcValue.Void;
    });

    // ── 创建 VM 并运行 ──────────────────────────────────
    var vm = new VM(module, new VMOptions
    {
        HostBridge = bridge,
        StackSize = 4096
    });

    Console.WriteLine("  ─────────────────────────────────");
    vm.Run();
    Console.WriteLine("  ─────────────────────────────────");
    Console.WriteLine($"\n  [完成] VM 状态: {vm.State}");
}