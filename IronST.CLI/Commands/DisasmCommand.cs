// DisasmCommand.cs — disasm <file.isb>
using IronST.Runtime.Shared;

namespace IronST.CLI.Commands;

public static class DisasmCommand
{
    public static int Run(string[] args)
    {
        var input = args.FirstOrDefault();
        if (input == null || !File.Exists(input))
        {
            Console.Error.WriteLine($"错误: 找不到文件 '{input}'");
            return 1;
        }

        try
        {
            var module = IronRuntime.LoadModule(input);
            var code = module.Code;

            Console.WriteLine($"入口 PC: 0x{module.EntryPC:X}  全局变量: {module.GlobalCount}  常量池: {module.ConstantPool.Length}");
            Console.WriteLine("────────────────────────────────────────────");

            uint pc = module.EntryPC;
            int count = 0;
            while (pc < (uint)code.Length)
            {
                var line = Disassembler.Disassemble(code, pc);
                Console.WriteLine($"{pc:X4}: {line}");

                byte len = VMBase.GetInstrLength((OpCode)code[pc]);
                pc += (uint)Math.Max((int)len, 1);

                if (++count > 100_000)
                {
                    Console.Error.WriteLine("警告: 反汇编超过 10 万条，停止");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误: {ex.Message}");
            return 1;
        }

        return 0;
    }
}
