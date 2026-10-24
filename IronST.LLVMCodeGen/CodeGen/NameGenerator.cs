// 无变更，保持原样
namespace IronST.LLVMCodeGen.CodeGen;

public sealed class NameGenerator
{
    private int _valueCounter;
    private readonly Dictionary<string, int> _blockHintCounters = [];

    public string NextValue(string? hint = null)
    {
        int id = ++_valueCounter;
        return hint != null ? $"{hint}{id}" : $"v{id}";
    }

    public string NextBlock(string hint = "bb")
    {
        if (!_blockHintCounters.TryGetValue(hint, out int count))
            count = 0;
        count++;
        _blockHintCounters[hint] = count;
        return $"{hint}{count}";
    }

    public void ResetFunction()
    {
        _valueCounter = 0;
        _blockHintCounters.Clear();
    }
}