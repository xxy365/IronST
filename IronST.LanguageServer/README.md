# IronST Language Server（模块 F）

为 ST（IEC 61131-3 结构化文本）编辑器提供智能提示能力的 LSP 服务器。

可执行文件名为 **`ironst-ls`**，通过 **stdio** 与客户端通信，零第三方依赖
（JSON-RPC 帧协议与 LSP 类型均手写，只用 `System.Text.Json`）。

## 已实现的能力

| LSP 方法 | 功能 | 说明 |
|---|---|---|
| `initialize` / `initialized` / `shutdown` / `exit` | 生命周期 | 退出码：正常 shutdown+exit 为 0，否则 1 |
| `textDocument/didOpen` / `didChange` / `didSave` / `didClose` | 文档同步 | **全量同步**（`TextDocumentSyncKind.Full`） |
| `textDocument/publishDiagnostics` | 实时诊断 | 编辑后 150ms 防抖推送 |
| `textDocument/completion` | 补全 | 变量 / 函数 / 功能块 / 类型 / 关键字 / IEC 标准函数；`.` 后给结构体字段与枚举成员 |
| `textDocument/hover` | 悬停 | Markdown 格式的变量类型、函数签名、结构体与枚举定义 |
| `textDocument/definition` | 跳转到定义 | 跨文件有效 |
| `textDocument/references` | 查找引用 | 含声明处，去重 |
| `textDocument/documentSymbol` | 文档大纲 | POU 与类型为父节点，参数与变量为子节点 |
| `textDocument/signatureHelp` | 参数提示 | 光标在调用括号内时给出签名与当前参数序号 |

尚未实现的方法返回标准错误码 `-32601 MethodNotFound`，客户端会优雅降级。
`$/cancelRequest`、`workspace/didChangeConfiguration` 等通知按规范静默忽略。

## 运行

```bash
# 直接运行（默认走 stdio）
dotnet run --project IronST.LanguageServer

# 或用编译产物
dotnet IronST.LanguageServer/bin/Debug/net10.0/ironst-ls.dll

# 日志写到文件（否则写 stderr；stdout 严格保留给协议）
dotnet .../ironst-ls.dll --log ironst-ls.log
```

## 客户端配置

### VS Code

在 `.vscode/settings.json`（工作区）或用户设置中配置，并安装一个通用 LSP 客户端扩展
（如「vscode-languageclient」生态下的任意 ST/自定义语言扩展）：

```jsonc
{
  // 若使用「Custom Language Extension」类客户端
  "customLanguageServer.command": "dotnet",
  "customLanguageServer.args": [
    "run", "--project", "C:/Users/xxy/source/repos/IronST/IronST.LanguageServer"
  ],
  "customLanguageServer.languageId": "st"
}
```

更常见的做法是写一个十几行的 VS Code 扩展挂 `vscode-languageclient`：

```ts
import * as lc from "vscode-languageclient/node";

const client = new lc.LanguageClient(
  "ironst",
  "IronST Language Server",
  {
    command: "dotnet",
    args: ["run", "--project", "<仓库路径>/IronST.LanguageServer"],
  },
  { documentSelector: [{ language: "st" }] }
);
client.start();
```

### Neovim（0.10+ 原生 LSP）

```lua
vim.filetype.add({ extension = { st = "st" } })

vim.api.nvim_create_autocmd("FileType", {
  pattern = "st",
  callback = function(args)
    vim.lsp.start({
      name = "ironst",
      cmd = { "dotnet", "<仓库路径>/IronST.LanguageServer/bin/Debug/net10.0/ironst-ls.dll" },
      root_dir = vim.fs.dirname(
        vim.fs.find({ "IronST.slnx", ".git" }, { upward = true })[1]
      ),
    }, { bufnr = args.buf })
  end,
})
```

### Helix

```toml
# languages.toml
[language-server.ironst]
command = "dotnet"
args = ["<仓库路径>/IronST.LanguageServer/bin/Debug/net10.0/ironst-ls.dll"]

[[language]]
name = "st"
scope = "source.st"
file-types = ["st"]
roots = ["IronST.slnx"]
language-servers = ["ironst"]
```

### 任意支持 stdio LSP 的客户端

启动命令即 `dotnet <仓库路径>/IronST.LanguageServer/bin/Debug/net10.0/ironst-ls.dll`，
无需任何参数。客户端按 LSP 规范用 `Content-Length` 帧走 stdin/stdout 即可。

## 手动验证（不装编辑器）

```bash
# 1) 启动服务器（会阻塞等待 stdio）
dotnet IronST.LanguageServer/bin/Debug/net10.0/ironst-ls.dll --log ls.log

# 2) 另开一个终端，按 LSP 规范发送帧，例如：
printf 'Content-Length: 118\r\n\r\n{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":1,"capabilities":{},"rootUri":"file:///"}}' | ...
```

真实客户端里更省事的做法是用编辑器的 LSP 调试面板，或直接跑测试：

```bash
dotnet test IronST.LanguageServer.Tests
```

测试里 `LspTestClient` 就是一个进程内的最小 LSP 客户端，可作为协议用法参考。

## 工作方式

1. **文档同步**：客户端把整个文件内容发过来（全量同步），服务器在 `DocumentStore` 里维护副本。
2. **分析调度**：`didOpen`/`didChange` 触发 150ms 防抖 → 调 `FrontendPipeline.AnalyzeFiles`
   把**所有打开的文档**一起分析（跨文件符号共享）→ 缓存快照 → 按文档推送诊断。
   分析期间若文档又被编辑，本次结果作废，避免过期结果覆盖新结果。
3. **功能响应**：各 Provider 复用缓存的 `AnalysisResult`（诊断、符号、引用、调用点、类型），
   不重复跑前端。

### 位置换算（关键细节）

三种坐标系需要互转：

- **LSP 位置**：0 基行、0 基 **UTF-16 码元**列
- **UTF-16 绝对偏移**：.NET `string` 索引
- **前端位置**：1 基行、1 基**码位（code point）**列（ANTLR 的 column）

差异来源：ANTLR 数的是码位，LSP 数的是 UTF-16 码元。源码里出现增补平面字符
（如 emoji，占 1 个码位但 2 个 UTF-16 码元）时，同一行后半段的列号会整体错位。
`Protocol/TextIndex.cs` 按行建「码位索引 → UTF-16 偏移」映射表精确换算，两种字符集都正确。

### 容错

参数提示用**文本扫描**识别调用（跳过注释与字符串、维护括号栈），而不是依赖 AST，
因此源码有语法错误时依然可用。

## 项目结构

```
IronST.LanguageServer/
├── Program.cs                 入口：接 stdio，--log 指定日志文件
├── JsonRpc/
│   ├── RpcReader.cs           Content-Length 帧解析
│   ├── RpcWriter.cs           帧写出（内部加锁，主循环与分析线程可并发写）
│   └── RpcMessages.cs         JSON-RPC 2.0 消息构造与错误码
├── Protocol/
│   ├── TextIndex.cs           三种坐标系互换 + 行内标识符定位
│   ├── Identifiers.cs         标识符/限定名/调用扫描
│   └── Json.cs                LSP 对象构造工具与常量
├── Server/
│   ├── LanguageServer.cs      主循环与方法分发
│   ├── DocumentStore.cs       打开文档集合
│   └── AnalysisService.cs     防抖调度 + 快照缓存 + 诊断推送
└── Features/
    ├── DiagnosticsMapper.cs
    ├── CompletionProvider.cs
    ├── HoverProvider.cs
    ├── NavigationProvider.cs  definition + references
    ├── DocumentSymbolProvider.cs
    └── SignatureHelpProvider.cs
```

依赖：仅 `IronST.Frontend` + `IronST.Common`（符合分层规则，不反向依赖上层）。

## 已知限制

- **全量同步**：每次编辑都重传整个文件。ST 程序通常很小，影响可忽略；将来可改为增量同步。
- **无 `workspace/didChangeWatchedFiles`**：磁盘上其他文件的变更不会自动触发重新分析，
  需要重新打开或编辑该文件。
- **不支持取消**：`$/cancelRequest` 被忽略。分析本身很快，实践中无感知。
- **跨文件跳转会读磁盘**：跳转到其他文件的定义时，若该文件未在编辑器中打开，
  服务器直接 `File.ReadAllText`。文件恰好在跳转瞬间被改会导致轻微不一致。
- **限定名补全只支持一级**：`p.` 能补结构体字段，但 `p.sub.` 这类二级嵌套未实现。
