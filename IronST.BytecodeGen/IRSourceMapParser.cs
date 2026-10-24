using System.Text.RegularExpressions;

namespace IronST.BytecodeGen;

/// <summary>
/// 从 LLVM IR 文本解析调试元数据，构建 指令序号 -> 源码位置 映射。
/// </summary>
public static class IRSourceMapParser
{
    /// <summary>
    /// 解析 IR 文本，返回每个函数内每条 IR 指令对应的源码位置。
    /// </summary>
    /// <param name="irText">完整的 LLVM IR 文本</param>
    /// <param name="sourceFile">源文件名</param>
    /// <returns>函数名 -> 指令列表（索引对应 IR 中该函数的第 N 条指令，null 表示无源码位置）</returns>
    public static Dictionary<string, List<(string file, int line)?>> Parse(
        string irText, string sourceFile)
    {
        // Step 1: 解析 DILocation 元数据定义
        // 格式: !N = !DILocation(line: X, column: Y, scope: !M)
        var diLocations = new Dictionary<int, int>();
        foreach (Match m in Regex.Matches(irText,
                     @"!(\d+)\s*=\s*!DILocation\(line:\s*(\d+)"))
        {
            int id = int.Parse(m.Groups[1].Value);
            int line = int.Parse(m.Groups[2].Value);
            diLocations[id] = line;
        }

        // Step 2: 逐函数、逐指令提取 !dbg 引用
        var result = new Dictionary<string, List<(string file, int line)?>>(
            StringComparer.Ordinal);

        string? currentFunc = null;
        List<(string file, int line)?>? instrList = null;
        bool inBody = false;

        foreach (var rawLine in irText.Split('\n'))
        {
            string trimmed = rawLine.Trim();

            // 函数定义: define void @Main() #0 !dbg !2 {
            var funcMatch = Regex.Match(trimmed, @"^define\s+.*@([\w.]+)\s*\(");
            if (funcMatch.Success)
            {
                currentFunc = funcMatch.Groups[1].Value;
                instrList = new List<(string, int)?>();
                result[currentFunc] = instrList;
                inBody = trimmed.Contains('{');
                continue;
            }

            // 函数体开始（{ 单独一行）
            if (!inBody && currentFunc != null && trimmed == "{")
            {
                inBody = true;
                continue;
            }

            // 函数体结束
            if (inBody && trimmed == "}")
            {
                inBody = false;
                currentFunc = null;
                instrList = null;
                continue;
            }

            if (!inBody || instrList == null)
                continue;

            // 跳过非指令行
            if (trimmed.Length == 0
                || Regex.IsMatch(trimmed, @"^[\w$.]+:")
                || trimmed.StartsWith(';')
                || trimmed.StartsWith('!'))
                continue;

            // IR 指令，查找 !dbg !N
            var dbgMatch = Regex.Match(trimmed, @"!dbg\s+!(\d+)");
            if (dbgMatch.Success)
            {
                int mdId = int.Parse(dbgMatch.Groups[1].Value);
                if (diLocations.TryGetValue(mdId, out int srcLine) && srcLine > 0)
                    instrList.Add((sourceFile, srcLine));
                else
                    instrList.Add(null);
            }
            else
            {
                instrList.Add(null);
            }
        }

        return result;
    }
}