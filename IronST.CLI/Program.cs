// Program.cs — IronST 命令行入口（模块 D）
//
// 用法:
//   ironst compile <file.st ...> [-o out.isb] [--ir out.ll] [--bc out.bc] [--opt 0|1|2]
//   ironst run     <file.st ...> [--entry NAME] [--stack N]
//   ironst run     <file.isb | file.irst> [--stack N]
//   ironst disasm  <file.isb>
//   ironst verify  <file.st>
//   ironst analyze <file.st>
//   ironst repl
//   ironst demo
using IronST.CLI.Commands;

Console.OutputEncoding = System.Text.Encoding.UTF8;

return CliRouter.Run(args);
