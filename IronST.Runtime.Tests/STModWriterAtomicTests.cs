// STModWriterAtomicTests.cs — P3：`.stmod` 原子写出（链接协议 §8 步骤 14）。
//
// 要求：写失败时**不留下**看似有效的半成品，也不残留临时文件；
// 目标已存在时不得被写坏。
using IronST.Runtime.Shared.Modules;

namespace IronST.Runtime.Tests;

public class STModWriterAtomicTests : IDisposable
{
    private readonly string _dir;

    public STModWriterAtomicTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-stmod-atomic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private static STModFunctionEntry MainEntry(uint id, ulong offset) => new()
    {
        FunctionId = id,
        Name = "Main",
        Signature = "void ()",
        Flags = STModFunctionFlags.None,
        CodeOffset = offset,
        CodeLength = 1,
    };

    private static Dictionary<string, STModMetadataEntry> DefaultMetadata(string name) =>
        new(StringComparer.Ordinal)
        {
            [STModMetadataKeys.ModuleName] =
                STModMetadataEntry.FromString(STModMetadataKeys.ModuleName, name),
            [STModMetadataKeys.ModuleVersion] =
                STModMetadataEntry.FromString(STModMetadataKeys.ModuleVersion, "1.0.0"),
            [STModMetadataKeys.CompilerVersion] =
                STModMetadataEntry.FromString(STModMetadataKeys.CompilerVersion, "1.0.0.0"),
        };

    private static STModModule MinimalModule(
        string name = "Atomic",
        IReadOnlyList<STModFunctionEntry>? functions = null,
        Dictionary<string, STModMetadataEntry>? metadata = null) => new()
    {
        Metadata = metadata ?? DefaultMetadata(name),
        Functions = functions ?? [MainEntry(0, 0)],
        Code = [0x80],          // RET
        ConstantPool = [],
        Sections = [],
        HasEntryPoint = false,
        EntryPC = 0,
        Relocations = null,
        Imports = [],
        Exports = [],
        IsLinked = true,
        GlobalCount = 0,
    };

    private string[] TempFiles() =>
        Directory.GetFiles(_dir, ".*.tmp", SearchOption.AllDirectories);

    [Fact]
    public void SaveAtomic_WritesReadableModule_AndReplacesExistingFile()
    {
        var target = Path.Combine(_dir, "out.stmod");
        File.WriteAllBytes(target, "ORIGINAL-GARBAGE"u8.ToArray());

        STModWriter.SaveAtomic(target, MinimalModule());

        var loaded = STModReader.Load(target);
        Assert.Equal("Atomic", loaded.GetString(STModMetadataKeys.ModuleName));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void SaveAtomic_OnValidationFailure_LeavesNoOutput_AndNoTempFile()
    {
        var target = Path.Combine(_dir, "fresh.stmod");

        // 非法模块：两个函数同名 → 写侧必须先失败。
        var bad = MinimalModule(functions: [MainEntry(0, 0), MainEntry(1, 1)]);

        Assert.Throws<STModFormatException>(() => STModWriter.SaveAtomic(target, bad));

        Assert.False(File.Exists(target));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void SaveAtomic_OnValidationFailure_KeepsExistingFileIntact()
    {
        var target = Path.Combine(_dir, "keep.stmod");
        byte[] original = "KEEP-ME-INTACT"u8.ToArray();
        File.WriteAllBytes(target, original);

        var badMeta = DefaultMetadata("Bad");
        badMeta.Remove(STModMetadataKeys.ModuleName);   // 缺必需元数据
        var bad = MinimalModule(metadata: badMeta);

        Assert.Throws<STModFormatException>(() => STModWriter.SaveAtomic(target, bad));

        Assert.Equal(original, File.ReadAllBytes(target));
        Assert.Empty(TempFiles());
    }

    [Fact]
    public void SaveAtomic_CreatesMissingParentDirectory()
    {
        var target = Path.Combine(_dir, "nested", "deep", "out.stmod");

        STModWriter.SaveAtomic(target, MinimalModule());

        Assert.True(File.Exists(target));
        Assert.Equal(
            "Atomic",
            STModReader.Load(target).GetString(STModMetadataKeys.ModuleName));
    }
}
