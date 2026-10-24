// ═══════════════════════════════════════════════════════════
//  Samples/runtime-error-line.st
//
//  运行期错误带「文件:行号」的最小示例。
//
//  运行（CLI）：
//    ironst run Samples/runtime-error-line.st
//    → 运行时错误: [ArrayBounds] at 0x58: BOUNDS: index 99 out of range [0, 10)
//       （Samples/runtime-error-line.st:30）
//
//  改造前只会给 "at 0x58"（裸 PC），不知道是哪一行出的错；
//  改造后位置由 VM 在错误边界单点回填（见 RuntimeError.cs / SourceResolver.cs），
//  异常 Message 自带「（文件:行）」—— CLI / IDE 都只打印 ex.Message。
//
//  在 IDE 里：
//    文件 → 打开   本文件
//    F5            调试（停在出错行，整行高亮）
//    Ctrl+F5       不调试运行
//    底部输出窗口打印带「（文件:行）」的运行时错误，编辑器自动跳到出错行。
// ═══════════════════════════════════════════════════════════

PROGRAM Main
VAR
    g : ARRAY[0..9] OF DINT;   // 10 个元素，下标 0..9
END_VAR

    g[0] := 1;                 // 合法
    Print(g[0]);

    g[99] := 1;                // ← 越界：下标 99 超出 [0, 10)
END_PROGRAM
