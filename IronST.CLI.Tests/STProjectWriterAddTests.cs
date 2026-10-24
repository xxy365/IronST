// STProjectWriterAddTests.cs — 往已有工程里追加条目 + 新建源文件（阶段 IV）。
//
// 契约：
//   · 追加是 **XML 级** 的：只往 <ItemGroup> 里加一项，工程文件里的注释、
//     未知元素、PropertyGroup 顺序都必须原样保留（别人的 .stproj 里有自定义东西时不能丢）；
//   · 拒绝重复、拒绝不存在、拒绝越出工程目录、拒绝错误扩展名；
//   · 落盘后仍能被 STProjectFile.Load 读通，且编码仍是 UTF-8（声明与字节一致、无 BOM）。
using IronST.CLI.Commands;

namespace IronST.CLI.Tests;

[Collection("cli-console")]
public class STProjectWriterAddTests : IDisposable
{
    private readonly string _dir;

    public STProjectWriterAddTests()
    {
        _dir = Path.Combine(
            Path.GetTempPath(), "ironst-stproj-add", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private static (int Exit, string Out, string Err) Invoke(params string[] args)
    {
        var outW = new StringWriter();
        var errW = new StringWriter();
        var prevOut = Console.Out;
        var prevErr = Console.Error;
        try
        {
            Console.SetOut(outW);
            Console.SetError(errW);
            int exit = CliRouter.Run(args);
            return (exit, outW.ToString(), errW.ToString());
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
        }
    }

    /// <summary>造一个新工程（Main.st 已生成），返回 .stproj 路径。</summary>
    private string NewProject(string name)
    {
        var projDir = Path.Combine(_dir, name);
        return STProjectWriter.CreateProject(
            projDir, new STProjectTemplate { Name = name });
    }

    // ═══════════════════════════════════════════════════════════
    //  1. 追加源文件
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void AddSource_AppendsItem_AndIsVisibleAfterReload()
    {
        var projPath = NewProject("Add");
        var projDir = Path.GetDirectoryName(projPath)!;
        File.WriteAllText(Path.Combine(projDir, "Motor.st"), "FUNCTION Motor : DINT\nEND_FUNCTION\n");

        STProjectWriter.AddSource(projPath, "Motor.st");

        var project = STProjectFile.Load(projPath);
        Assert.Equal(
            [
                Path.GetFullPath(Path.Combine(projDir, "Main.st")),
                Path.GetFullPath(Path.Combine(projDir, "Motor.st")),
            ],
            project.SourceFiles);
    }

    [Fact]
    public void AddSource_PreservesCommentsUnknownElementsAndProperties()
    {
        var projDir = Path.Combine(_dir, "Legacy");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "Main.st"), "PROGRAM Main\nEND_PROGRAM\n");
        File.WriteAllText(Path.Combine(projDir, "Extra.st"), "FUNCTION Extra : DINT\nEND_FUNCTION\n");

        var projPath = Path.Combine(projDir, "Legacy.stproj");
        File.WriteAllText(projPath, """
            <?xml version="1.0" encoding="utf-8"?>
            <STProject Version="1">
              <!-- 保留我 -->
              <PropertyGroup>
                <Name>Legacy</Name>
                <VendorSetting>keep-me</VendorSetting>
              </PropertyGroup>
              <ItemGroup>
                <STCompile Include="Main.st" />
              </ItemGroup>
              <ItemGroup>
                <CustomItem Include="x" />
              </ItemGroup>
            </STProject>
            """);

        STProjectWriter.AddSource(projPath, "Extra.st");

        string text = File.ReadAllText(projPath);
        Assert.Contains("<!-- 保留我 -->", text);
        Assert.Contains("<VendorSetting>keep-me</VendorSetting>", text);
        Assert.Contains("<CustomItem Include=\"x\" />", text);
        Assert.Contains("<STCompile Include=\"Extra.st\" />", text);

        // 而且确实读得回来（两个源文件）
        Assert.Equal(2, STProjectFile.Load(projPath).SourceFiles.Count);
    }

    [Fact]
    public void AddSource_RejectsDuplicate()
    {
        var projPath = NewProject("Dup");
        var projDir = Path.GetDirectoryName(projPath)!;
        File.WriteAllText(Path.Combine(projDir, "Motor.st"), "FUNCTION Motor : DINT\nEND_FUNCTION\n");
        STProjectWriter.AddSource(projPath, "Motor.st");

        Assert.Throws<ProjectFileException>(
            () => STProjectWriter.AddSource(projPath, "Motor.st"));
        Assert.Throws<ProjectFileException>(
            () => STProjectWriter.AddSource(projPath, "Main.st"));
    }

    [Fact]
    public void AddSource_RejectsMissingFile()
    {
        var projPath = NewProject("Missing");

        Assert.Throws<ProjectFileException>(
            () => STProjectWriter.AddSource(projPath, "Ghost.st"));
    }

    [Fact]
    public void AddSource_RejectsBadExtensionAndEscapingPath()
    {
        var projPath = NewProject("Bad");
        var projDir = Path.GetDirectoryName(projPath)!;
        File.WriteAllText(Path.Combine(_dir, "Outside.st"), "PROGRAM Outside\nEND_PROGRAM\n");
        File.WriteAllText(Path.Combine(projDir, "notes.txt"), "hi");

        Assert.Throws<ProjectFileException>(
            () => STProjectWriter.AddSource(projPath, "notes.txt"));
        Assert.Throws<ProjectFileException>(
            () => STProjectWriter.AddSource(projPath, "../Outside.st"));
    }

    [Fact]
    public void AddSource_WritesUtf8WithoutBom()
    {
        var projPath = NewProject("Enc");
        var projDir = Path.GetDirectoryName(projPath)!;
        File.WriteAllText(Path.Combine(projDir, "Motor.st"), "FUNCTION Motor : DINT\nEND_FUNCTION\n");

        STProjectWriter.AddSource(projPath, "Motor.st");

        string text = File.ReadAllText(projPath);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text.TrimStart());

        byte[] bytes = File.ReadAllBytes(projPath);
        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "工程文件不应带 UTF-8 BOM");
    }

    // ═══════════════════════════════════════════════════════════
    //  2. 追加模块引用
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void AddModuleReference_AppendsStmodReference()
    {
        var projPath = NewProject("Refs");
        var projDir = Path.GetDirectoryName(projPath)!;
        File.WriteAllBytes(Path.Combine(projDir, "Math.stmod"), [0x00]);

        STProjectWriter.AddModuleReference(projPath, "Math.stmod");

        var project = STProjectFile.Load(projPath);
        Assert.Equal(
            [Path.GetFullPath(Path.Combine(projDir, "Math.stmod"))],
            project.ModuleReferences);
    }

    [Fact]
    public void AddModuleReference_RejectsNonStmodAndDuplicate()
    {
        var projPath = NewProject("RefsBad");
        var projDir = Path.GetDirectoryName(projPath)!;
        File.WriteAllText(Path.Combine(projDir, "Motor.st"), "FUNCTION Motor : DINT\nEND_FUNCTION\n");
        File.WriteAllBytes(Path.Combine(projDir, "Math.stmod"), [0x00]);
        STProjectWriter.AddModuleReference(projPath, "Math.stmod");

        Assert.Throws<ProjectFileException>(
            () => STProjectWriter.AddModuleReference(projPath, "Motor.st"));
        Assert.Throws<ProjectFileException>(
            () => STProjectWriter.AddModuleReference(projPath, "Math.stmod"));
    }

    // ═══════════════════════════════════════════════════════════
    //  3. 新建源文件（模板必须真能编译）
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void CreateSourceFile_WritesCompilableStub()
    {
        var path = Path.Combine(_dir, "NewPou", "Motor.st");

        STProjectWriter.CreateSourceFile(path);

        Assert.True(File.Exists(path));
        Assert.Contains("PROGRAM Motor", File.ReadAllText(path));

        // 模板必须真的能过前端与字节码生成。
        var (exit, _, err) = Invoke("compile", path);
        Assert.Equal(0, exit);
        Assert.Equal("", err);
    }

    [Fact]
    public void CreateSourceFile_AddsIntoProjectDirectory_AndBuilds()
    {
        var projPath = NewProject("WithNew");
        var projDir = Path.GetDirectoryName(projPath)!;
        var newSource = Path.Combine(projDir, "Motor.st");

        STProjectWriter.CreateSourceFile(newSource);
        STProjectWriter.AddSource(projPath, "Motor.st");

        var isb = Path.Combine(projDir, "WithNew.isb");
        var (exit, _, err) = Invoke("build", projPath, "-o", isb);
        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(isb));
    }

    [Fact]
    public void CreateSourceFile_RejectsExistingFile()
    {
        var path = Path.Combine(_dir, "Exists", "Motor.st");
        STProjectWriter.CreateSourceFile(path);

        Assert.Throws<ProjectFileException>(() => STProjectWriter.CreateSourceFile(path));
    }

    [Fact]
    public void CreateSourceFile_RejectsInvalidProgramName()
    {
        var path = Path.Combine(_dir, "Bad", "Motor-1.st");

        // 文件名派生不出合法 ST 标识符 → 明确拒绝，而不是生成编不过的代码。
        Assert.Throws<ProjectFileException>(() => STProjectWriter.CreateSourceFile(path));
        Assert.False(File.Exists(path));
    }
}
