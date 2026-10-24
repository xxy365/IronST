// ═══════════════════════════════════════════════════════════
//  Samples/loop-carry.st
//
//  WHILE 内「循环携带变量 + 函数调用」累加缺陷的最小复现。
//
//  运行：
//    ironst run Samples/loop-carry.st
//
//  期望（每轮 ra 依次）：
//    1111  →  3333  →  6666
//
//  实测（每轮 ra 依次）：
//    1112  →  2224  →  3336
//  差值恒为 1112 ⇒ 每轮都加了同一个旧值，循环携带量没有跟着更新。
//
//  注意：循环内只放 1 条 Print。多放会把循环体撑出 8 位分支范围，
//        汇编器直接抛 “Branch offset ... out of range for 8-bit patch”。
// ═══════════════════════════════════════════════════════════


FUNCTION Sum4 : DINT
VAR_INPUT
    a : DINT;
    b : DINT;
    c : DINT;
    d : DINT;
END_VAR
    Sum4 := a * 1000 + b * 100 + c * 10 + d;
END_FUNCTION


PROGRAM Main
VAR
    i : INT;
    ra : DINT;
END_VAR
    i := 0;
    ra := 0;

    WHILE i < 3 DO
        i := i + 1;
        ra := ra + Sum4(i, i, i, i);
        Print(ra);                  // 每轮累加后的 ra
    END_WHILE

    Print(ra);                      // 循环结束后的最终值
END_PROGRAM
