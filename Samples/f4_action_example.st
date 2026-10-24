PROGRAM Main
VAR
    timer : TON;         (* 标准功能块 TON — 外部库声明已注册（A5 修复），运行时接口已存在（简单行为模拟），完整 PLC 行为随标准库完善 *);
    outQ : BOOL;
END_VAR
(* 当前无法完整编译运行：外部库声明链路（F4）仍挂起，标准 FB 完整行为（计时状态机）未实现（A5 部分修）。
   本示例仅演示声明语法与修复状态：前端可识别 TON 声明（IECStandardLibrary.CreateStandardFunctionBlocks），运行时有基本接口（0x0100）。 *)
END_PROGRAM
