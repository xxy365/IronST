// CompilationSourcesTests.cs — 打开工程后，前端该看到哪些文件（IDE 阶段 VI）。
//
// 契约：
//   · 没有工程（或当前文件不属于工程）→ 只分析当前标签页（保持既有单文件行为）；
//   · 当前文件属于工程 → 分析**整个工程**：当前文件用编辑器里的实时内容
//     （可能未保存），其它文件从磁盘读；
//   · 顺序 = 工程文件里的顺序（诊断顺序与入口推断都依赖它）；
//   · 路径比较大小写不敏感（Windows 上目录对话框给的路径与 .stproj 里的
//     拼写不保证一致）。
//
// 没有这一步，打开工程后 Main.st 会把别的文件里的 ClampDint / MotorInfo
// 全报成"未声明/未知类型" —— 而同一个工程 build 起来是 0 错误。
using IronST.CLI;
using IronST.IDE.Services;

namespace IronST.IDE.Tests;

public class CompilationSourcesTests
{
    private static STProjectFile Project(params string[] sources) => new()
    {
        ProjectPath = @"C:\proj\P.stproj",
        ProjectDirectory = @"C:\proj",
        Name = "P",
        SourceFiles = sources,
    };

    private static Func<string, string> Disk(params (string Path, string Text)[] files)
    {
        var map = files.ToDictionary(f => f.Path, f => f.Text, StringComparer.OrdinalIgnoreCase);
        return path => map.TryGetValue(path, out var text)
            ? text
            : throw new InvalidOperationException($"测试没有准备这个文件: {path}");
    }

    [Fact]
    public void NoProject_AnalyzesCurrentTabOnly()
    {
        var list = CompilationSources.Build(
            null, @"C:\proj\Main.st", "Main.st", "PROGRAM Main END_PROGRAM", Disk());

        var one = Assert.Single(list);
        Assert.Equal("PROGRAM Main END_PROGRAM", one.Source);
        Assert.Equal(@"C:\proj\Main.st", one.FileName);
    }

    [Fact]
    public void CurrentFileInProject_UsesLiveCode_OthersFromDisk()
    {
        var project = Project(@"C:\proj\Main.st", @"C:\proj\Motor.st", @"C:\proj\Utils.st");

        var list = CompilationSources.Build(
            project,
            @"C:\proj\Main.st",
            "Main.st",
            "// 编辑器里的实时内容",
            Disk(
                (@"C:\proj\Motor.st", "// Motor 磁盘内容"),
                (@"C:\proj\Utils.st", "// Utils 磁盘内容")));

        Assert.Equal(3, list.Count);
        Assert.Equal("// 编辑器里的实时内容", list[0].Source);
        Assert.Equal("// Motor 磁盘内容", list[1].Source);
        Assert.Equal("// Utils 磁盘内容", list[2].Source);

        Assert.Equal(
            [@"C:\proj\Main.st", @"C:\proj\Motor.st", @"C:\proj\Utils.st"],
            list.Select(e => e.FileName).ToList());
    }

    [Fact]
    public void CurrentFileInProject_KeepsProjectOrder()
    {
        // 当前文件在工程里排最后 → 列表里也排最后，不能因为"它是当前"就挪到最前
        var project = Project(@"C:\proj\Main.st", @"C:\proj\Utils.st");

        var list = CompilationSources.Build(
            project,
            @"C:\proj\Utils.st",
            "Utils.st",
            "// 实时",
            Disk((@"C:\proj\Main.st", "// Main")));

        Assert.Equal(
            [@"C:\proj\Main.st", @"C:\proj\Utils.st"],
            list.Select(e => e.FileName).ToList());
        Assert.Equal("// Main", list[0].Source);
        Assert.Equal("// 实时", list[1].Source);
    }

    [Fact]
    public void UntitledTab_AnalyzesCurrentTabOnly()
    {
        var project = Project(@"C:\proj\Main.st");

        var list = CompilationSources.Build(
            project, null, "Untitled-1", "// 新文件", Disk());

        var one = Assert.Single(list);
        Assert.Equal("Untitled-1", one.FileName);
        Assert.Equal("// 新文件", one.Source);
    }

    [Fact]
    public void FileNotInProject_AnalyzesCurrentTabOnly()
    {
        var project = Project(@"C:\proj\Main.st");

        var list = CompilationSources.Build(
            project, @"C:\其它\Other.st", "Other.st", "// 别的文件", Disk());

        // 有路径的文件，前端里的文件名就是路径（诊断归属靠它），不是显示名
        var one = Assert.Single(list);
        Assert.Equal(@"C:\其它\Other.st", one.FileName);
        Assert.Equal("// 别的文件", one.Source);
    }

    [Fact]
    public void PathMatch_IsCaseInsensitive()
    {
        var project = Project(@"C:\PROJ\Main.st");

        var list = CompilationSources.Build(
            project, @"C:\proj\main.ST", "main.ST", "// 实时", Disk());

        var one = Assert.Single(list);
        Assert.Equal("// 实时", one.Source);
    }

    [Fact]
    public void NameFor_PrefersPath_AndFallsBackToDisplayName()
    {
        Assert.Equal(@"C:\proj\Main.st", CompilationSources.NameFor(@"C:\proj\Main.st", "Main.st"));
        Assert.Equal("Untitled-1", CompilationSources.NameFor(null, "Untitled-1"));
        Assert.Equal("Untitled-1", CompilationSources.NameFor("", "Untitled-1"));
    }
}
