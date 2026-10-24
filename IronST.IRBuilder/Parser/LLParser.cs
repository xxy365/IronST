using IronST.IRBuilder.IR;

namespace IronST.IRBuilder.Parser;

/// <summary>LLVM IR 文件解析器：将 .ll 文本解析为 IR 对象模型</summary>
class LLParser
{
    private List<Token> _tokens = new();
    private int _pos;
    private Module _module = null!;
    private Function? _currentFunc;
    private BasicBlock? _currentBB;
    private readonly Dictionary<string, Value> _locals = new();
    private readonly Dictionary<string, BasicBlock> _blocks = new();
    private int _unnamedBBCounter;

    // ================================================================
    // 公共入口
    // ================================================================

    /// <summary>解析 .ll 文本，返回 Module 对象</summary>
    public Module Parse(string source)
    {
        _tokens = new Lexer(source).Tokenize();
        _pos = 0;
        _module = new Module();
        _locals.Clear();
        _blocks.Clear();

        while (!IsEof)
        {
            ParseTopLevel();
        }

        ResolveMetadataReferences();

        return _module;
    }

    // ================================================================
    // Token 读取辅助
    // ================================================================
    /// <summary>
    /// 修正 metadata 前向引用。
    /// 指令解析时可能创建了占位 MDTuple，等所有 metadata 定义解析完后，
    /// 用真实节点替换指令上引用的占位节点。
    /// </summary>
    private void ResolveMetadataReferences()
    {
        foreach (var func in _module.Functions)
        {
            ResolveMetadataInDict(func.MetadataAttachments);
            foreach (var bb in func.BasicBlocks)
            {
                foreach (var inst in bb.Instructions)
                {
                    ResolveMetadataInDict(inst.MetadataAttachments);
                }
            }
        }
    }

    private void ResolveMetadataInDict(Dictionary<string, MetadataNode>? dict)
    {
        if (dict == null)
            return;

        foreach (var key in dict.Keys.ToList())
        {
            var node = dict[key];
            if (node.Id >= 0)
            {
                var real = _module.Metadata.GetNode(node.Id);
                if (real != null && !ReferenceEquals(real, node))
                    dict[key] = real;
            }
        }
    }

    private bool IsEof => _pos >= _tokens.Count || _tokens[_pos].Kind == TokenKind.Eof;

    private Token Peek(int offset = 0) =>
        (_pos + offset) < _tokens.Count
            ? _tokens[_pos + offset]
            : new Token { Kind = TokenKind.Eof };

    private Token Advance() => _tokens[Math.Min(_pos++, _tokens.Count - 1)];

    private bool Match(string keyword) =>
        !IsEof && _tokens[_pos].Kind == TokenKind.Ident && _tokens[_pos].Text == keyword;

    private bool MatchKind(TokenKind kind) => !IsEof && _tokens[_pos].Kind == kind;

    private Token Expect(TokenKind kind)
    {
        if (IsEof || _tokens[_pos].Kind != kind)
            throw new FormatException($"期望 {kind}，实际为 {Peek()} 在位置 {_pos}");
        return Advance();
    }

    private string ExpectStr()
    {
        var tok = Expect(TokenKind.StringLit);
        return tok.Text.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n");
    }

    private long AdvanceInt()
    {
        if (MatchKind(TokenKind.IntLit))
            return long.Parse(Advance().Text);
        throw new FormatException($"期望整数字面量, 实际为 {Peek()}");
    }

    private string AdvanceGlobalName()
    {
        if (MatchKind(TokenKind.GlobalIdent))
            return Advance().Text;
        throw new FormatException($"期望全局标识符 (@name), 实际为 {Peek()}");
    }

    // ================================================================
    // 顶层结构解析
    // ================================================================

    private void ParseTopLevel()
    {
        // source_filename = "..."
        if (Match("source_filename"))
        {
            Advance();
            Expect(TokenKind.Eq);
            _module.SourceFilename = ExpectStr();
            return;
        }

        // target datalayout = "..." / target triple = "..."
        if (Match("target"))
        {
            Advance();
            var what = Advance().Text;
            Expect(TokenKind.Eq);
            var val = ExpectStr();
            if (what == "datalayout")
                _module.TargetDataLayout = val;
            else if (what == "triple")
                _module.TargetTriple = val;
            return;
        }

        // %Name = type { ... }
        if (
            MatchKind(TokenKind.LocalIdent)
            && _pos + 2 < _tokens.Count
            && _tokens[_pos + 1].Kind == TokenKind.Eq
            && _tokens[_pos + 2].Text == "type"
        )
        {
            ParseNamedType();
            return;
        }

        // declare ...
        if (Match("declare"))
        {
            ParseFunctionDecl();
            return;
        }

        // define ...
        if (Match("define"))
        {
            ParseFunctionDef();
            return;
        }

        // @Name = global/constant ...
        if (MatchKind(TokenKind.GlobalIdent) && !IsEof && Peek(1).Kind == TokenKind.Eq)
        {
            ParseGlobalVar();
            return;
        }

        // attributes #N = { ... } (跳过)
        if (Match("attributes"))
        {
            SkipAttributesBlock();
            return;
        }
        // metadata 定义
        if (MatchKind(TokenKind.Bang))
        {
            ParseMetadataDefinition();
            return;
        }

        // 未知行，跳过一个 token
        Advance();
    }

    /// <summary>
    /// 解析一行 metadata 定义，如:
    ///   !0 = !DIFile(filename: "test.c", directory: "/home")
    ///   !1 = distinct !DICompileUnit(language: DW_LANG_C99, file: !0, ...)
    ///   !llvm.dbg.cu = !{!1}
    /// </summary>
    private void ParseMetadataDefinition()
    {
        Expect(TokenKind.Bang);

        var nameOrIdTok = Advance(); // "0" 或 "llvm.dbg.cu"

        Expect(TokenKind.Eq);

        if (Match("distinct"))
            Advance();

        var node = ParseMetadataValue();

        if (int.TryParse(nameOrIdTok.Text, out int id))
            _module.Metadata.SetNode(id, node);
        else
            _module.Metadata.SetNamed(nameOrIdTok.Text, node);
    }

    /// <summary>解析一个 metadata 值：!DIFile(...)、!{...}、!"string"、!0 等</summary>
    private MetadataNode ParseMetadataValue()
    {
        Expect(TokenKind.Bang);

        var tok = Peek();

        // !{...} — metadata 元组
        if (tok.Kind == TokenKind.LBrace)
            return ParseMetadataTuple();

        // !"string" — metadata 字符串
        if (tok.Kind == TokenKind.StringLit)
        {
            Advance();
            return new MDString(tok.Text);
        }

        // !DIFile(...)、!DILocation(...)、!DICompileUnit(...) 等
        if (tok.Kind == TokenKind.Ident)
        {
            var typeName = Advance();
            if (typeName.Text.StartsWith("DI"))
                return ParseDINode(typeName.Text);
            // 不是 DI 开头的，当作普通标识符，包成 MDTuple
            var fallback = new MDTuple();
            fallback.Operands.Add(typeName.Text);
            return fallback;
        }

        // !0 — 引用已有节点
        if (tok.Kind == TokenKind.IntLit)
        {
            Advance();
            int refId = int.Parse(tok.Text);
            return _module.Metadata.GetNode(refId) as MetadataNode ?? new MDTuple() { Id = refId }; // 占位
        }

        throw new FormatException($"无法解析 metadata 值: {tok} 位置={_pos}");
    }

    /// <summary>解析 !{op1, op2, ...} 格式的元组</summary>
    private MDTuple ParseMetadataTuple()
    {
        var tuple = new MDTuple();
        Expect(TokenKind.LBrace);

        if (!MatchKind(TokenKind.RBrace))
        {
            tuple.Operands.Add(ParseMetadataOperand());
            while (MatchKind(TokenKind.Comma))
            {
                Advance();
                if (MatchKind(TokenKind.RBrace))
                    break;
                tuple.Operands.Add(ParseMetadataOperand());
            }
        }

        Expect(TokenKind.RBrace);
        return tuple;
    }

    /// <summary>
    /// 解析 DI* 节点，如:
    ///   !DIFile(filename: "test.c", directory: "/home")
    ///   !DILocation(line: 4, column: 12, scope: !2)
    ///   !DICompileUnit(language: DW_LANG_C99, file: !0, ...)
    /// </summary>
    private MDTuple ParseDINode(string typeName)
    {
        var tuple = new MDTuple();
        tuple.Operands.Add(typeName); // 第一个 operand 记录类型名

        Expect(TokenKind.LParen);

        if (!MatchKind(TokenKind.RParen))
        {
            ParseMetadataKV(tuple);
            while (MatchKind(TokenKind.Comma))
            {
                Advance();
                if (MatchKind(TokenKind.RParen))
                    break;
                ParseMetadataKV(tuple);
            }
        }

        Expect(TokenKind.RParen);
        return tuple;
    }

    /// <summary>解析一个 key: value 对，加入 tuple</summary>
    private void ParseMetadataKV(MDTuple tuple)
    {
        var key = Advance().Text; // filename, directory, line, column, scope 等
        Expect(TokenKind.Colon);
        var value = ParseMetadataScalar();
        tuple.Operands.Add(key);
        tuple.Operands.Add(value);
    }

    /// <summary>解析 metadata 中的标量值</summary>
    private object? ParseMetadataScalar()
    {
        var tok = Peek();

        // !N 引用、!"string"、!{...}
        if (tok.Kind == TokenKind.Bang)
        {
            Advance(); // 消费 !

            // !{...} 元组
            if (MatchKind(TokenKind.LBrace))
                return ParseMetadataTuple();

            // !"string"
            if (MatchKind(TokenKind.StringLit))
            {
                var s = Advance();
                return new MDString(s.Text);
            }

            // !N 编号引用
            var refTok = Peek();
            if (refTok.Kind == TokenKind.IntLit)
            {
                Advance();
                int refId = int.Parse(refTok.Text);
                return _module.Metadata.GetNode(refId) ?? (object)new MDTuple() { Id = refId };
            }

            // 其他标识符
            return Advance().Text;
        }

        // 裸字符串（DI 节点中的 producer: "clang version 18" 等）
        if (tok.Kind == TokenKind.StringLit)
        {
            Advance();
            return new MDString(tok.Text);
        }

        // 类型化整数 i32 42
        if (tok.Kind == TokenKind.IntType)
        {
            Advance();
            return AdvanceInt();
        }

        // 裸整数
        if (tok.Kind == TokenKind.IntLit)
            return AdvanceInt();

        // null
        if (Match("null"))
        {
            Advance();
            return null;
        }

        // 标识符：DW_LANG_C99、true、false、DW_ATE_signed 等
        if (tok.Kind == TokenKind.Ident)
            return Advance().Text;

        return Advance().Text;
    }

    /// <summary>解析 metadata operand（元组中的元素）</summary>
    private object? ParseMetadataOperand()
    {
        // !N 引用
        if (MatchKind(TokenKind.Bang))
        {
            Advance();
            var tok = Peek();
            if (tok.Kind == TokenKind.IntLit)
            {
                int id = int.Parse(Advance().Text);
                return _module.Metadata.GetNode(id) ?? (object)new MDTuple() { Id = id };
            }
            if (tok.Kind == TokenKind.StringLit)
            {
                Advance();
                return new MDString(tok.Text);
            }
            if (tok.Kind == TokenKind.LBrace)
            {
                return ParseMetadataTuple();
            }
            // 其他情况当作标识符
            return Advance().Text;
        }

        // 类型化值如 i32 0
        if (MatchKind(TokenKind.IntType))
        {
            Advance(); // i32
            return AdvanceInt();
        }

        // 裸整数
        if (MatchKind(TokenKind.IntLit))
            return AdvanceInt();

        // null
        if (Match("null"))
        {
            Advance();
            return null;
        }

        return Advance().Text;
    }

    private void SkipAttributesBlock()
    {
        // 跳到 { 之前
        while (!IsEof && !MatchKind(TokenKind.LBrace))
            Advance();

        if (!MatchKind(TokenKind.LBrace))
            return;

        int depth = 0;
        do
        {
            if (MatchKind(TokenKind.LBrace))
                depth++;
            if (MatchKind(TokenKind.RBrace))
                depth--;
            Advance();
        } while (depth > 0 && !IsEof);
    }

    // ================================================================
    // 类型解析
    // ================================================================

    private IrType ParseType()
    {
        var tok = Peek();

        // void
        if (Match("void"))
        {
            Advance();
            return IrType.Void;
        }

        // iN
        if (tok.Kind == TokenKind.IntType)
        {
            Advance();
            return new IntegerType(int.Parse(tok.Text[1..]));
        }

        // 浮点 / 特殊类型
        if (MatchKind(TokenKind.Ident))
        {
            switch (tok.Text)
            {
                case "half":
                    Advance();
                    return IrType.Half;
                case "bfloat":
                    Advance();
                    return new FloatType(FloatKind.BFloat);
                case "float":
                    Advance();
                    return IrType.Float;
                case "double":
                    Advance();
                    return IrType.Double;
                case "x86_fp80":
                    Advance();
                    return new FloatType(FloatKind.X86_FP80);
                case "fp128":
                    Advance();
                    return new FloatType(FloatKind.FP128);
                case "ppc_fp128":
                    Advance();
                    return new FloatType(FloatKind.PPC_FP128);
                case "label":
                    Advance();
                    return IrType.Label;
                case "metadata":
                    Advance();
                    return IrType.Metadata;
                case "token":
                    Advance();
                    return IrType.Token;
            }
        }

        // ptr [addrspace(N)]
        if (MatchKind(TokenKind.Ident) && tok.Text == "ptr")
        {
            Advance();
            if (Match("addrspace"))
            {
                Advance();
                Expect(TokenKind.LParen);
                int addr = (int)AdvanceInt();
                Expect(TokenKind.RParen);
                return new PointerType(addr);
            }
            return IrType.Pointer;
        }

        // { ... } 字面结构体
        if (tok.Kind == TokenKind.LBrace)
            return ParseStructType(false);

        // <{ ... }> 打包结构体
        if (tok.Kind == TokenKind.LAngle && Peek(1).Kind == TokenKind.LBrace)
        {
            Advance(); // <
            var st = ParseStructType(true);
            Expect(TokenKind.RAngle);
            return st;
        }

        // [N x type] 数组
        if (tok.Kind == TokenKind.LBrack)
            return ParseArrayType();

        // <N x type> 或 <vscale x N x type> 向量
        if (tok.Kind == TokenKind.LAngle)
            return ParseVectorType();

        // %Name 命名结构体
        if (tok.Kind == TokenKind.LocalIdent)
        {
            Advance();
            var existing = _module.NamedTypes.FirstOrDefault(n => n.Name == tok.Text);
            return existing ?? new NamedStructType(tok.Text);
        }

        throw new FormatException($"无法解析类型: {tok} 在位置 {_pos}");
    }

    private StructType ParseStructType(bool packed)
    {
        Expect(TokenKind.LBrace);
        var types = new List<IrType>();
        if (!MatchKind(TokenKind.RBrace))
        {
            types.Add(ParseType());
            while (MatchKind(TokenKind.Comma))
            {
                Advance();
                types.Add(ParseType());
            }
        }
        Expect(TokenKind.RBrace);
        return new StructType(types, packed);
    }

    private ArrayType ParseArrayType()
    {
        Expect(TokenKind.LBrack);
        int len = (int)AdvanceInt();
        Expect(TokenKind.Ident); // "x"
        var elem = ParseType();
        Expect(TokenKind.RBrack);
        return new ArrayType(elem, len);
    }

    private VectorType ParseVectorType()
    {
        Expect(TokenKind.LAngle);
        bool scalable = false;
        if (Match("vscale"))
        {
            Advance();
            Expect(TokenKind.Ident);
            scalable = true;
        }
        int count = (int)AdvanceInt();
        Expect(TokenKind.Ident); // "x"
        var elem = ParseType();
        Expect(TokenKind.RAngle);
        return new VectorType(count, elem, scalable);
    }

    // ================================================================
    // 命名类型
    // ================================================================

    private void ParseNamedType()
    {
        var nameTok = Expect(TokenKind.LocalIdent); // %Name
        Expect(TokenKind.Eq); // =
        Expect(TokenKind.Ident); // type
        var body = ParseStructType(false);
        _module.AddNamedType(nameTok.Text, body);
    }

    // ================================================================
    // 全局变量
    // ================================================================

    private void ParseGlobalVar()
    {
        var nameTok = Expect(TokenKind.GlobalIdent);
        Expect(TokenKind.Eq);

        var linkage = LinkageType.External;
        bool unnamedAddr = false;
        var vis = VisibilityStyle.Default;

        if (IsLinkageKeyword())
            linkage = ParseLinkage();
        if (Match("unnamed_addr"))
        {
            Advance();
            unnamedAddr = true;
        }
        if (IsVisibilityKeyword())
            vis = ParseVisibility();

        bool isConst = false;
        if (Match("constant"))
        {
            Advance();
            isConst = true;
        }
        else if (Match("global"))
        {
            Advance();
        }
        else
            throw new FormatException($"期望 'constant' 或 'global'，实际为 {Peek()}");

        var vtype = ParseType();

        // 初始值
        Constant? init = null;
        if (!MatchKind(TokenKind.Comma))
        {
            init = ParseConstantExpr(vtype);
        }

        // , align N
        int alignment = 0;
        if (MatchKind(TokenKind.Comma))
        {
            Advance();
            if (Match("align"))
            {
                Advance();
                alignment = (int)AdvanceInt();
            }
        }

        var gv = new GlobalVariable(nameTok.Text, vtype, isConst, linkage, init)
        {
            Alignment = alignment,
            IsUnnamedAddr = unnamedAddr,
            Visibility = vis,
        };
        _module.AddGlobalVariable(gv);
    }

    // ================================================================
    // 函数声明
    // ================================================================

    private void ParseFunctionDecl()
    {
        Advance(); // declare

        if (IsLinkageKeyword())
            Advance();
        if (IsVisibilityKeyword())
            Advance();
        if (Match("cc"))
        {
            Advance();
            AdvanceInt();
        }
        if (Match("fastcc") || Match("coldcc"))
            Advance();

        var retType = ParseType();
        var funcName = AdvanceGlobalName();
        Expect(TokenKind.LParen);

        var (paramTypes, varArg) = ParseParamTypeList();
        Expect(TokenKind.RParen);

        SkipFuncTrailingAttrs();

        var ft = new FunctionType(retType, paramTypes, varArg);
        _module.AddFunction(new Function(funcName, ft));
    }

    // ================================================================
    // 函数定义
    // ================================================================

    private void ParseFunctionDef()
    {
        Advance(); // define
        _locals.Clear();
        _blocks.Clear();
        _unnamedBBCounter = 0;
        var linkage = LinkageType.External;
        var vis = VisibilityStyle.Default;
        var callingConv = CallingConv.C;

        if (IsLinkageKeyword())
            linkage = ParseLinkage();

        if (IsVisibilityKeyword())
            vis = ParseVisibility();

        // 解析调用约定并保存
        if (Match("cc"))
        {
            Advance();
            callingConv = (CallingConv)(int)AdvanceInt();
        }
        else if (Match("fastcc"))
        {
            Advance();
            callingConv = CallingConv.Fast;
        }
        else if (Match("coldcc"))
        {
            Advance();
            callingConv = CallingConv.Cold;
        }

        SkipInlineAttrs();

        var retType = ParseType();
        var funcName = AdvanceGlobalName();
        Expect(TokenKind.LParen);

        // 参数列表
        var paramTypes = new List<IrType>();
        var paramNames = new List<string>();
        bool varArg = false;

        if (!MatchKind(TokenKind.RParen))
        {
            ParseOneParam(paramTypes, paramNames);
            while (MatchKind(TokenKind.Comma))
            {
                Advance();
                if (MatchKind(TokenKind.Ellipsis))
                {
                    Advance();
                    varArg = true;
                    break;
                }
                ParseOneParam(paramTypes, paramNames);
            }
        }
        Expect(TokenKind.RParen);

        var ft = new FunctionType(retType, paramTypes, varArg);
        var func = new Function(funcName, ft, linkage);

        // 设置 visibility 和 calling convention
        func.Visibility = vis;
        func.CallingConvention = callingConv;

        for (int i = 0; i < paramNames.Count; i++)
        {
            func.Parameters[i].Name = paramNames[i];
            if (paramNames[i] != "")
                _locals["%" + paramNames[i]] = func.Parameters[i];
        }

        SkipFuncTrailingAttrs();

        // 函数级 metadata，如 !dbg !4
        while (MatchKind(TokenKind.Bang))
        {
            Advance(); // !
            string kind = Advance().Text; // dbg

            if (MatchKind(TokenKind.Bang))
            {
                Expect(TokenKind.Bang); // !
                int id = (int)AdvanceInt();

                if (_module.Metadata != null)
                {
                    var node = _module.Metadata.GetNode(id);
                    if (node != null)
                        func.SetMetadata(kind, node);
                }
            }
            else
            {
                break;
            }
        }

        _module.AddFunction(func);
        _currentFunc = func;

        // 函数体
        Expect(TokenKind.LBrace);
        while (!MatchKind(TokenKind.RBrace) && !IsEof)
            ParseBasicBlock();
        Expect(TokenKind.RBrace);

        ResolveForwardReferences();
        _currentFunc = null;
    }

    /// <summary>解析一个参数声明：Type [attrs] [%name]</summary>
    private void ParseOneParam(List<IrType> types, List<string> names)
    {
        var ty = ParseType();

        // 跳过参数属性
        while (!IsEof && MatchKind(TokenKind.Ident) && IsParamAttr())
        {
            Advance();
            // 处理 byval(i32) / dereferenceable(4) 之类带括号的属性
            if (MatchKind(TokenKind.LParen))
            {
                int depth = 0;
                do
                {
                    if (MatchKind(TokenKind.LParen))
                        depth++;
                    if (MatchKind(TokenKind.RParen))
                        depth--;
                    if (depth > 0)
                        Advance();
                } while (depth > 0 && !IsEof);
                if (!IsEof)
                    Advance(); // 消费最后的 )
            }
            // align N
            if (MatchKind(TokenKind.IntLit) && Peek(-1).Text == "align")
                AdvanceInt();
        }

        string name = "";
        if (MatchKind(TokenKind.LocalIdent))
            name = Advance().Text;
        types.Add(ty);
        names.Add(name);
    }

    /// <summary>解析函数参数类型列表（用于声明，不含名字）</summary>
    private (List<IrType> types, bool varArg) ParseParamTypeList()
    {
        var types = new List<IrType>();
        bool varArg = false;
        if (!MatchKind(TokenKind.RParen))
        {
            types.Add(ParseType());
            while (MatchKind(TokenKind.Comma))
            {
                Advance();
                if (MatchKind(TokenKind.Ellipsis))
                {
                    Advance();
                    varArg = true;
                    break;
                }
                types.Add(ParseType());
            }
        }
        return (types, varArg);
    }

    // ================================================================
    // 属性跳过辅助（修复：合并重复定义）
    // ================================================================

    /// <summary>判断当前 token 是否是参数属性关键字</summary>
    private bool IsParamAttr()
    {
        if (IsEof)
            return false;
        var t = _tokens[_pos].Text;
        return t
            is "signext"
                or "zeroext"
                or "noalias"
                or "nocapture"
                or "readonly"
                or "readnone"
                or "noundef"
                or "nonnull"
                or "byval"
                or "byref"
                or "sret"
                or "inalloca"
                or "inreg"
                or "nest"
                or "returned"
                or "dereferenceable"
                or "dereferenceable_or_null"
                or "align"
                or "immarg"
                or "swiftself"
                or "swifterror";
    }

    /// <summary>判断当前 token 是否是函数属性关键字</summary>
    private bool IsFuncAttrKeyword()
    {
        if (IsEof)
            return false;
        var t = _tokens[_pos].Text;
        return t
            is "noinline"
                or "nounwind"
                or "optnone"
                or "alwaysinline"
                or "naked"
                or "readnone"
                or "readonly"
                or "argmemonly"
                or "uwtable"
                or "ssp"
                or "sspreq"
                or "sanitize_address"
                or "sanitize_memory"
                or "sanitize_thread"
                or "mustprogress"
                or "willreturn"
                or "nosync"
                or "nofree"
                or "noundef"
                or "nonnull"
                or "dereferenceable";
    }

    /// <summary>跳过函数声明/定义前面的内联属性</summary>
    private void SkipInlineAttrs()
    {
        while (!IsEof && MatchKind(TokenKind.Ident) && IsFuncAttrKeyword())
            Advance();
    }

    /// <summary>跳过函数签名后的对齐和属性</summary>
    private void SkipFuncTrailingAttrs()
    {
        while (!IsEof && !MatchKind(TokenKind.LBrace))
        {
            if (Match("align"))
            {
                Advance();
                if (!IsEof && MatchKind(TokenKind.IntLit))
                    AdvanceInt();
                continue;
            }
            if (MatchKind(TokenKind.Ident) && IsFuncAttrKeyword())
            {
                Advance();
                continue;
            }
            if (MatchKind(TokenKind.IntLit))
            {
                Advance();
                continue;
            } // #0 属性组引用
            break;
        }
    }

    // ================================================================
    // 基本块
    // ================================================================

    private void ParseBasicBlock()
    {
        string bbName;

        if (MatchKind(TokenKind.LocalIdent) && Peek(1).Kind == TokenKind.Colon)
        {
            bbName = Advance().Text;
            Advance(); // :
        }
        else if (MatchKind(TokenKind.Ident) && Peek(1).Kind == TokenKind.Colon)
        {
            bbName = Advance().Text;
            Advance(); // :
        }
        else
        {
            if (_unnamedBBCounter == 0)
                bbName = "entry";
            else
                bbName = $"__bb_{_unnamedBBCounter}";
            _unnamedBBCounter++;
        }

        if (!_blocks.ContainsKey(bbName))
        {
            var bb = new BasicBlock(bbName);
            _currentFunc!.AppendBasicBlock(bb);
            _blocks[bbName] = bb;
        }
        _currentBB = _blocks[bbName];

        while (!IsEof && !MatchKind(TokenKind.RBrace))
        {
            if (MatchKind(TokenKind.LocalIdent) && Peek(1).Kind == TokenKind.Colon)
                break;
            if (MatchKind(TokenKind.Ident) && Peek(1).Kind == TokenKind.Colon)
                break;
            ParseInstruction();
        }
    }

    // ================================================================
    // 指令解析
    // ================================================================

    private void ParseInstruction()
    {
        string? resultName = null;
        if (MatchKind(TokenKind.LocalIdent) && Peek(1).Kind == TokenKind.Eq)
        {
            resultName = Advance().Text;
            Advance(); // =
        }

        var keyword = Peek().Text;
        Instruction inst = keyword switch
        {
            "ret" => ParseRet(),
            "br" => ParseBr(),
            "switch" => ParseSwitch(),
            "unreachable" => (Advance(), new UnreachableInst()).Item2,
            "add"
            or "fadd"
            or "sub"
            or "fsub"
            or "mul"
            or "fmul"
            or "udiv"
            or "sdiv"
            or "fdiv"
            or "urem"
            or "srem"
            or "frem"
            or "shl"
            or "lshr"
            or "ashr"
            or "and"
            or "or"
            or "xor" => ParseBinaryOp(),
            "icmp" => ParseICmp(),
            "fcmp" => ParseFCmp(),
            "alloca" => ParseAlloca(),
            "load" => ParseLoad(),
            "store" => ParseStore(),
            "getelementptr" => ParseGEP(),
            "trunc"
            or "zext"
            or "sext"
            or "fptrunc"
            or "fpext"
            or "fptoui"
            or "fptosi"
            or "uitofp"
            or "sitofp"
            or "ptrtoint"
            or "inttoptr"
            or "bitcast"
            or "addrspacecast" => ParseCast(),
            "phi" => ParsePhi(),
            "select" => ParseSelect(),
            "call" or "tail" or "musttail" or "notail" => ParseCall(),
            "extractvalue" => ParseExtractValue(),
            "insertvalue" => ParseInsertValue(),
            _ => throw new FormatException($"未识别的指令: '{keyword}' 在位置 {_pos}"),
        };

        // 指令后可能跟着 , !dbg !N 或 !tbaa !M 等 metadata 附加项
        ParseInstructionMetadata(inst);

        if (resultName != null)
            inst.Name = resultName;
        _currentBB!.AddInstruction(inst);
        if (resultName != null && inst.ProducesResult)
            _locals["%" + resultName] = inst;
    }

    /// <summary>
    /// 解析指令尾部的 metadata 附加项。
    /// 格式: , !kind !N [, !kind2 !M] ...
    /// </summary>
    private void ParseInstructionMetadata(Instruction inst)
    {
        while (!IsEof)
        {
            if (
                MatchKind(TokenKind.Comma)
                && _pos + 1 < _tokens.Count
                && _tokens[_pos + 1].Kind == TokenKind.Bang
            )
            {
                Advance();
            }
            else if (MatchKind(TokenKind.Bang))
            {
                // 直接进入解析
            }
            else
            {
                break;
            }

            Expect(TokenKind.Bang);
            string kind = Advance().Text;

            if (MatchKind(TokenKind.Bang))
            {
                Expect(TokenKind.Bang);
                int id = (int)AdvanceInt();

                if (_module.Metadata != null)
                {
                    var node = _module.Metadata.GetNode(id);
                    if (node == null)
                    {
                        // metadata 尚未定义，创建占位节点
                        node = new MDTuple();
                        node.Id = id;
                        _module.Metadata.SetNode(id, node);
                    }
                    inst.SetMetadata(kind, node);
                }
            }
            else
            {
                break;
            }
        }
    }

    // ================================================================
    // 各指令解析方法
    // ================================================================

    private ReturnInst ParseRet()
    {
        Advance(); // ret
        if (Match("void"))
        {
            Advance();
            return new ReturnInst();
        }
        var ty = ParseType();
        var val = ParseValueRef(ty);
        return new ReturnInst(val);
    }

    private BranchInst ParseBr()
    {
        Advance(); // br
        if (Match("label"))
        {
            var dest = ParseBBRef();
            return new BranchInst(dest);
        }
        var condType = ParseType();
        var cond = ParseValueRef(condType);
        Expect(TokenKind.Comma);
        var trueBB = ParseBBRef();
        Expect(TokenKind.Comma);
        var falseBB = ParseBBRef();
        return new BranchInst(cond, trueBB, falseBB);
    }

    private SwitchInst ParseSwitch()
    {
        Advance(); // switch
        var condType = ParseType();
        var cond = ParseValueRef(condType);
        Expect(TokenKind.Comma);
        var defBB = ParseBBRef();
        Expect(TokenKind.LBrack);
        var sw = new SwitchInst(cond, defBB);
        while (!MatchKind(TokenKind.RBrack))
        {
            var caseType = ParseType();
            var caseVal = new ConstantInt(caseType, AdvanceInt());
            Expect(TokenKind.Comma);
            var caseDest = ParseBBRef();
            sw.AddCase(caseVal, caseDest);
        }
        Expect(TokenKind.RBrack);
        return sw;
    }

    private BinaryOperator ParseBinaryOp()
    {
        var opText = Advance().Text;
        bool nsw = false,
            nuw = false;
        while (Match("nsw") || Match("nuw"))
        {
            if (Advance().Text == "nsw")
                nsw = true;
            else
                nuw = true;
        }
        var op = opText switch
        {
            "add" => BinaryOp.Add,
            "fadd" => BinaryOp.FAdd,
            "sub" => BinaryOp.Sub,
            "fsub" => BinaryOp.FSub,
            "mul" => BinaryOp.Mul,
            "fmul" => BinaryOp.FMul,
            "udiv" => BinaryOp.UDiv,
            "sdiv" => BinaryOp.SDiv,
            "fdiv" => BinaryOp.FDiv,
            "urem" => BinaryOp.URem,
            "srem" => BinaryOp.SRem,
            "frem" => BinaryOp.FRem,
            "shl" => BinaryOp.Shl,
            "lshr" => BinaryOp.LShr,
            "ashr" => BinaryOp.AShr,
            "and" => BinaryOp.And,
            "or" => BinaryOp.Or,
            "xor" => BinaryOp.Xor,
            _ => throw new FormatException($"未知二元操作: {opText}"),
        };
        var ty = ParseType();
        var lhs = ParseValueRef(ty);
        Expect(TokenKind.Comma);
        var rhs = ParseValueRef(ty);
        return new BinaryOperator(op, lhs, rhs) { NoSignedWrap = nsw, NoUnsignedWrap = nuw };
    }

    private ICmpInst ParseICmp()
    {
        Advance(); // icmp
        var pred = ParseICmpPredicate();
        var ty = ParseType();
        var lhs = ParseValueRef(ty);
        Expect(TokenKind.Comma);
        var rhs = ParseValueRef(ty);
        return new ICmpInst(pred, lhs, rhs);
    }

    private ICmpPredicate ParseICmpPredicate() =>
        Advance().Text switch
        {
            "eq" => ICmpPredicate.Eq,
            "ne" => ICmpPredicate.Ne,
            "ugt" => ICmpPredicate.Ugt,
            "uge" => ICmpPredicate.Uge,
            "ult" => ICmpPredicate.Ult,
            "ule" => ICmpPredicate.Ule,
            "sgt" => ICmpPredicate.Sgt,
            "sge" => ICmpPredicate.Sge,
            "slt" => ICmpPredicate.Slt,
            "sle" => ICmpPredicate.Sle,
            var s => throw new FormatException($"未知 icmp 谓词: {s}"),
        };

    private FCmpInst ParseFCmp()
    {
        Advance(); // fcmp
        var pred = Advance().Text switch
        {
            "false" => FCmpPredicate.False,
            "true" => FCmpPredicate.True,
            "oeq" => FCmpPredicate.Oeq,
            "ogt" => FCmpPredicate.Ogt,
            "oge" => FCmpPredicate.Oge,
            "olt" => FCmpPredicate.Olt,
            "ole" => FCmpPredicate.Ole,
            "one" => FCmpPredicate.One,
            "ord" => FCmpPredicate.Ord,
            "ueq" => FCmpPredicate.Ueq,
            "ugt" => FCmpPredicate.Ugt,
            "uge" => FCmpPredicate.Uge,
            "ult" => FCmpPredicate.Ult,
            "ule" => FCmpPredicate.Ule,
            "une" => FCmpPredicate.Une,
            "uno" => FCmpPredicate.Uno,
            var s => throw new FormatException($"未知 fcmp 谓词: {s}"),
        };
        var ty = ParseType();
        var lhs = ParseValueRef(ty);
        Expect(TokenKind.Comma);
        var rhs = ParseValueRef(ty);
        return new FCmpInst(pred, lhs, rhs);
    }

    private AllocaInst ParseAlloca()
    {
        Advance(); // alloca
        var allocType = ParseType();

        Value? arrSize = null;
        int alignment = 0;

        // 第一个逗号: 可能是数组大小，也可能是 align，也可能是 metadata
        if (MatchKind(TokenKind.Comma))
        {
            Advance();

            if (Match("align"))
            {
                // alloca i32, align 4
                Advance();
                alignment = (int)AdvanceInt();
            }
            else if (MatchKind(TokenKind.Bang))
            {
                // alloca i32, !dbg !5 — metadata，回退逗号
                _pos--; // 回退到逗号之前的位置，让 ParseInstructionMetadata 处理
            }
            else
            {
                // alloca i32, i32 5 — 数组大小
                var sizeType = ParseType();
                arrSize = ParseValueRef(sizeType);

                // 第二个逗号: 可能是 align 或 metadata
                if (MatchKind(TokenKind.Comma))
                {
                    Advance();

                    if (Match("align"))
                    {
                        Advance();
                        alignment = (int)AdvanceInt();
                    }
                    else if (MatchKind(TokenKind.Bang))
                    {
                        _pos--;
                    }
                }
            }
        }

        return new AllocaInst(allocType, arrSize, alignment: alignment);
    }

    private LoadInst ParseLoad()
    {
        Advance(); // load
        bool volatile_ = false;
        if (Match("volatile"))
        {
            Advance();
            volatile_ = true;
        }
        var loadedType = ParseType();
        Expect(TokenKind.Comma);
        var ptrType = ParseType();
        var ptr = ParseValueRef(ptrType);

        int alignment = 0;
        if (MatchKind(TokenKind.Comma))
        {
            Advance();

            if (Match("align"))
            {
                Advance();
                alignment = (int)AdvanceInt();
            }
            else if (MatchKind(TokenKind.Bang))
            {
                _pos--;
            }
        }

        return new LoadInst(loadedType, ptr, alignment: alignment) { IsVolatile = volatile_ };
    }

    private StoreInst ParseStore()
    {
        Advance(); // store
        bool volatile_ = false;
        if (Match("volatile"))
        {
            Advance();
            volatile_ = true;
        }
        var valType = ParseType();
        var val = ParseValueRef(valType);
        Expect(TokenKind.Comma);
        var ptrType = ParseType();
        var ptr = ParseValueRef(ptrType);

        int alignment = 0;
        if (MatchKind(TokenKind.Comma))
        {
            Advance();

            if (Match("align"))
            {
                Advance();
                alignment = (int)AdvanceInt();
            }
            else if (MatchKind(TokenKind.Bang))
            {
                _pos--;
            }
        }

        return new StoreInst(val, ptr, alignment) { IsVolatile = volatile_ };
    }

    private GetElementPtrInst ParseGEP()
    {
        Advance(); // getelementptr
        bool inBounds = false;
        if (Match("inbounds"))
        {
            Advance();
            inBounds = true;
        }
        var elemType = ParseType();
        Expect(TokenKind.Comma);
        var ptrType = ParseType();
        var ptr = ParseValueRef(ptrType);

        var indices = new List<Value>();
        while (MatchKind(TokenKind.Comma))
        {
            // 预读下一个 token，如果是 ! 则是 metadata，不是索引
            if (_pos + 1 < _tokens.Count && _tokens[_pos + 1].Kind == TokenKind.Bang)
                break;

            Advance();
            var idxType = ParseType();
            indices.Add(ParseValueRef(idxType));
        }

        return new GetElementPtrInst(elemType, ptr, indices, inBounds: inBounds);
    }

    private CastInst ParseCast()
    {
        var opText = Advance().Text;
        var op = opText switch
        {
            "trunc" => CastOp.Trunc,
            "zext" => CastOp.ZExt,
            "sext" => CastOp.SExt,
            "fptrunc" => CastOp.FPTrunc,
            "fpext" => CastOp.FPExt,
            "fptoui" => CastOp.FPToUI,
            "fptosi" => CastOp.FPToSI,
            "uitofp" => CastOp.UIToFP,
            "sitofp" => CastOp.SIToFP,
            "ptrtoint" => CastOp.PtrToInt,
            "inttoptr" => CastOp.IntToPtr,
            "bitcast" => CastOp.BitCast,
            "addrspacecast" => CastOp.AddrSpaceCast,
            _ => throw new FormatException($"未知转换操作: {opText}"),
        };
        var srcType = ParseType();
        var src = ParseValueRef(srcType);
        Expect(TokenKind.Ident); // "to"
        var destType = ParseType();
        return new CastInst(op, src, destType);
    }

    private PHINode ParsePhi()
    {
        Advance(); // phi
        var ty = ParseType();
        var phi = new PHINode(ty);
        ParsePhiIncoming(phi);

        while (MatchKind(TokenKind.Comma))
        {
            // phi 的 [val, block] 后面如果紧跟逗号和 [ 则是下一个 incoming
            // 如果紧跟逗号和 ! 则是 metadata
            if (_pos + 1 < _tokens.Count && _tokens[_pos + 1].Kind == TokenKind.Bang)
                break;
            Advance();
            ParsePhiIncoming(phi);
        }

        return phi;
    }

    private void ParsePhiIncoming(PHINode phi)
    {
        Expect(TokenKind.LBrack);
        var val = ParseValueRef(phi.Type);
        Expect(TokenKind.Comma);
        var bb = ParseBBRef();
        Expect(TokenKind.RBrack);
        phi.AddIncoming(val, bb);
    }

    private SelectInst ParseSelect()
    {
        Advance(); // select
        var condType = ParseType();
        var cond = ParseValueRef(condType);
        Expect(TokenKind.Comma);
        var trueType = ParseType();
        var trueVal = ParseValueRef(trueType);
        Expect(TokenKind.Comma);
        var falseType = ParseType();
        var falseVal = ParseValueRef(falseType);
        return new SelectInst(cond, trueVal, falseVal);
    }

    private CallInst ParseCall()
    {
        bool tail = false;
        if (Match("tail"))
        {
            Advance();
            tail = true;
        }
        else if (Match("musttail"))
        {
            Advance();
            tail = true;
        }
        else if (Match("notail"))
        {
            Advance();
        }

        Advance(); // call

        if (Match("cc"))
        {
            Advance();
            AdvanceInt();
        }
        if (Match("fastcc") || Match("coldcc"))
            Advance();

        // 跳过返回属性
        while (Match("noundef") || Match("nonnull") || Match("signext") || Match("zeroext"))
            Advance();

        var retType = ParseType();

        // 函数引用
        Value callee;
        if (MatchKind(TokenKind.GlobalIdent))
        {
            var funcName = Advance().Text;
            callee = GetOrDeclareFunction(
                funcName,
                new FunctionType(retType, System.Array.Empty<IrType>())
            );
        }
        else
        {
            callee = ParseValueRef(IrType.Pointer);
        }

        Expect(TokenKind.LParen);
        var args = new List<Value>();
        var paramTypes = new List<IrType>();

        if (!MatchKind(TokenKind.RParen))
        {
            var aty = ParseType();
            paramTypes.Add(aty);
            args.Add(ParseValueRef(aty));
            while (MatchKind(TokenKind.Comma))
            {
                Advance();
                if (MatchKind(TokenKind.Ellipsis))
                {
                    Advance();
                    break;
                }
                var aty2 = ParseType();
                paramTypes.Add(aty2);
                args.Add(ParseValueRef(aty2));
            }
        }
        Expect(TokenKind.RParen);

        SkipCallAttrs();

        return new CallInst(new FunctionType(retType, paramTypes), callee, args)
        {
            IsTailCall = tail,
        };
    }

    private void SkipCallAttrs()
    {
        while (!IsEof && MatchKind(TokenKind.Ident))
        {
            var t = Peek().Text;
            if (
                t
                is "nounwind"
                    or "noinline"
                    or "readnone"
                    or "readonly"
                    or "willreturn"
                    or "mustprogress"
                    or "nosync"
                    or "nofree"
                    or "noundef"
                    or "nonnull"
            )
            {
                Advance();
                continue;
            }
            if (t is "align" or "dereferenceable")
            {
                Advance();
                if (MatchKind(TokenKind.LParen))
                {
                    Advance();
                    AdvanceInt();
                    Expect(TokenKind.RParen);
                }
                else if (MatchKind(TokenKind.IntLit))
                    AdvanceInt();
                continue;
            }
            break;
        }
    }

    private ExtractValueInst ParseExtractValue()
    {
        Advance(); // extractvalue
        var aggType = ParseType();
        var agg = ParseValueRef(aggType);
        var indices = new List<int>();
        while (MatchKind(TokenKind.Comma))
        {
            Advance();
            indices.Add((int)AdvanceInt());
        }
        return new ExtractValueInst(agg, indices);
    }

    private InsertValueInst ParseInsertValue()
    {
        Advance(); // insertvalue
        var aggType = ParseType();
        var agg = ParseValueRef(aggType);
        Expect(TokenKind.Comma);
        var valType = ParseType();
        var val = ParseValueRef(valType);
        var indices = new List<int>();
        while (MatchKind(TokenKind.Comma))
        {
            Advance();
            indices.Add((int)AdvanceInt());
        }
        return new InsertValueInst(agg, val, indices);
    }

    // ================================================================
    // 值引用和常量解析
    // ================================================================

    private Value ParseValueRef(IrType expectedType)
    {
        var tok = Peek();

        // 整数字面量
        if (tok.Kind == TokenKind.IntLit)
            return new ConstantInt(expectedType, long.Parse(Advance().Text));

        // 浮点字面量
        if (tok.Kind == TokenKind.FloatLit)
            return new ConstantFP(
                expectedType,
                double.Parse(Advance().Text, System.Globalization.CultureInfo.InvariantCulture)
            );

        // 关键字常量
        if (Match("null"))
        {
            Advance();
            return expectedType is PointerType
                ? new ConstantPointerNull()
                : new ConstantNull(expectedType);
        }
        if (Match("undef"))
        {
            Advance();
            return new UndefValue(expectedType);
        }
        if (Match("poison"))
        {
            Advance();
            return new PoisonValue(expectedType);
        }
        if (Match("true"))
        {
            Advance();
            return new ConstantInt(IrType.Int1, 1);
        }
        if (Match("false"))
        {
            Advance();
            return new ConstantInt(IrType.Int1, 0);
        }
        if (Match("zeroinitializer"))
        {
            Advance();
            return new ConstantAggregateZero(expectedType);
        }

        // 0x 十六进制浮点
        if (tok.Kind == TokenKind.Ident && tok.Text.StartsWith("0x"))
        {
            long bits = Convert.ToInt64(Advance().Text, 16);
            return new ConstantFP(expectedType, BitConverter.Int64BitsToDouble(bits));
        }

        // 数组常量 [ ... ]
        if (tok.Kind == TokenKind.LBrack)
            return ParseConstArray(expectedType as ArrayType);

        // 结构体常量 { ... }
        if (tok.Kind == TokenKind.LBrace)
            return ParseConstStructLiteral(expectedType);

        // c"string" 格式
        if (tok.Kind == TokenKind.Ident && tok.Text == "c" && Peek(1).Kind == TokenKind.StringLit)
        {
            Advance(); // c
            return new ConstantDataArray(ParseStringBytes(Advance().Text));
        }

        // 字符串字面量（不含 c 前缀）
        if (tok.Kind == TokenKind.StringLit)
            return new ConstantDataArray(ParseStringBytes(Advance().Text));

        // 局部变量引用
        if (tok.Kind == TokenKind.LocalIdent)
        {
            var name = "%" + Advance().Text;
            if (_locals.TryGetValue(name, out var val))
                return val;
            // 前向引用：创建占位符
            var placeholder = new Argument(expectedType, -1, _currentFunc!);
            placeholder.Name = name.TrimStart('%');
            _locals[name] = placeholder;
            return placeholder;
        }

        // 全局引用
        if (tok.Kind == TokenKind.GlobalIdent)
        {
            var globalName = Advance().Text;
            var func = _module.Functions.FirstOrDefault(f => f.Name == globalName);
            if (func != null)
                return func;
            var gv = _module.GlobalVariables.FirstOrDefault(g => g.Name == globalName);
            if (gv != null)
                return gv;
            return GetOrDeclareFunction(
                globalName,
                new FunctionType(expectedType, System.Array.Empty<IrType>())
            );
        }

        // 常量表达式 getelementptr
        if (Match("getelementptr"))
            return ParseConstExprGEP();

        // 常量表达式 cast
        if (MatchKeyword("bitcast", "inttoptr", "ptrtoint", "sext", "zext", "trunc"))
            return ParseConstExprCast();

        throw new FormatException($"无法解析值引用: {tok} 位置={_pos}");
    }

    private Constant ParseConstantExpr(IrType? expectedType)
    {
        var tok = Peek();

        if (Match("null"))
        {
            Advance();
            return expectedType is PointerType
                ? new ConstantPointerNull()
                : new ConstantNull(expectedType ?? IrType.Pointer);
        }
        if (Match("undef"))
        {
            Advance();
            return new UndefValue(expectedType ?? IrType.Int32);
        }
        if (Match("poison"))
        {
            Advance();
            return new PoisonValue(expectedType ?? IrType.Int32);
        }
        if (Match("zeroinitializer"))
        {
            Advance();
            return new ConstantAggregateZero(expectedType ?? IrType.Int32);
        }
        if (Match("true"))
        {
            Advance();
            return new ConstantInt(IrType.Int1, 1);
        }
        if (Match("false"))
        {
            Advance();
            return new ConstantInt(IrType.Int1, 0);
        }

        if (tok.Kind == TokenKind.IntLit)
            return new ConstantInt(expectedType ?? IrType.Int32, long.Parse(Advance().Text));
        if (tok.Kind == TokenKind.FloatLit)
            return new ConstantFP(
                expectedType ?? IrType.Double,
                double.Parse(Advance().Text, System.Globalization.CultureInfo.InvariantCulture)
            );

        if (tok.Kind == TokenKind.Ident && tok.Text.StartsWith("0x"))
        {
            long bits = Convert.ToInt64(Advance().Text, 16);
            return new ConstantFP(
                expectedType ?? IrType.Double,
                BitConverter.Int64BitsToDouble(bits)
            );
        }

        // c"string"
        if (tok.Kind == TokenKind.Ident && tok.Text == "c" && Peek(1).Kind == TokenKind.StringLit)
        {
            Advance();
            return new ConstantDataArray(ParseStringBytes(Advance().Text));
        }
        if (tok.Kind == TokenKind.StringLit)
            return new ConstantDataArray(ParseStringBytes(Advance().Text));

        if (tok.Kind == TokenKind.LBrack)
            return ParseConstArray(expectedType as ArrayType);
        if (tok.Kind == TokenKind.LBrace)
            return ParseConstStructLiteral(expectedType);

        throw new FormatException($"无法解析常量: {tok}");
    }

    private ConstantArray ParseConstArray(ArrayType? arrType)
    {
        Expect(TokenKind.LBrack);
        var values = new List<Constant>();
        IrType? elemType = null;
        if (!MatchKind(TokenKind.RBrack))
        {
            var ty = ParseType();
            elemType ??= ty;
            values.Add(ParseTypedConst(ty));
            while (MatchKind(TokenKind.Comma))
            {
                Advance();
                var ty2 = ParseType();
                values.Add(ParseTypedConst(ty2));
            }
        }
        Expect(TokenKind.RBrack);
        var at = arrType ?? new ArrayType(elemType ?? IrType.Int8, values.Count);
        return new ConstantArray(at, values);
    }

    private Constant ParseTypedConst(IrType ty)
    {
        if (Match("null"))
        {
            Advance();
            return new ConstantNull(ty);
        }
        if (Match("undef"))
        {
            Advance();
            return new UndefValue(ty);
        }
        if (Match("zeroinitializer"))
        {
            Advance();
            return new ConstantAggregateZero(ty);
        }
        if (ty is IntegerType)
            return new ConstantInt(ty, AdvanceInt());
        if (ty is FloatType)
            return new ConstantFP(
                ty,
                double.Parse(Advance().Text, System.Globalization.CultureInfo.InvariantCulture)
            );
        return ParseConstantExpr(ty);
    }

    private Constant ParseConstStructLiteral(IrType? structType)
    {
        Expect(TokenKind.LBrace);
        var values = new List<Constant>();
        if (!MatchKind(TokenKind.RBrace))
        {
            var ty = ParseType();
            values.Add(ParseTypedConst(ty));
            while (MatchKind(TokenKind.Comma))
            {
                Advance();
                var ty2 = ParseType();
                values.Add(ParseTypedConst(ty2));
            }
        }
        Expect(TokenKind.RBrace);
        var st = structType as StructType ?? new StructType(values.Select(v => v.Type).ToArray());
        return new ConstantStruct(st, values);
    }

    private Value ParseConstExprGEP()
    {
        Advance(); // getelementptr
        bool inBounds = false;
        if (Match("inbounds"))
        {
            Advance();
            inBounds = true;
        }
        var elemType = ParseType();
        Expect(TokenKind.Comma);
        var ptrType = ParseType();
        var ptr = ParseValueRef(ptrType);
        var indices = new List<Value>();
        while (MatchKind(TokenKind.Comma))
        {
            Advance();
            var idxType = ParseType();
            indices.Add(ParseValueRef(idxType));
        }
        return new GetElementPtrInst(elemType, ptr, indices, inBounds: inBounds);
    }

    private Value ParseConstExprCast()
    {
        var opText = Advance().Text;
        var op = opText switch
        {
            "bitcast" => CastOp.BitCast,
            "inttoptr" => CastOp.IntToPtr,
            "ptrtoint" => CastOp.PtrToInt,
            "sext" => CastOp.SExt,
            "zext" => CastOp.ZExt,
            "trunc" => CastOp.Trunc,
            _ => throw new FormatException($"未实现的常量表达式: {opText}"),
        };
        var srcType = ParseType();
        var src = ParseValueRef(srcType);
        Expect(TokenKind.Ident); // "to"
        var destType = ParseType();
        return new CastInst(op, src, destType);
    }

    // ================================================================
    // 前向引用解析
    // ================================================================

    private void ResolveForwardReferences()
    {
        foreach (var bb in _currentFunc!.BasicBlocks)
        {
            foreach (var inst in bb.Instructions)
            {
                for (int i = 0; i < inst.Operands.Count; i++)
                {
                    var operand = inst.Operands[i];
                    if (operand is Argument arg && arg.Index == -1 && arg.Name != null)
                    {
                        var key = "%" + arg.Name;
                        if (_locals.TryGetValue(key, out var resolved) && resolved != operand)
                        {
                            inst.SetOperand(i, resolved);
                            if (inst is PHINode phi && i % 2 == 0)
                            {
                                int incomingIdx = i / 2;
                                if (incomingIdx < phi.Incoming.Count)
                                    phi.ReplaceIncomingValue(incomingIdx, resolved);
                            }
                        }
                    }
                }
            }
        }
    }

    // ================================================================
    // 辅助方法
    // ================================================================

    private BasicBlock ParseBBRef()
    {
        if (Match("label"))
            Advance();
        Expect(TokenKind.LocalIdent);
        var name = Peek(-1).Text;
        if (!_blocks.TryGetValue(name, out var bb))
        {
            bb = new BasicBlock(name);
            _currentFunc!.AppendBasicBlock(bb);
            _blocks[name] = bb;
        }
        return bb;
    }

    private bool MatchKeyword(params string[] keywords) =>
        !IsEof && _tokens[_pos].Kind == TokenKind.Ident && keywords.Contains(_tokens[_pos].Text);

    private bool IsLinkageKeyword()
    {
        if (IsEof)
            return false;
        var t = _tokens[_pos].Text;
        return t
            is "internal"
                or "private"
                or "external"
                or "weak"
                or "weak_any"
                or "linkonce"
                or "linkonce_odr"
                or "weak_odr"
                or "common"
                or "appending"
                or "available_externally"
                or "extern_weak";
    }

    private bool IsVisibilityKeyword()
    {
        if (IsEof)
            return false;
        return _tokens[_pos].Text is "default" or "hidden" or "protected";
    }

    private LinkageType ParseLinkage() =>
        Advance().Text switch
        {
            "internal" => LinkageType.Internal,
            "private" => LinkageType.Private,
            "external" => LinkageType.External,
            "weak" or "weak_any" => LinkageType.WeakAny,
            "linkonce" => LinkageType.Linkonce,
            "linkonce_odr" => LinkageType.LinkonceODR,
            "weak_odr" => LinkageType.WeakODR,
            "common" => LinkageType.Common,
            "appending" => LinkageType.Appending,
            "available_externally" => LinkageType.AvailableExternally,
            "extern_weak" => LinkageType.ExternalWeak,
            _ => LinkageType.External,
        };

    private VisibilityStyle ParseVisibility() =>
        Advance().Text switch
        {
            "hidden" => VisibilityStyle.Hidden,
            "protected" => VisibilityStyle.Protected,
            _ => VisibilityStyle.Default,
        };

    /// <summary>解析字符串字面量为字节数组（处理 \XX 十六进制转义，不自动追加 null）</summary>
    private static byte[] ParseStringBytes(string raw)
    {
        var bytes = new List<byte>();
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 2 < raw.Length)
            {
                var hex = raw.Substring(i + 1, 2);
                bytes.Add(Convert.ToByte(hex, 16));
                i += 2;
            }
            else
            {
                bytes.Add((byte)raw[i]);
            }
        }
        return bytes.ToArray();
    }

    private Function GetOrDeclareFunction(string name, FunctionType ft)
    {
        var existing = _module.Functions.FirstOrDefault(f => f.Name == name);
        if (existing != null)
            return existing;
        var func = new Function(name, ft);
        _module.AddFunction(func);
        return func;
    }
}
