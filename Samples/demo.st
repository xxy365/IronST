// ═══════════════════════════════════════════════════════════
//  IronST.IDE 示例
//
//  在 IDE 里怎么玩：
//    文件 → 打开     加载本文件，约 300ms 后底部"错误"列表自动填充
//    点左边栏         行号左侧空白区点一下 = 设/删断点（红点）
//    F9              在光标行切换断点（和点边栏等价）
//    F5              启动调试，会带着你先前设的断点跑
//    F10 / F11       单步执行 / 进入
//    F5（暂停时）     继续运行
//    Shift+F5        停止调试
//
//  断点可以先设再调试 —— 不需要先 F5。
//  暂停时当前行会有整行高亮，输出面板里能看到 Print(...) 的结果。
//
//  本例刻意避开四条当前有缺陷的路径，见文末"已知问题"：
//    ① 功能块调用（带 VAR_OUTPUT）  ② CASE ... OF 区间分支
//    ③ 结构体作函数形参             ④ 字符串字面量
// ═══════════════════════════════════════════════════════════

// ── 自定义类型 ①：枚举 ────────────────────────────────
TYPE
    Level : (Low, Medium, High);
END_TYPE;

// ── 自定义类型 ②：结构体（只做字段读写，见已知问题 ③）──
TYPE
    Point :
    STRUCT
        X : REAL;
        Y : REAL;
    END_STRUCT
;
END_TYPE;

// ── 函数：标量形参，无状态，靠返回值传出结果 ─────────
FUNCTION Dist : REAL
VAR_INPUT
    dx : REAL;
    dy : REAL;
END_VAR
    Dist := dx * dx + dy * dy;
END_FUNCTION

// ── 主程序 ──────────────────────────────────────────────
PROGRAM Main
VAR
    // 1. 启动/停止互锁
    startBtn    : BOOL;
    stopBtn     : BOOL;
    running     : BOOL;

    // 2. 循环累加
    i           : INT;
    sum         : INT;

    // 3. 温度告警分级
    temperature : INT;
    level       : Level;

    // 4. 结构体字段读写
    p2          : Point;
    gotX        : REAL;
    gotY        : REAL;

    // 5. 标量函数调用
    distance    : REAL;
    isFar       : BOOL;
END_VAR

    // ── 1. 停止优先：两按钮同时按下时停机 ──
    IF stopBtn THEN
        running := FALSE;
    ELSIF startBtn THEN
        running := TRUE;
    END_IF

    // ── 2. FOR 循环 → sum = 1+2+3+4+5 = 15 ──
    sum := 0;
    FOR i := 1 TO 5 DO
        sum := sum + i;
    END_FOR
    Print(sum);

    // ── 3. WHILE 循环 → i 最终为 3 ──
    i := 0;
    WHILE i < 3 DO
        i := i + 1;
    END_WHILE
    Print(i);

    // ── 4. 温度分级（用 IF/ELSIF 实现，⚠ 见已知问题 ②）──
    temperature := 75;
    IF temperature <= 50 THEN
        level := Level.Low;
    ELSIF temperature <= 80 THEN
        level := Level.Medium;
    ELSE
        level := Level.High;
    END_IF
    Print(temperature);

    // ── 5. 结构体字段读写：期望依次打印 3 和 4 ──
    p2.X := 3.0;
    p2.Y := 4.0;
    gotX := p2.X;
    gotY := p2.Y;
    Print(gotX);
    Print(gotY);

    // ── 6. 标量函数：Dist(3.0, 4.0) = 9 + 16 = 25 ──
    distance := Dist(3.0, 4.0);
    isFar := distance > 20.0;
    Print(distance);
    Print(isFar);

    Print(0);
END_PROGRAM


// ═══════════════════════════════════════════════════════════
//  关于 Print
//
//  Print 是 IDE 注入的宿主函数（不是 IEC 标准），前 4 个实参走
//  寄存器 A0–A3，输出面板里一行一个。
//
//  预期输出（依次）：
//    15      ← sum
//    3       ← i
//    75      ← temperature
//    3       ← gotX
//    4       ← gotY
//    25      ← distance
//    1       ← isFar（TRUE 打成 1）
//    0       ← 结束标记
//
//  限制：
//    · REAL 用 G 格式，整数值不带小数点，25.0 显示成 25
//    · 字符串字面量打出来是 nil（见已知问题 ④），所以本例不打标签
//    · ABS / SQRT / SIN 等 IEC 标准库函数不可用（见已知问题 ⑤）
//
//
//  ────────────────────────────────────────────────────────
//  已知问题 ①：带 VAR_OUTPUT 的功能块被调用
//
//  下面这段前端语义分析能通过，但后端会抛
//  ArgumentNullException（FunctionEmitter.EnsureReg 的 key 为 null）：
//
//    FUNCTION_BLOCK Counter
//    VAR_INPUT  inc : INT; END_VAR
//    VAR_OUTPUT count : INT; END_VAR
//        count := count + inc;
//    END_FUNCTION_BLOCK
//
//    PROGRAM Main
//    VAR ctr : Counter; END_VAR
//        ctr(inc := 1);        // ← 崩在这里
//    END_PROGRAM
//
//  实测边界：无 VAR_OUTPUT 的功能块调用正常；有 VAR_OUTPUT
//  但不被调用也正常。只有"有 VAR_OUTPUT 且被调用"这一组合崩溃。
//
//
//  ────────────────────────────────────────────────────────
//  已知问题 ②：CASE ... OF 区间分支死循环
//
//  下面这段编译能通过，但运行时永不结束（实测 15s+ 无输出）：
//
//    temperature := 75;
//    CASE temperature OF
//        0..50   : level := 1;
//        51..80  : level := 2;
//        81..200 : level := 3;
//    END_CASE        // ← 死循环
//
//  同样逻辑改写成 IF/ELSIF 立刻正常。所以本例的温度分级用
//  IF/ELSIF 实现。
//
//
//  ────────────────────────────────────────────────────────
//  已知问题 ③：结构体作函数形参，返回值错乱
//
//    FUNCTION GetY : REAL
//    VAR_INPUT p : Point; END_VAR
//        GetY := p.Y;          // ← 返回 nil
//    END_FUNCTION
//
//  直接读结构体字段（本例第 5 节）完全正常，只有"结构体被传进
//  函数"这一种会错。标量形参（本例第 6 节的 REAL）正常。
//
//  根因：FunctionEmitter.EnsureReg 遇到它不认识的 IR 值类型时，
//  只分配寄存器却不发任何加载指令，静默返回一个未初始化寄存器。
//
//
//  ────────────────────────────────────────────────────────
//  已知问题 ④：字符串字面量打出来是 nil
//
//    s := 'hello';  Print(s);   // → nil
//    Print('world');            // → nil
//
//  同一个根因（已知问题 ③）：字符串字面量在 IR 里是 GlobalVariable，
//  而 EnsureReg 不处理它。而且字节码路径根本没有全局初始化器机制，
//  ConstantDataArray 的字符串内容从没被写进全局槽。要修得补两条链路。
//
//
//  ────────────────────────────────────────────────────────
//  已知问题 ⑤：IEC 标准库函数在字节码后端跑不通
//
//  SQRT / SIN / ABS 等在 IR 层会被降级成 C extern 函数名
//  （sqrt / sin / fabs…），而 ByteCodeGenerator 要求每个 extern
//  都在 hostFunctions 里有登记，缺了就抛 InvalidOperationException。
//  HostCallIds 里只有 9 个数学常量，降级要 13 个 —— 接不齐。
//
//  所以 IDE 没有注入 IECStandardLibrary：注入了只会把前端那句老实的
//  "未声明的函数" 换成一条指不到用户代码的后端报错。
//
//
//  ────────────────────────────────────────────────────────
//  已知问题 ⑥：INT 与 REAL 混合运算，结果标签错成 INT
//
//    a := 55;  b := 2.0;  c := a / b;   // → 1.4E-322，不是 27.5
//    c := 10.0 / 2.0;                   // → 5      正常
//    d := a / 2;                        // → 27     正常
//    c := a * 1.5;                      // → 4.05E-322，不是 82.5
//
//  纯 INT、纯 REAL 都对，只有混合类型中招。把那两个垃圾值的位模式还原
//  出来是 28 和 82 —— 正好是 27.5 和 82.5 取整后的值。说明算是对了，
//  只是结果被存成了 INT 标签，再按 double 读就成了非规格化数。
//
//  最小复现见 Samples/debug-test.st 的 avg := sum / 2.0。
// ═══════════════════════════════════════════════════════════
