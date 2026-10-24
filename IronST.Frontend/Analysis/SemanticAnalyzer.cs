using IronST.Common.Diagnostics;
using IronST.Common.Symbol;
using IronST.Common.Types;
using IronST.Frontend.AST;

namespace IronST.Frontend.Analysis;

public class SemanticAnalyzer
{
    private SymbolTable _globalScope = null!;
    private SymbolTable _currentScope = null!;
    private readonly DiagnosticBag _bag = new();
    private string? _currentFunctionName;
    private IECType? _currentReturnType;
    private string? _currentPOUName;

    // 当前循环嵌套深度，用于校验 EXIT 是否合法
    private int _loopDepth;

    // 收集容器
    private readonly List<VariableInfo> _collectedVariables = new();
    private readonly List<POUInfo> _collectedPOUs = new();
    private readonly List<TypeInfo> _collectedTypes = new();
    private readonly List<SymbolReference> _collectedRefs = new();
    private readonly List<CallSite> _collectedCalls = new();

    // 外部注入的库符号名。用户声明可覆盖它们（库是环境兜底，用户定义优先）。
    private readonly HashSet<string> _injectedSymbols = new(StringComparer.OrdinalIgnoreCase);

    // 已声明但布局尚未解析完成的自定义类型名（F7 两趟注册的中间状态）。
    private readonly HashSet<string> _pendingTypeNames = new(StringComparer.OrdinalIgnoreCase);

    // 已报过「未知的类型」的类型节点。唯一性按<b>声明节点</b>判定，不是按诊断文案 ——
    // 同名但位于不同源码位置的错误各自保留，只有同一处声明不会重复报告。
    private readonly HashSet<DataTypeNode> _reportedUnknownTypeNodes = new();

    // ═══════════════════════════════════════
    //  入口
    // ═══════════════════════════════════════

    public AnalysisResult Analyze(
        CompilationUnitNode ast,
        IReadOnlyList<ExternalLibrary>? libraries = null
    )
    {
        _globalScope = new SymbolTable();
        _currentScope = _globalScope;
        _bag.Clear();
        _currentFunctionName = null;
        _currentReturnType = null;
        _currentPOUName = null;
        _loopDepth = 0;

        _collectedVariables.Clear();
        _collectedPOUs.Clear();
        _collectedTypes.Clear();
        _collectedRefs.Clear();
        _collectedCalls.Clear();
        _injectedSymbols.Clear();
        _pendingTypeNames.Clear();
        _reportedUnknownTypeNodes.Clear();

        // ── 注入外部库（取代原 RegisterBuiltinFunctions） ──
        if (libraries != null)
            foreach (var lib in libraries)
                RegisterExternalLibrary(lib);

        // 第一遍：先声明所有自定义类型的名字，使其互相可见
        foreach (var td in ast.TypeDeclarations)
            PredeclareTypeDecl(td);

        // 第二遍：迭代解析类型布局到不动点（F7 两趟注册）
        ResolveTypeDeclarations(ast.TypeDeclarations);

        // 第三遍：注册 POU 签名
        foreach (var pou in ast.POUs)
            RegisterPOU(pou);

        // 第四遍：分析 POU 体
        foreach (var pou in ast.POUs)
            AnalyzePOU(pou);

        return new AnalysisResult
        {
            AST = ast,
            Symbols = _globalScope,
            Diagnostics = _bag.Diagnostics.ToList(),
            Variables = _collectedVariables.ToList(),
            POUs = _collectedPOUs.ToList(),
            CustomTypes = _collectedTypes.ToList(),
            References = _collectedRefs.ToList(),
            CallSites = _collectedCalls.ToList(),
        };
    }

    // ═══════════════════════════════════════
    //  符号声明（F8：重复声明必须可见）
    // ═══════════════════════════════════════

    /// <summary>
    /// 向指定作用域声明符号；重名时报 E2002（重复声明），并引用先前声明的位置。
    ///
    /// <para>
    /// <b>F8 背景</b>：此前 <see cref="SymbolTable.TryDeclare"/> 的返回值在全部 10 个调用点
    /// 被丢弃，重复的函数/类型/全局变量/局部变量声明会被静默吞掉 —— 第一个声明生效、
    /// 后续同名声明全部消失且零诊断。这使增量编译契约 A2「重复定义导出符号」在整体编译
    /// 路径下完全不可观测。
    /// </para>
    /// <para>
    /// <paramref name="tolerant"/> = true 用于外部接口注入（<c>RegisterExternalLibrary</c>）
    /// 与 VAR_CONFIG 配置绑定：前者同一份接口可被多个编译单元重复注入，后者是对已声明
    /// 变量的配置而非新声明 —— 二者重名均不视为错误。
    /// </para>
    /// <para>
    /// 用户声明若与库注入符号重名，以用户为准（覆盖而非报错）。此前用户重定义标准库
    /// 函数时，函数体会被静默丢弃、调用却解析到库声明上。
    /// </para>
    /// </summary>
    private bool Declare(SymbolTable scope, Symbol symbol, SourceSpan span, bool tolerant = false)
    {
        if (scope.TryDeclare(symbol, out var existing)) return true;

        if (tolerant) return false;

        // 用户声明覆盖外部注入的库符号
        if (existing is not null && _injectedSymbols.Remove(existing.Name))
        {
            scope.Replace(symbol);
            return true;
        }

        var prev =
            existing is { Declaration.Line: > 0 }
                ? $"（先前声明于 {existing.Declaration}）"
                : "";
        _bag.Error($"重复声明 '{symbol.Name}'{prev}", span, ErrorCode.E2002);
        return false;
    }

    /// <summary>
    /// 报告「未知的类型」，同一处声明至多一次（④）。
    ///
    /// <para>
    /// 同一个 <see cref="DataTypeNode"/> 会被两趟各遍历一次：<see cref="RegisterPOU"/> 走
    /// <c>CollectParameters</c>（函数签名需要参数类型），<see cref="AnalyzePOU"/> 走自身
    /// 的 VarBlocks（要把变量声明进作用域）。两趟都调 <c>ResolveDataTypeNode</c>，于是
    /// 函数参数的未知类型此前报 2 次。
    /// </para>
    /// <para>
    /// 唯一性按<b>节点实例</b>判定而非诊断文案：不同源码位置、哪怕文案完全相同，也各自
    /// 报告，不会被吞掉。
    /// </para>
    /// </summary>
    private void ReportUnknownType(DataTypeNode node, string name)
    {
        if (!_reportedUnknownTypeNodes.Add(node))
            return;
        _bag.Error($"未知的类型 '{name}'", node.Span, ErrorCode.E2015);
    }

    // ═══════════════════════════════════════
    //  内置函数注册
    // ═══════════════════════════════════════

    private void RegisterExternalLibrary(ExternalLibrary library)
    {
        // ── 注册类型 ──
        foreach (var t in library.Types)
        {
            IECType type = t.Kind switch
            {
                TypeKind.Struct when t.Fields != null => IECType.MakeStruct(
                    t.Name,
                    t.Fields.ToDictionary(
                        f => f.Name,
                        f => ResolveTypeName(f.TypeName) ?? IECType.DInt,
                        StringComparer.OrdinalIgnoreCase
                    )
                ),

                TypeKind.Enum when t.EnumMembers != null => IECType.MakeEnum(
                    t.Name,
                    t.EnumMembers,
                    t.EnumMemberValues!
                ),

                TypeKind.Subrange => IECType.LookupBuiltin(t.UnderlyingType ?? "") ?? IECType.DInt,

                TypeKind.SimpleType => ResolveTypeName(t.UnderlyingType ?? "") ?? IECType.DInt,

                _ => IECType.DInt,
            };

            var typeDeclared = Declare(
                _globalScope,
                new TypeSymbol
                {
                    Name = t.Name,
                    Declaration = SourceSpan.Unknown,
                    Type = type,
                },
                SourceSpan.Unknown,
                tolerant: true
            );

            if (typeDeclared) _injectedSymbols.Add(t.Name);

            // 重复注入同一份接口时，仅保留首个条目，避免下游重复发射
            if (!typeDeclared) continue;

            _collectedTypes.Add(
                new TypeInfo(
                    Name: t.Name,
                    Kind: t.Kind,
                    Fields: t.Fields?.Select(f => new FieldInfo(f.Name, f.TypeName, null, 0))
                        .ToList(),
                    EnumMembers: t.EnumMembers,
                    EnumMemberValues: t.EnumMemberValues,
                    UnderlyingType: t.UnderlyingType,
                    FilePath: null,
                    Line: 0
                )
            );

            _collectedRefs.Add(
                new SymbolReference(
                    SymbolName: t.Name,
                    Kind: SymbolKind.Type,
                    Usage: ReferenceUsage.Declaration,
                    POUName: null,
                    FilePath: null,
                    Line: 0,
                    Column: 0
                )
            );
        }

        // ── 注册函数 ──
        foreach (var fn in library.Functions)
        {
            var declared = Declare(
                _globalScope,
                new FunctionSymbol
                {
                    Name = fn.Name,
                    Declaration = SourceSpan.Unknown,
                    ReturnType = ResolveTypeName(fn.ReturnTypeName ?? "") ?? IECType.Void,
                    Parameters = fn
                        .Parameters.Select(p => new VariableSymbol
                        {
                            Name = p.Name,
                            Type = ResolveTypeName(p.TypeName) ?? IECType.DInt,
                            Scope = VarScope.Input,
                        })
                        .ToList(),
                },
                SourceSpan.Unknown,
                tolerant: true
            );

            if (declared) _injectedSymbols.Add(fn.Name);

            // 重复注入同一份接口时，仅保留首个 POU 条目，避免下游重复发射
            if (!declared) continue;

            _collectedPOUs.Add(
                new POUInfo(
                    Name: fn.Name,
                    Kind: POUKind.Function,
                    ReturnTypeName: fn.ReturnTypeName,
                    Parameters: fn.Parameters.Select(p => new ParameterInfo(
                            Name: p.Name,
                            TypeName: p.TypeName,
                            Direction: "VAR_INPUT",
                            FilePath: null,
                            Line: 0
                        ))
                        .ToList(),
                    FilePath: null,
                    Line: 0,
                    IsExternal: true // ← 加这个
                )
            );

            _collectedRefs.Add(
                new SymbolReference(
                    SymbolName: fn.Name,
                    Kind: SymbolKind.Function,
                    Usage: ReferenceUsage.Declaration,
                    POUName: null,
                    FilePath: null,
                    Line: 0,
                    Column: 0
                )
            );
        }

        // ── 注册 FunctionBlock ──
        foreach (var fb in library.FunctionBlocks)
        {
            var members = new Dictionary<string, VariableSymbol>(StringComparer.OrdinalIgnoreCase);

            void AddMembers(List<ExternalParameter> ps, VarScope scope)
            {
                foreach (var p in ps)
                    members[p.Name] = new VariableSymbol
                    {
                        Name = p.Name,
                        Type = ResolveTypeName(p.TypeName) ?? IECType.DInt,
                        Scope = scope,
                    };
            }

            AddMembers(fb.InputVars, VarScope.Input);
            AddMembers(fb.OutputVars, VarScope.Output);
            AddMembers(fb.InOutVars, VarScope.InOut);

            foreach (var (name, typeName) in fb.InternalVars)
                members[name] = new VariableSymbol
                {
                    Name = name,
                    Type = ResolveTypeName(typeName) ?? IECType.DInt,
                    Scope = VarScope.Local,
                };

            var fbDeclared = Declare(
                _globalScope,
                new FunctionBlockSymbol
                {
                    Name = fb.Name,
                    Declaration = SourceSpan.Unknown,
                    Members = members,
                },
                SourceSpan.Unknown,
                tolerant: true
            );

            if (fbDeclared) _injectedSymbols.Add(fb.Name);

            List<ParameterInfo> BuildParams()
            {
                var list = new List<ParameterInfo>();
                void Add(List<ExternalParameter> ps, string dir)
                {
                    foreach (var p in ps)
                        list.Add(new ParameterInfo(p.Name, p.TypeName, dir, null, 0));
                }
                Add(fb.InputVars, "VAR_INPUT");
                Add(fb.OutputVars, "VAR_OUTPUT");
                Add(fb.InOutVars, "VAR_IN_OUT");
                return list;
            }

            // 重复注入同一份接口时，仅保留首个条目，避免下游重复发射
            if (fbDeclared)
            {
                _collectedPOUs.Add(
                    new POUInfo(
                        Name: fb.Name,
                        Kind: POUKind.FunctionBlock,
                        ReturnTypeName: null,
                        Parameters: BuildParams(),
                        FilePath: null,
                        Line: 0,
                        IsExternal: true // ← 加这个
                    )
                );

                _collectedRefs.Add(
                    new SymbolReference(
                        SymbolName: fb.Name,
                        Kind: SymbolKind.FunctionBlock,
                        Usage: ReferenceUsage.Declaration,
                        POUName: null,
                        FilePath: null,
                        Line: 0,
                        Column: 0
                    )
                );
            }
        }
    }

    // ═══════════════════════════════════════
    //  类型注册
    // ═══════════════════════════════════════

    /// <summary>
    /// 第一趟：只登记类型名，使同一批 TYPE 声明互相可见。
    ///
    /// <para>
    /// F7：此前类型在声明处立即解析字段类型，导致前向引用按声明顺序误报 E2015
    /// （尤其在多文件合并编译时，跨文件的 STRUCT 字段类型必然踩到）。改为先声明
    /// 名字、再统一解析布局。
    /// </para>
    /// </summary>
    private void PredeclareTypeDecl(TypeDeclNode td)
    {
        if (Declare(
            _globalScope,
            new TypeSymbol { Name = td.Name, Declaration = td.Span, Type = IECType.Void },
            td.Span
        ))
        {
            _pendingTypeNames.Add(td.Name);
        }
    }

    /// <summary>
    /// 第二趟：迭代解析类型布局到不动点。
    ///
    /// <para>
    /// 每轮只解析"字段布局已就绪"的类型；一轮下来若无进展，剩余的即为按值递归
    /// 布局（A 内含 B、B 又内含 A），无法确定内存布局 —— 报 E2016。
    /// </para>
    /// <para>
    /// 引用边（REFERENCE TO）不要求目标类型布局就绪，因此只有值布局环会卡住，
    /// 与增量编译契约 M5 的递归分类一致：函数调用循环与引用型类型循环合法，
    /// 按值递归布局非法。
    /// </para>
    /// </summary>
    private void ResolveTypeDeclarations(IReadOnlyList<TypeDeclNode> decls)
    {
        var remaining = new List<TypeDeclNode>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var td in decls)
        {
            // 重复声明已在预声明时报 E2002，不参与解析
            if (!_pendingTypeNames.Contains(td.Name)) continue;
            if (!seen.Add(td.Name)) continue; // 同名只取首个
            remaining.Add(td);
        }

        bool progress;
        do
        {
            progress = false;
            foreach (var td in remaining.ToList())
            {
                if (!IsTypeLayoutReady(td)) continue;

                // 回填占位符的实际类型
                if (_globalScope.Resolve(td.Name) is TypeSymbol ts)
                    ts.Type = BuildTypeDecl(td);

                _pendingTypeNames.Remove(td.Name);
                CollectTypeInfo(td);
                remaining.Remove(td);
                progress = true;
            }
        } while (progress);

        foreach (var td in remaining)
        {
            _bag.Error(
                $"类型 '{td.Name}' 存在按值递归布局，无法确定其内存布局",
                td.Span,
                ErrorCode.E2016
            );
            // 移出待解析集，避免影响后续阶段
            _pendingTypeNames.Remove(td.Name);
        }

        // 不动点已收敛：回填引用边的目标类型（缺陷③）
        BackfillReferenceTargets();
    }

    /// <summary>
    /// 回填 <c>REFERENCE TO</c> 的目标类型（缺陷③）。
    ///
    /// <para>
    /// <b>成因</b>：F7 的类型布局不动点里，引用边（<c>REFERENCE TO</c>）**不阻塞**
    /// —— 这是增量编译契约 M5 要求的正确行为（引用型递归布局合法）。代价是：引用
    /// 目标可能在该结构体被解析时仍是预声明占位符 <see cref="IECType.Void"/>，
    /// 于是 <c>REF_TO VOID</c> 被永久烘焙进字段类型。
    /// </para>
    ///
    /// <para>
    /// <b>修复</b>：不动点收敛后做一次回填。此时所有类型都已定型，按名字把占位符
    /// 换成真实目标。结构体走「就地改字段字典」，因此所有持有该结构体实例的地方
    /// （符号表、代码生成侧注册的类型表）自动看到修正后的字段类型，无需重建。
    /// </para>
    ///
    /// <para>
    /// <b>互递归无构造顺序依赖</b>：<c>A.p : REFERENCE TO B</c> 与
    /// <c>B.q : REFERENCE TO A</c> 同时存在时，回填只改「指向谁」，不递归进目标
    /// 结构体内部，因此两个方向都能各自拿到对方的真实类型。
    /// </para>
    /// </summary>
    private void BackfillReferenceTargets()
    {
        foreach (var sym in _globalScope.Locals.Values)
        {
            if (sym is not TypeSymbol ts) continue;
            ts.Type = PatchReferenceTargets(ts.Type);
        }
    }

    /// <summary>
    /// 就地修补一个类型里的引用目标。
    ///
    /// <para>
    /// 返回修补后的类型：结构体原地改字段字典（返回<b>同一实例</b>），数组与引用
    /// 需要重建（返回新实例）。
    /// </para>
    /// </summary>
    private IECType PatchReferenceTargets(IECType type) =>
        type.Kind switch
        {
            IECType.KindTag.REFERENCE => PatchReferenceTarget(type),
            IECType.KindTag.ARRAY => PatchArrayReferenceTargets(type),
            IECType.KindTag.STRUCT => PatchStructReferenceTargets(type),
            _ => type,
        };

    /// <summary>
    /// 把引用类型的占位符目标换成真实目标。
    /// </summary>
    private IECType PatchReferenceTarget(IECType reference)
    {
        // 目标名缺失（老式构造）—— 无从回填
        if (string.IsNullOrEmpty(reference.ReferenceTargetName)) return reference;

        // 目标已定型（非占位符）—— 不动。含 E2015 错误恢复的
        // `REF_TO DInt` 退路：那是「名字确实不存在」，不是前向引用。
        if (reference.ReferencedType is { Kind: not IECType.KindTag.VOID }) return reference;

        var resolved = ResolveTypeName(reference.ReferenceTargetName!);

        // 目标仍无布局（按值递归布局报错 E2016）—— 保持占位符
        if (resolved is null || resolved.Kind == IECType.KindTag.VOID) return reference;

        return IECType.MakeReference(resolved, reference.ReferenceTargetName);
    }

    /// <summary>
    /// 修补数组元素类型里的引用目标（<c>ARRAY[..] OF REFERENCE TO T</c>）。
    /// </summary>
    private IECType PatchArrayReferenceTargets(IECType array)
    {
        if (array.ElementType is not { } element) return array;
        var patched = PatchReferenceTargets(element);
        if (ReferenceEquals(patched, element)) return array;
        return IECType.MakeArray(patched, array.Dimensions ?? new List<(int, int)>());
    }

    /// <summary>
    /// 修补结构体字段里的引用目标。
    ///
    /// <para>
    /// 字段字典是<b>就地</b>改的（不重建结构体实例），因此嵌套持有该结构体的
    /// 地方（别的结构体字段、变量符号、代码生成侧的类型表）自动同步。
    /// </para>
    /// </summary>
    private IECType PatchStructReferenceTargets(IECType structType)
    {
        if (structType.Fields is not { } fields) return structType;
        foreach (var key in fields.Keys.ToList())
        {
            var current = fields[key];
            var patched = PatchReferenceTargets(current);
            if (!ReferenceEquals(patched, current))
                fields[key] = patched;
        }
        return structType;
    }

    /// <summary>判断类型定义的字段布局是否已可解析。</summary>
    private bool IsTypeLayoutReady(TypeDeclNode td) =>
        td.Definition switch
        {
            StructDefNode sd => sd.Members.All(m => IsLayoutReady(m.TypeName)),
            SimpleTypeDefNode st => IsLayoutReady(st.UnderlyingType),
            // 枚举成员值来自字面量、子范围仅基于内建类型，均无布局依赖
            _ => true,
        };

    /// <summary>
    /// 判断一个数据类型节点的布局是否已就绪。
    ///
    /// 值布局边（直接字段、数组元素）要求目标类型的完整布局，必须等其解析完成；
    /// 引用边（REFERENCE TO）只需名字即可表示，不阻塞。
    /// </summary>
    private bool IsLayoutReady(DataTypeNode? node) =>
        node switch
        {
            // 带长度参数的是 STRING/WSTRING，与自定义类型无关
            SimpleDataTypeNode s => s.SizeParam != null || !_pendingTypeNames.Contains(s.TypeName),
            ArrayDataTypeNode a => IsLayoutReady(a.ElementType),
            ReferenceDataTypeNode => true,
            _ => true,
        };

    /// <summary>枚举成员值预计算。</summary>
    private static Dictionary<string, long> ComputeEnumValues(EnumDefNode ed)
    {
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long nextValue = 0;
        foreach (var item in ed.Items)
        {
            long value;
            if (item.ExplicitValue is LiteralExprNode lit && lit.ParsedValue is long lv)
                value = lv;
            else
                value = nextValue;

            values[item.Name] = value;
            nextValue = value + 1;
        }
        return values;
    }

    /// <summary>由类型声明构造 IECType（须在布局就绪后调用）。</summary>
    private IECType BuildTypeDecl(TypeDeclNode td)
    {
        var enumMemberValues =
            td.Definition is EnumDefNode enumDef ? ComputeEnumValues(enumDef) : null;

        return td.Definition switch
        {
            StructDefNode sd => RegisterStructType(td.Name, sd),
            EnumDefNode ed => IECType.MakeEnum(
                td.Name,
                ed.Items.Select(i => i.Name).ToList(),
                enumMemberValues!
            ),
            SubrangeDefNode sr => IECType.LookupBuiltin(sr.BaseTypeName) ?? IECType.DInt,
            SimpleTypeDefNode st => ResolveDataTypeNode(st.UnderlyingType) ?? IECType.DInt,
            _ => IECType.DInt,
        };
    }

    /// <summary>收集 AnalysisResult.CustomTypes 所需的 TypeInfo。</summary>
    private void CollectTypeInfo(TypeDeclNode td)
    {
        var enumMemberValues =
            td.Definition is EnumDefNode enumDef ? ComputeEnumValues(enumDef) : null;

        _collectedTypes.Add(
            td.Definition switch
            {
                StructDefNode sd => new TypeInfo(
                    Name: td.Name,
                    Kind: TypeKind.Struct,
                    Fields: sd.Members.SelectMany(m =>
                            m.Names.Select(n => new FieldInfo(
                                Name: n.Name,
                                TypeName: GetDataTypeDisplayString(m.TypeName),
                                FilePath: n.Span.FileName,
                                Line: n.Span.Line
                            ))
                        )
                        .ToList(),
                    EnumMembers: null,
                    EnumMemberValues: null,
                    UnderlyingType: null,
                    FilePath: td.Span.FileName,
                    Line: td.Span.Line
                ),
                EnumDefNode ed => new TypeInfo(
                    Name: td.Name,
                    Kind: TypeKind.Enum,
                    Fields: null,
                    EnumMembers: ed.Items.Select(i => i.Name).ToList(),
                    EnumMemberValues: enumMemberValues,
                    UnderlyingType: null,
                    FilePath: td.Span.FileName,
                    Line: td.Span.Line
                ),
                SubrangeDefNode sr => new TypeInfo(
                    Name: td.Name,
                    Kind: TypeKind.Subrange,
                    Fields: null,
                    EnumMembers: null,
                    EnumMemberValues: null,
                    UnderlyingType: sr.BaseTypeName,
                    FilePath: td.Span.FileName,
                    Line: td.Span.Line
                ),
                SimpleTypeDefNode st => new TypeInfo(
                    Name: td.Name,
                    Kind: TypeKind.SimpleType,
                    Fields: null,
                    EnumMembers: null,
                    EnumMemberValues: null,
                    UnderlyingType: GetDataTypeDisplayString(st.UnderlyingType),
                    FilePath: td.Span.FileName,
                    Line: td.Span.Line
                ),
                _ => new TypeInfo(
                    Name: td.Name,
                    Kind: TypeKind.SimpleType,
                    Fields: null,
                    EnumMembers: null,
                    EnumMemberValues: null,
                    UnderlyingType: null,
                    FilePath: td.Span.FileName,
                    Line: td.Span.Line
                ),
            }
        );

        _collectedRefs.Add(
            new SymbolReference(
                SymbolName: td.Name,
                Kind: SymbolKind.Type,
                Usage: ReferenceUsage.Declaration,
                POUName: null,
                FilePath: td.Span.FileName,
                Line: td.Span.Line,
                Column: 0
            )
        );
    }

    private IECType RegisterStructType(string name, StructDefNode sd)
    {
        var fields = new Dictionary<string, IECType>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in sd.Members)
        {
            var memberType = ResolveDataTypeNode(member.TypeName);

            // 结构体成员类型不存在时报错
            if (memberType == null && member.TypeName is SimpleDataTypeNode sStruct)
            {
                ReportUnknownType(sStruct, sStruct.TypeName);
                memberType = IECType.DInt;
            }
            memberType ??= IECType.DInt;

            foreach (var vn in member.Names)
                fields[vn.Name] = memberType;
        }
        return IECType.MakeStruct(name, fields);
    }

    // ═══════════════════════════════════════
    //  POU 注册
    // ═══════════════════════════════════════

    private void RegisterPOU(POUNode pou)
    {
        switch (pou)
        {
            case FunctionNode fn:
            {
                var retType = ResolveTypeName(fn.ReturnTypeName) ?? IECType.Void;
                fn.ReturnType = retType;
                var parms = CollectParameters(fn.VarBlocks);
                var declared = Declare(
                    _globalScope,
                    new FunctionSymbol
                    {
                        Name = fn.Name,
                        Declaration = fn.Span,
                        ReturnType = retType,
                        Parameters = parms,
                    },
                    fn.Span
                );

                // 重复声明已报 E2002，跳过收集以免下游重复发射
                if (!declared) break;

                _collectedPOUs.Add(
                    new POUInfo(
                        Name: fn.Name,
                        Kind: POUKind.Function,
                        ReturnTypeName: fn.ReturnTypeName,
                        Parameters: CollectParameterInfos(fn.VarBlocks),
                        FilePath: fn.Span.FileName,
                        Line: fn.Span.Line
                    )
                );

                _collectedRefs.Add(
                    new SymbolReference(
                        SymbolName: fn.Name,
                        Kind: SymbolKind.Function,
                        Usage: ReferenceUsage.Declaration,
                        POUName: null,
                        FilePath: fn.Span.FileName,
                        Line: fn.Span.Line,
                        Column: 0
                    )
                );
                break;
            }
            case FunctionBlockNode fb:
            {
                var members = new Dictionary<string, VariableSymbol>(
                    StringComparer.OrdinalIgnoreCase
                );
                foreach (var vb in fb.VarBlocks)
                {
                    foreach (var item in vb.Items)
                    {
                        var vType = ResolveDataTypeNode(item.TypeName);

                        // E2015: 未知类型
                        if (vType == null && item.TypeName is SimpleDataTypeNode sfb)
                        {
                            ReportUnknownType(sfb, sfb.TypeName);
                            vType = IECType.DInt;
                        }
                        vType ??= IECType.DInt;

                        foreach (var vn in item.Names)
                            members[vn.Name] = new VariableSymbol
                            {
                                Name = vn.Name,
                                Type = vType,
                                Scope = vb.Scope,
                                Modifier = vb.Modifier,
                            };
                    }
                }
                var fbDeclared = Declare(
                    _globalScope,
                    new FunctionBlockSymbol
                    {
                        Name = fb.Name,
                        Declaration = fb.Span,
                        Members = members,
                    },
                    fb.Span
                );

                // 重复声明已报 E2002，跳过收集以免下游重复发射
                if (!fbDeclared) break;

                _collectedPOUs.Add(
                    new POUInfo(
                        Name: fb.Name,
                        Kind: POUKind.FunctionBlock,
                        ReturnTypeName: null,
                        Parameters: CollectParameterInfos(fb.VarBlocks),
                        FilePath: fb.Span.FileName,
                        Line: fb.Span.Line
                    )
                );

                _collectedRefs.Add(
                    new SymbolReference(
                        SymbolName: fb.Name,
                        Kind: SymbolKind.FunctionBlock,
                        Usage: ReferenceUsage.Declaration,
                        POUName: null,
                        FilePath: fb.Span.FileName,
                        Line: fb.Span.Line,
                        Column: 0
                    )
                );
                break;
            }
            case ProgramNode prog:
            {
                var progDeclared = Declare(
                    _globalScope,
                    new FunctionSymbol
                    {
                        Name = prog.Name,
                        Declaration = prog.Span,
                        ReturnType = IECType.Void,
                        Parameters = new(),
                    },
                    prog.Span
                );

                // 重复声明已报 E2002，跳过收集以免下游重复发射
                if (!progDeclared) break;

                _collectedPOUs.Add(
                    new POUInfo(
                        Name: prog.Name,
                        Kind: POUKind.Program,
                        ReturnTypeName: null,
                        Parameters: new(),
                        FilePath: prog.Span.FileName,
                        Line: prog.Span.Line
                    )
                );

                _collectedRefs.Add(
                    new SymbolReference(
                        SymbolName: prog.Name,
                        Kind: SymbolKind.Program,
                        Usage: ReferenceUsage.Declaration,
                        POUName: null,
                        FilePath: prog.Span.FileName,
                        Line: prog.Span.Line,
                        Column: 0
                    )
                );
                break;
            }
        }
    }

    // ═══════════════════════════════════════
    //  POU 体分析
    // ═══════════════════════════════════════

    private void AnalyzePOU(POUNode pou)
    {
        _currentScope = _globalScope.EnterScope();
        _currentFunctionName = pou.Name;
        _currentPOUName = pou.Name;
        _currentReturnType = pou is FunctionNode fn ? fn.ReturnType : IECType.Void;
        _loopDepth = 0;

        foreach (var vb in pou.VarBlocks)
        {
            var blockKind = vb.Scope switch
            {
                VarScope.Local => "VAR",
                VarScope.Input => "VAR_INPUT",
                VarScope.Output => "VAR_OUTPUT",
                VarScope.InOut => "VAR_IN_OUT",
                VarScope.Global => "VAR_GLOBAL",
                VarScope.External => "VAR_EXTERNAL",
                VarScope.Temp => "VAR_TEMP",
                _ => "VAR",
            };

            // VAR_CONFIG 块使用 ConfigItems 而非 Items
            if (vb.IsConfigBlock)
            {
                foreach (var ci in vb.ConfigItems)
                {
                    var vType = ci.TypeName != null ? ResolveDataTypeNode(ci.TypeName) : null;
                    vType ??= IECType.DInt;
                    var typeName =
                        ci.TypeName != null ? GetDataTypeDisplayString(ci.TypeName) : "UNKNOWN";

                    // VAR_CONFIG 是对已声明变量的配置绑定，不是新声明 ——
                    // 与 VAR 中的同名变量并存是正常情况，不报 E2002，
                    // 且无论是否已声明都要收集该配置项。
                    Declare(
                        _currentScope,
                        new VariableSymbol
                        {
                            Name = ci.Name,
                            Declaration = ci.Span,
                            Type = vType,
                            Scope = VarScope.Local,
                            DirectAddress = ci.DirectAddress,
                        },
                        ci.Span,
                        tolerant: true
                    );

                    _collectedVariables.Add(
                        new VariableInfo(
                            Name: ci.Name,
                            TypeName: typeName,
                            POUName: pou.Name ?? "UNKNOWN",
                            VarBlockKind: "VAR_CONFIG",
                            FilePath: ci.Span.FileName,
                            Line: ci.Span.Line
                        )
                    );

                    _collectedRefs.Add(
                        new SymbolReference(
                            SymbolName: ci.Name,
                            Kind: SymbolKind.Variable,
                            Usage: ReferenceUsage.Declaration,
                            POUName: pou.Name,
                            FilePath: ci.Span.FileName,
                            Line: ci.Span.Line,
                            Column: 0
                        )
                    );
                }
                continue;
            }

            foreach (var item in vb.Items)
            {
                var vType = ResolveDataTypeNode(item.TypeName);

                // E2015: 未知类型
                if (vType == null && item.TypeName is SimpleDataTypeNode sErr)
                {
                    ReportUnknownType(sErr, sErr.TypeName);
                    vType = IECType.DInt;
                }
                vType ??= IECType.DInt;

                var typeName = GetDataTypeDisplayString(item.TypeName);

                // F3：显式初始值。语义层此前**不碰** `item.InitialValue`，
                // 于是初值在前端边界就丢了（缺陷登记簿 §3.1）。
                // 这里先做类型推导（IR 层靠 ResolvedType 决定发什么指令），
                // 再对全局变量做编译期常量求值（IEC 要求全局初值是常量）。
                ConstantInitializer? initConst = null;
                if (item.InitialValue is ExpressionNode initExpr)
                {
                    InferType(initExpr);

                    if (vb.Scope == VarScope.Global)
                    {
                        initConst = ConstantEvaluator.TryEvaluate(initExpr, vType, out var isConstant);
                        if (initConst is null)
                        {
                            if (isConstant)
                                _bag.Warning(
                                    "暂不支持该类型的全局变量初始值（初值被忽略）",
                                    initExpr.Span,
                                    ErrorCode.E2017);
                            else
                                _bag.Error(
                                    "全局变量的初始值必须是编译期常量",
                                    initExpr.Span,
                                    ErrorCode.E2017);
                        }
                    }
                }

                if (item.TypeName is SimpleDataTypeNode simple)
                {
                    _collectedRefs.Add(
                        new SymbolReference(
                            SymbolName: simple.TypeName,
                            Kind: SymbolKind.Type,
                            Usage: ReferenceUsage.TypeUsage,
                            POUName: pou.Name,
                            FilePath: simple.Span.FileName,
                            Line: simple.Span.Line,
                            Column: 0
                        )
                    );
                }

                foreach (var vn in item.Names)
                {
                    var vnDeclared = Declare(
                        _currentScope,
                        new VariableSymbol
                        {
                            Name = vn.Name,
                            Declaration = vn.Span,
                            Type = vType,
                            Scope = vb.Scope,
                            Modifier = vb.Modifier,
                            DirectAddress = vn.DirectAddress,
                        },
                        vn.Span
                    );

                    // 重复声明已报 E2002，跳过收集以免下游重复分配
                    if (!vnDeclared) continue;

                    // A3：VAR_RETAIN / NON_RETAIN 拒绝路径（检查表 A3 验收：前端拒绝，不能静默忽略）
                    if (vb.Modifier == VarModifier.Retain || vb.Modifier == VarModifier.NonRetain)
                    {
                        _bag.Error(
                            "VAR_RETAIN / NON_RETAIN 语义未实现：持久化与恢复功能不可用（A3 未修复）",
                            vn.Span,
                            ErrorCode.E3003);
                    }

                    _collectedVariables.Add(
                        new VariableInfo(
                            Name: vn.Name,
                            TypeName: typeName,
                            POUName: pou.Name ?? "UNKNOWN",
                            VarBlockKind: blockKind,
                            FilePath: vn.Span.FileName,
                            Line: vn.Span.Line,
                            InitialValue: initConst,
                            DeclaredType: vType
                        )
                    );

                    _collectedRefs.Add(
                        new SymbolReference(
                            SymbolName: vn.Name,
                            Kind: SymbolKind.Variable,
                            Usage: ReferenceUsage.Declaration,
                            POUName: pou.Name,
                            FilePath: vn.Span.FileName,
                            Line: vn.Span.Line,
                            Column: 0
                        )
                    );
                }
            }
        }

        if (pou is FunctionNode && _currentReturnType != IECType.Void)
        {
            // 函数名隐式充当返回值变量。若用户已用同名局部变量占位，
            // 这里会被顶掉，返回值赋值随之失效 —— 必须显式报错。
            Declare(
                _currentScope,
                new VariableSymbol
                {
                    Name = pou.Name!,
                    Type = _currentReturnType!,
                    Scope = VarScope.Local,
                },
                pou.Span
            );
        }

        // 分析主体语句
        foreach (var stmt in pou.Body)
            AnalyzeStatement(stmt);

        // FunctionBlock 的 BodyItems 包含语句和 Action 的交错序列，
        // Parser 将它们全部放入 BodyItems 而非基类的 Body，
        // 因此需要额外遍历 BodyItems。
        if (pou is FunctionBlockNode fbNode)
        {
            foreach (var item in fbNode.BodyItems)
            {
                switch (item)
                {
                    case FBStatementItemNode stmtItem:
                        AnalyzeStatement(stmtItem.Statement);
                        break;
                    case FBActionItemNode actionItem:
                        foreach (var stmt in actionItem.Action.Body)
                            AnalyzeStatement(stmt);
                        break;
                }
            }
        }

        // 检查函数返回变量是否被赋值（大小写不敏感，IEC 61131-3 标识符不区分大小写）
        if (pou is FunctionNode && _currentReturnType != IECType.Void)
        {
            var hasReturnAssignment = _collectedRefs.Any(r =>
                string.Equals(r.SymbolName, pou.Name, StringComparison.OrdinalIgnoreCase)
                && r.Usage == ReferenceUsage.Write
                && string.Equals(r.POUName, pou.Name, StringComparison.OrdinalIgnoreCase)
            );

            if (!hasReturnAssignment)
            {
                _bag.Error(
                    $"函数 '{pou.Name}' 声明了返回类型但未对返回变量赋值",
                    pou.Span,
                    ErrorCode.E2001
                );
            }
        }

        _currentScope = _globalScope;
    }

    // ═══════════════════════════════════════
    //  语句分析
    // ═══════════════════════════════════════

    private void ValidateCallTarget(CallStatementNode c)
    {
        if (IECType.LookupBuiltin(c.Name) != null)
            return;
        var sym = _globalScope.Resolve(c.Name);
        if (sym is FunctionSymbol or FunctionBlockSymbol)
            return;

        // FB 实例调用（如 fb(args)）
        var localSym = _currentScope.Resolve(c.Name);
        if (localSym is VariableSymbol)
            return;

        _bag.Error($"未声明的函数 '{c.Name}'", c.Span, ErrorCode.E2006);
    }

    private void AnalyzeStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case AssignmentNode a:
                CollectExprRefs(a.Target, ReferenceUsage.Write);
                CollectExprRefs(a.Value, ReferenceUsage.Read);
                InferType(a.Target);
                InferType(a.Value);
                break;

            // REF= 赋值（引用绑定）
            case RefAssignmentNode ra:
                CollectExprRefs(ra.Target, ReferenceUsage.Write);
                CollectExprRefs(ra.Value, ReferenceUsage.Read);
                var raTargetType = InferType(ra.Target);
                if (raTargetType.Kind != IECType.KindTag.REFERENCE)
                {
                    _bag.Error("REF= 的目标必须是 REFERENCE 类型", ra.Target.Span, ErrorCode.E2015);
                }
                break;

            case CallStatementNode c:
                ValidateCallTarget(c);

                _collectedCalls.Add(BuildCallSite(c.Name, c.Arguments, c.Span));

                _collectedRefs.Add(
                    new SymbolReference(
                        SymbolName: c.Name,
                        Kind: SymbolKind.Function,
                        Usage: ReferenceUsage.Call,
                        POUName: _currentPOUName,
                        FilePath: c.Span.FileName,
                        Line: c.Span.Line,
                        Column: 0
                    )
                );

                foreach (var arg in c.Arguments)
                {
                    CollectExprRefs(arg.Value, ReferenceUsage.Read);
                    InferType(arg.Value);
                }
                break;

            case IfNode ifn:
                CollectExprRefs(ifn.Condition, ReferenceUsage.Read);
                InferType(ifn.Condition);
                foreach (var s in ifn.ThenBody)
                    AnalyzeStatement(s);
                foreach (var (_, body) in ifn.ElsIfBranches)
                foreach (var s in body)
                    AnalyzeStatement(s);
                if (ifn.ElseBody != null)
                    foreach (var s in ifn.ElseBody)
                        AnalyzeStatement(s);
                break;

            case ForNode fn:
                CollectExprRefs(fn.From, ReferenceUsage.Read);
                CollectExprRefs(fn.To, ReferenceUsage.Read);
                InferType(fn.From);
                InferType(fn.To);
                if (fn.By != null)
                {
                    CollectExprRefs(fn.By, ReferenceUsage.Read);
                    InferType(fn.By);
                }
                _loopDepth++;
                foreach (var s in fn.Body)
                    AnalyzeStatement(s);
                _loopDepth--;
                break;

            case WhileNode wn:
                CollectExprRefs(wn.Condition, ReferenceUsage.Read);
                InferType(wn.Condition);
                _loopDepth++;
                foreach (var s in wn.Body)
                    AnalyzeStatement(s);
                _loopDepth--;
                break;

            case RepeatNode rn:
                _loopDepth++;
                foreach (var s in rn.Body)
                    AnalyzeStatement(s);
                _loopDepth--;
                if (rn.UntilCondition != null)
                {
                    CollectExprRefs(rn.UntilCondition, ReferenceUsage.Read);
                    InferType(rn.UntilCondition);
                }
                else
                    _bag.Error("REPEAT 缺少 UNTIL 条件", rn.Span, ErrorCode.E1009);
                break;

            case CaseNode cn:
                CollectExprRefs(cn.Selector, ReferenceUsage.Read);
                InferType(cn.Selector);
                foreach (var b in cn.Branches)
                foreach (var s in b.Body)
                    AnalyzeStatement(s);
                if (cn.ElseBody != null)
                    foreach (var s in cn.ElseBody)
                        AnalyzeStatement(s);
                break;

            // EXIT 只能出现在循环体内
            case ExitNode exit:
                if (_loopDepth <= 0)
                {
                    _bag.Error(
                        "EXIT 语句只能出现在循环体内（FOR / WHILE / REPEAT）",
                        exit.Span,
                        ErrorCode.E1009
                    );
                }
                break;

            case ReturnNode ret:
                // RETURN 语句通过，后续可扩展返回类型校验
                break;
        }
    }

    // ═══════════════════════════════════════
    //  表达式引用收集
    // ═══════════════════════════════════════

    private void CollectExprRefs(ExpressionNode expr, ReferenceUsage usage)
    {
        switch (expr)
        {
            case VariableExprNode v:
                if (v.Segments.Count > 0)
                {
                    var firstName = v.Segments[0].Name;
                    var sym = _currentScope.Resolve(firstName);

                    // 枚举类型引用 Color.Red -> TypeUsage
                    if (sym is TypeSymbol && v.Segments.Count > 1)
                    {
                        _collectedRefs.Add(
                            new SymbolReference(
                                SymbolName: firstName,
                                Kind: SymbolKind.Type,
                                Usage: ReferenceUsage.TypeUsage,
                                POUName: _currentPOUName,
                                FilePath: v.Span.FileName,
                                Line: v.Span.Line,
                                Column: v.Span.Column
                            )
                        );
                    }
                    else
                    {
                        _collectedRefs.Add(
                            new SymbolReference(
                                SymbolName: firstName,
                                Kind: SymbolKind.Variable,
                                Usage: usage,
                                POUName: _currentPOUName,
                                FilePath: v.Span.FileName,
                                Line: v.Span.Line,
                                Column: v.Span.Column
                            )
                        );
                    }
                }
                break;

            case FunctionCallExprNode call:
                if (IECType.LookupBuiltin(call.Name) == null)
                {
                    _collectedCalls.Add(BuildCallSite(call.Name, call.Arguments, call.Span));

                    _collectedRefs.Add(
                        new SymbolReference(
                            SymbolName: call.Name,
                            Kind: SymbolKind.Function,
                            Usage: ReferenceUsage.Call,
                            POUName: _currentPOUName,
                            FilePath: call.Span.FileName,
                            Line: call.Span.Line,
                            Column: 0
                        )
                    );
                }
                foreach (var arg in call.Arguments)
                    CollectExprRefs(arg.Value, ReferenceUsage.Read);
                break;

            case BinaryExprNode bin:
                CollectExprRefs(bin.Left, usage);
                CollectExprRefs(bin.Right, usage);
                break;

            case UnaryExprNode un:
                CollectExprRefs(un.Operand, usage);
                break;
        }
    }

    // ═══════════════════════════════════════
    //  调用关系收集（带参数方向和类型）
    // ═══════════════════════════════════════

    private CallSite BuildCallSite(
        string calleeName,
        IReadOnlyList<CallParamNode> arguments,
        SourceSpan span
    )
    {
        List<VariableSymbol>? formalParams = null;
        var calleeSym = _globalScope.Resolve(calleeName);
        if (calleeSym is FunctionSymbol fs)
            formalParams = fs.Parameters;
        else if (calleeSym is FunctionBlockSymbol fbs)
            formalParams = fbs.Members.Values.ToList();
        else
        {
            // 通过 FB 实例变量推导类型，而非直接取全局符号表中第一个 FB
            var localSym = _currentScope.Resolve(calleeName);
            if (localSym is VariableSymbol vs && vs.Type.Name != null)
            {
                var resolvedFb = _globalScope.Resolve(vs.Type.Name);
                if (resolvedFb is FunctionBlockSymbol targetFb)
                    formalParams = targetFb.Members.Values.ToList();
            }
        }

        // CALL-ARITY：函数调用的实参数量 / 命名绑定校验（详见 docs/阶段0B设计.md §6.3.9）。
        // 仅覆盖函数调用；FB 实例调用保留原有容忍，不在本切片内改动。
        if (calleeSym is FunctionSymbol fnSym)
            ValidateFunctionCallArguments(calleeName, arguments, fnSym.Parameters, span);

        var argInfos = new List<CallArgumentInfo>();

        for (int i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            var resolvedType = InferType(arg.Value);

            string? paramName = arg.Name;
            string? paramTypeName = null;
            ParameterDirection direction = ParameterDirection.Unknown;

            VariableSymbol? formal = null;

            if (paramName != null)
            {
                formal = formalParams?.FirstOrDefault(p =>
                    p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase)
                );
            }
            else if (formalParams != null && i < formalParams.Count)
            {
                formal = formalParams[i];
                paramName = formal.Name;
            }

            if (formal != null)
            {
                paramTypeName = GetIECTypeDisplayString(formal.Type);
                direction = formal.Scope switch
                {
                    VarScope.Input => ParameterDirection.Input,
                    VarScope.Output => ParameterDirection.Output,
                    VarScope.InOut => ParameterDirection.InOut,
                    _ => ParameterDirection.Input,
                };
            }

            ArgumentBindingStyle binding;
            if (paramName != null && arg.Name != null)
            {
                binding = arg.IsOutputBinding
                    ? ArgumentBindingStyle.NamedOutput
                    : ArgumentBindingStyle.NamedInput;
            }
            else
            {
                binding = ArgumentBindingStyle.Positional;
            }

            var summary = SummarizeExpression(arg.Value);

            argInfos.Add(
                new CallArgumentInfo(
                    Index: i,
                    ParameterName: paramName,
                    ParameterTypeName: paramTypeName,
                    Direction: direction,
                    ExpressionSummary: summary,
                    ResolvedTypeName: GetIECTypeDisplayString(resolvedType),
                    Binding: binding
                )
            );
        }

        return new CallSite(
            CallerName: _currentPOUName ?? "UNKNOWN",
            CalleeName: calleeName,
            CallerPOU: _currentPOUName,
            FilePath: span.FileName,
            Line: span.Line,
            Arguments: argInfos
        );
    }

    /// <summary>
    /// CALL-ARITY：校验函数调用的实参。
    ///
    /// <para>裁定（2026-10-16，针对本项目调用契约）：实参个数与形参数量不一致应编译失败。
    /// 依据是后端调用约定要求 <c>A0..An</c> 与被调方 <c>L0..Ln</c> 逐位对应；缺参后
    /// 缺失指针形参会解引用 <c>A</c> 位残留值（静默写错帧槽）。详见
    /// <c>docs/阶段0B设计.md</c> §6.3.9。</para>
    ///
    /// <para>判据与 <c>ExpressionEmitter.ResolveCallArguments</c> 一致：只要出现命名实参就
    /// 整体按命名绑定，命名路径要求每个形参恰好绑定一次。所有违约统一报既有
    /// <see cref="ErrorCode.E2004"/>（参数数量不匹配），不新增编号。</para>
    /// </summary>
    private void ValidateFunctionCallArguments(
        string calleeName,
        IReadOnlyList<CallParamNode> arguments,
        IReadOnlyList<VariableSymbol> formalParams,
        SourceSpan span
    )
    {
        bool hasNamed = arguments.Any(a => a.Name != null);

        // 位置实参：个数必须与形参数量完全一致。
        if (!hasNamed)
        {
            if (arguments.Count != formalParams.Count)
            {
                _bag.Error(
                    $"函数 '{calleeName}' 期望 {formalParams.Count} 个实参，实际 {arguments.Count} 个",
                    span,
                    ErrorCode.E2004
                );
            }
            return;
        }

        // 命名实参：每个实参都须有名字，且恰好覆盖每个形参一次。
        var bound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in arguments)
        {
            if (arg.Name == null)
            {
                _bag.Error(
                    $"函数 '{calleeName}' 的调用不能混用位置实参与命名实参",
                    arg.Value.Span,
                    ErrorCode.E2004
                );
                continue;
            }

            var formal = formalParams.FirstOrDefault(p =>
                p.Name.Equals(arg.Name, StringComparison.OrdinalIgnoreCase)
            );

            if (formal == null)
            {
                _bag.Error(
                    $"函数 '{calleeName}' 没有名为 '{arg.Name}' 的形参",
                    arg.Value.Span,
                    ErrorCode.E2004
                );
                continue;
            }

            if (!bound.Add(formal.Name))
            {
                _bag.Error(
                    $"函数 '{calleeName}' 的形参 '{arg.Name}' 被重复指定",
                    arg.Value.Span,
                    ErrorCode.E2004
                );
            }
        }

        if (bound.Count != formalParams.Count)
        {
            var missing = formalParams
                .Where(p => !bound.Contains(p.Name))
                .Select(p => p.Name);
            _bag.Error(
                $"函数 '{calleeName}' 期望 {formalParams.Count} 个实参，命名调用只覆盖了 {bound.Count} 个（缺少: {string.Join(", ", missing)}）",
                span,
                ErrorCode.E2004
            );
        }
    }

    // ═══════════════════════════════════════
    //  表达式类型推导
    // ═══════════════════════════════════════

    private IECType InferType(ExpressionNode expr)
    {
        // 先处理时间字面量（需要副作用填充 ParsedValue）
        if (expr is LiteralExprNode litNode)
            TryParseTimeLiteral(litNode);

        if (expr.ResolvedType != null)
            return expr.ResolvedType;

        IECType result = expr switch
        {
            LiteralExprNode lit => lit.ResolvedType ?? IECType.DInt,
            TypedLiteralExprNode tl => ResolveTypeName(tl.TypeName) ?? IECType.DInt,
            VariableExprNode v => InferVariable(v),
            BinaryExprNode bin => InferBinary(bin),
            UnaryExprNode un => InferUnary(un),
            FunctionCallExprNode call => InferCall(call),
            _ => IECType.DInt,
        };

        expr.ResolvedType = result;
        return result;
    }

    private static void TryParseTimeLiteral(LiteralExprNode lit)
    {
        if (
            lit.ParsedValue == null
            && lit.RawText != null
            && lit.Kind
                is LiteralKind.Time
                    or LiteralKind.Date
                    or LiteralKind.TimeOfDay
                    or LiteralKind.DateTime
        )
        {
            lit.ParsedValue = TimeLiteralParser.TryParse(lit.Kind, lit.RawText);
        }
    }

    private IECType InferVariable(VariableExprNode v)
    {
        if (v.Segments.Count == 0)
        {
            _bag.Error("空变量表达式", v.Span, ErrorCode.E2001);
            return IECType.DInt;
        }

        var firstName = v.Segments[0].Name;
        var sym = _currentScope.Resolve(firstName);
        IECType ty;

        if (sym is VariableSymbol vs)
            ty = vs.Type;
        else if (string.Equals(firstName, _currentFunctionName, StringComparison.OrdinalIgnoreCase))
            ty = _currentReturnType ?? IECType.DInt;
        // Color.Red 形式
        else if (
            sym is TypeSymbol ts
            && ts.Type.Kind == IECType.KindTag.ENUM
            && v.Segments.Count > 1
        )
        {
            var memberName = v.Segments[1].Name;
            if (
                ts.Type.EnumValues != null
                && ts.Type.EnumValues.Any(m =>
                    string.Equals(m, memberName, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return ts.Type;
            }
            _bag.Error(
                $"枚举 '{firstName}' 没有成员 '{memberName}'",
                v.Segments[1].Span,
                ErrorCode.E2008
            );
            return IECType.DInt;
        }
        // 单独使用 Red（无前缀）
        else if (sym is null)
        {
            foreach (var (_, globalSym) in _globalScope.Locals)
            {
                if (
                    globalSym is TypeSymbol enumTs
                    && enumTs.Type.Kind == IECType.KindTag.ENUM
                    && enumTs.Type.EnumValues != null
                    && enumTs.Type.EnumValues.Any(m =>
                        string.Equals(m, firstName, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    return enumTs.Type;
                }
            }
            _bag.Error($"未声明的变量 '{firstName}'", v.Span, ErrorCode.E2001);
            return IECType.DInt;
        }
        else
        {
            _bag.Error($"未声明的变量 '{firstName}'", v.Span, ErrorCode.E2001);
            return IECType.DInt;
        }

        for (int i = 0; i < v.Segments.Count; i++)
        {
            var segment = v.Segments[i];

            if (i > 0)
            {
                if (
                    ty is { Kind: IECType.KindTag.STRUCT, Fields: not null }
                    && ty.Fields.TryGetValue(segment.Name, out var ft)
                )
                    ty = ft;
                // 枚举成员（Color.Red 走到这里时命中）
                else if (
                    ty is { Kind: IECType.KindTag.ENUM, EnumValues: not null }
                    && ty.EnumValues.Any(m =>
                        string.Equals(m, segment.Name, StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    // 枚举成员访问，类型保持不变
                }
                else
                {
                    _bag.Error($"结构体没有字段 '{segment.Name}'", segment.Span, ErrorCode.E2008);
                    return IECType.DInt;
                }
            }

            foreach (var suffix in segment.Suffixes)
            {
                if (suffix.IsDereference)
                {
                    // 指针解引用：REF_TO T -> T
                    // ① 曾错读 ElementType（那是 ARRAY 的字段）：MakeReference 只写
                    // ReferencedType，于是此条件恒假，合法的 r^ 也报「无法对非引用
                    // 类型进行解引用」。
                    //
                    // 前向引用（目标类型声明在后面）时占位符问题已由
                    // BackfillReferenceTargets 在类型不动点收敛后回填解决，
                    // 此处直接取 ReferencedType 即为真实目标。
                    if (ty is { Kind: IECType.KindTag.REFERENCE, ReferencedType: not null })
                        ty = ty.ReferencedType;
                    else
                    {
                        _bag.Error(
                            $"无法对非引用类型 '{GetIECTypeDisplayString(ty)}' 进行解引用",
                            segment.Span,
                            ErrorCode.E2015
                        );
                        return IECType.DInt;
                    }
                }
                else if (suffix.Subscripts != null)
                {
                    if (ty is { Kind: IECType.KindTag.ARRAY, ElementType: not null })
                        ty = ty.ElementType;
                }
            }
        }

        return ty;
    }

    private IECType InferBinary(BinaryExprNode bin)
    {
        var lt = InferType(bin.Left);
        var rt = InferType(bin.Right);
        if (
            bin.Op
            is BinaryOp.Eq
                or BinaryOp.Neq
                or BinaryOp.Lt
                or BinaryOp.Gt
                or BinaryOp.Lte
                or BinaryOp.Gte
        )
        {
            CheckStructComparison(bin, lt, rt);
            return IECType.Bool;
        }
        if (bin.Op is BinaryOp.And or BinaryOp.Or or BinaryOp.Xor)
            return IECType.Bool;
        return Widen(lt, rt);
    }

    /// <summary>
    /// 结构体比较的前端裁定（A1/F5 · 方案 A）。
    ///
    /// <para>
    /// 值语义比较只对**同一命名结构体**的 <c>=</c> / <c>&lt;&gt;</c> 开放。
    /// 首版不比较含 <c>REFERENCE</c> / <c>STRING</c> / <c>WSTRING</c> 字段的结构体 ——
    /// 引用的“值”是地址还是目标对象没有语言级裁定，定长字符串的存储形状
    /// 也与字节码侧的单槽表示不同。这里先在前端拒绝，避免
    /// <c>verify</c> 通过、<c>run</c> 才在代码生成阶段失败。
    /// </para>
    /// <para>
    /// 嵌套结构体与数组会递归检查；直接递归布局在前端已由 E2016 拦截，
    /// 这里的 <paramref name="visiting"/> 只作为防御性环保护。
    /// </para>
    /// </summary>
    private void CheckStructComparison(BinaryExprNode bin, IECType lt, IECType rt)
    {
        // 数组整体比较不在 A1 范围内：结构体**字段**里的数组会被逐元素展开，
        // 但 `arr1 = arr2` 这种顶层数组操作数没有对应的按值降级路径。
        // 先给出可操作的前端诊断，避免落到代码生成阶段产生错误比较。
        if (lt.Kind == IECType.KindTag.ARRAY || rt.Kind == IECType.KindTag.ARRAY)
        {
            _bag.Error(
                "数组整体比较暂不支持；请逐元素比较",
                bin.Span,
                ErrorCode.E2003
            );
            return;
        }

        bool leftIsStruct = lt.Kind == IECType.KindTag.STRUCT;
        bool rightIsStruct = rt.Kind == IECType.KindTag.STRUCT;
        if (!leftIsStruct && !rightIsStruct)
            return;

        if (bin.Op is not (BinaryOp.Eq or BinaryOp.Neq))
        {
            _bag.Error(
                $"结构体只支持 = 和 <> 比较，不支持 {StructCompareOpText(bin.Op)}",
                bin.Span,
                ErrorCode.E2003
            );
            return;
        }

        if (!leftIsStruct || !rightIsStruct)
        {
            var structType = leftIsStruct ? lt : rt;
            var otherType = leftIsStruct ? rt : lt;
            _bag.Error(
                $"结构体 '{structType.Name}' 不能与非结构体 '{GetIECTypeDisplayString(otherType)}' 比较",
                bin.Span,
                ErrorCode.E2003
            );
            return;
        }

        if (!SameStructType(lt, rt))
        {
            _bag.Error(
                $"结构体比较要求两侧类型相同：'{lt.Name}' 与 '{rt.Name}'",
                bin.Span,
                ErrorCode.E2003
            );
            return;
        }

        // 值比较的降级实现需要操作数地址（逐字段 GEP + load）。
        // 前端先把非左值（如结构体函数返回值）挡掉，避免 verify 通过、run 才失败。
        if (bin.Left is not VariableExprNode || bin.Right is not VariableExprNode)
        {
            _bag.Error(
                "结构体比较的操作数必须是可寻址的变量、字段或数组元素",
                bin.Span,
                ErrorCode.E2003
            );
            return;
        }

        if (
            TryFindUnsupportedStructCompareField(
                lt,
                new HashSet<IECType>(),
                "",
                out var fieldPath,
                out var fieldKind
            )
        )
        {
            _bag.Error(
                $"结构体 '{lt.Name}' 的字段 '{fieldPath}' 类型为 {fieldKind}，暂不支持结构体比较",
                bin.Span,
                ErrorCode.E2003
            );
        }
    }

    private static bool SameStructType(IECType left, IECType right) =>
        ReferenceEquals(left, right)
        || string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 在结构体（可经数组嵌套）中查找首版结构体比较不支持的字段类型。
    /// 返回 <c>true</c> 时 <paramref name="fieldPath"/> / <paramref name="fieldKind"/>
    /// 指向首个命中的字段与类型。
    /// </summary>
    private static bool TryFindUnsupportedStructCompareField(
        IECType type,
        HashSet<IECType> visiting,
        string path,
        out string fieldPath,
        out string fieldKind
    )
    {
        switch (type.Kind)
        {
            case IECType.KindTag.REFERENCE:
            case IECType.KindTag.STRING:
            case IECType.KindTag.WSTRING:
                fieldPath = path.Length == 0 ? "<root>" : path;
                fieldKind = type.Kind.ToString();
                return true;

            case IECType.KindTag.ARRAY:
                return TryFindUnsupportedStructCompareField(
                    type.ElementType!,
                    visiting,
                    path + "[]",
                    out fieldPath,
                    out fieldKind
                );

            case IECType.KindTag.STRUCT:
                if (!visiting.Add(type))
                {
                    fieldPath = "";
                    fieldKind = "";
                    return false;
                }
                if (type.Fields is { } fields)
                {
                    foreach (var (name, fieldType) in fields)
                    {
                        var childPath = path.Length == 0 ? name : path + "." + name;
                        if (
                            TryFindUnsupportedStructCompareField(
                                fieldType,
                                visiting,
                                childPath,
                                out fieldPath,
                                out fieldKind
                            )
                        )
                        {
                            visiting.Remove(type);
                            return true;
                        }
                    }
                }
                visiting.Remove(type);
                fieldPath = "";
                fieldKind = "";
                return false;

            default:
                fieldPath = "";
                fieldKind = "";
                return false;
        }
    }

    private static string StructCompareOpText(BinaryOp op) =>
        op switch
        {
            BinaryOp.Lt => "<",
            BinaryOp.Gt => ">",
            BinaryOp.Lte => "<=",
            BinaryOp.Gte => ">=",
            _ => op.ToString(),
        };

    private IECType InferUnary(UnaryExprNode un)
    {
        var t = InferType(un.Operand);
        return un.Op == UnaryOp.Not ? IECType.Bool : t;
    }

    private IECType InferCall(FunctionCallExprNode call)
    {
        foreach (var arg in call.Arguments)
            InferType(arg.Value);

        var builtin = IECType.LookupBuiltin(call.Name);
        if (builtin != null)
            return builtin;
        var sym = _globalScope.Resolve(call.Name);
        if (sym is FunctionSymbol fs)
            return fs.ReturnType;
        _bag.Error($"未声明的函数 '{call.Name}'", call.Span, ErrorCode.E2006);
        return IECType.DInt;
    }

    // ═══════════════════════════════════════
    //  工具方法
    // ═══════════════════════════════════════

    private IECType? ResolveDataTypeNode(DataTypeNode? node) =>
        node switch
        {
            SimpleDataTypeNode s => ResolveSimpleDataType(s),
            ArrayDataTypeNode a => ResolveArrayType(a),
            ReferenceDataTypeNode r => ResolveReferenceDataType(r),
            _ => null,
        };

    /// <summary>
    /// 解析 <c>REFERENCE TO T</c>（F1）。
    ///
    /// <para>
    /// <b>此前</b>写的是 <c>ResolveTypeName(T) ?? IECType.DInt</c> —— T 不存在时静默变成
    /// <c>REF_TO DInt</c>，编译器把它当成完全合法的引用类型接受，全程零诊断。这使
    /// 「写错了目标类型名」与「就是想引用一个 DInt」无法区分。
    /// </para>
    /// <para>
    /// 现在目标名未知时报 E2015（与普通未知类型同一条诊断码，不新增）。
    /// 退回值仍保持 <c>Kind.REFERENCE</c> 而非整体降成 DInt —— 这样 F7 的布局分类
    /// （引用边不阻塞）与 <c>REF=</c> 的目标类型检查拿到的仍是正确的形状，错误恢复
    /// 不会连带污染后续判断。
    /// </para>
    /// <para>
    /// 占位符不会误报：<see cref="PredeclareTypeDecl"/> 把未解析类型登记为
    /// <c>IECType.Void</c>（非 null），库注入的 TypeSymbol 也恒有值，因此
    /// <see cref="ResolveTypeName"/> 返回 null 当且仅当名字确实不存在。
    /// </para>
    /// </summary>
    private IECType ResolveReferenceDataType(ReferenceDataTypeNode r)
    {
        if (ResolveTypeName(r.ReferencedTypeName) is { } target)
            return IECType.MakeReference(target, r.ReferencedTypeName);

        ReportUnknownType(r, r.ReferencedTypeName);
        return IECType.MakeReference(IECType.DInt, r.ReferencedTypeName);
    }

    private IECType? ResolveSimpleDataType(SimpleDataTypeNode s)
    {
        if (s.SizeParam != null)
        {
            int? size = EvaluateSizeParam(s.SizeParam);
            if (size.HasValue)
                return IECType.MakeString(
                    size.Value,
                    s.TypeName.Contains("WSTRING", StringComparison.OrdinalIgnoreCase)
                );
        }
        return ResolveTypeName(s.TypeName); // 返回 null，不兜底
    }

    private static int? EvaluateSizeParam(ExpressionNode expr)
    {
        if (expr is LiteralExprNode lit)
            return lit.ParsedValue switch
            {
                long l => (int)l,
                int i => i,
                _ => null,
            };
        return null;
    }

    private IECType ResolveArrayType(ArrayDataTypeNode a)
    {
        var element = ResolveDataTypeNode(a.ElementType) ?? IECType.DInt;
        var dims = new List<(int, int)>();
        foreach (var (lower, upper) in a.Dimensions)
            dims.Add((EvaluateIntLiteral(lower), EvaluateIntLiteral(upper)));
        return IECType.MakeArray(element, dims);
    }

    private IECType? ResolveTypeName(string name)
    {
        var builtin = IECType.LookupBuiltin(name);
        if (builtin != null)
            return builtin;
        var sym = _globalScope.Resolve(name);
        if (sym is TypeSymbol ts)
            return ts.Type;

        // FunctionBlock 名字可以当类型用（如 pid : PID;）
        if (sym is FunctionBlockSymbol fb)
        {
            var fields = new Dictionary<string, IECType>(StringComparer.OrdinalIgnoreCase);
            foreach (var (memberName, memberSym) in fb.Members)
                fields[memberName] = memberSym.Type;
            return IECType.MakeStruct(fb.Name, fields);
        }

        return null;
    }

    // CollectParameters 加上 E2015 检查
    private List<VariableSymbol> CollectParameters(List<VarDeclBlockNode> blocks)
    {
        var result = new List<VariableSymbol>();
        foreach (var vb in blocks)
        {
            if (vb.Scope is not (VarScope.Input or VarScope.Output or VarScope.InOut))
                continue;
            foreach (var item in vb.Items)
            {
                var vType = ResolveDataTypeNode(item.TypeName);

                // E2015: 未知类型
                if (vType == null && item.TypeName is SimpleDataTypeNode sParam)
                {
                    ReportUnknownType(sParam, sParam.TypeName);
                    vType = IECType.DInt;
                }
                vType ??= IECType.DInt;

                foreach (var vn in item.Names)
                    result.Add(
                        new VariableSymbol
                        {
                            Name = vn.Name,
                            Type = vType,
                            Scope = vb.Scope,
                            Modifier = vb.Modifier,
                        }
                    );
            }
        }
        return result;
    }

    private List<ParameterInfo> CollectParameterInfos(List<VarDeclBlockNode> blocks)
    {
        var result = new List<ParameterInfo>();
        foreach (var vb in blocks)
        {
            var direction = vb.Scope switch
            {
                VarScope.Input => "VAR_INPUT",
                VarScope.Output => "VAR_OUTPUT",
                VarScope.InOut => "VAR_IN_OUT",
                _ => "VAR",
            };

            foreach (var item in vb.Items)
            {
                var typeName = GetDataTypeDisplayString(item.TypeName);
                foreach (var vn in item.Names)
                {
                    result.Add(
                        new ParameterInfo(
                            Name: vn.Name,
                            TypeName: typeName,
                            Direction: direction,
                            FilePath: vn.Span.FileName,
                            Line: vn.Span.Line
                        )
                    );
                }
            }
        }
        return result;
    }

    private static IECType Widen(IECType a, IECType b)
    {
        if (a.Kind == b.Kind)
            return a;
        if (a.IsReal && b.IsReal)
            return a.BitWidth >= b.BitWidth ? a : b;
        if (a.IsReal)
            return a;
        if (b.IsReal)
            return b;
        return a.BitWidth >= b.BitWidth ? a : b;
    }

    private static int EvaluateIntLiteral(ExpressionNode expr)
    {
        if (expr is LiteralExprNode lit)
            return lit.ParsedValue switch
            {
                long l => (int)l,
                int i => i,
                _ => 0,
            };
        return 0;
    }

    // ═══════════════════════════════════════
    //  显示字符串工具方法
    // ═══════════════════════════════════════

    private static string GetDataTypeDisplayString(DataTypeNode? dataType)
    {
        if (dataType == null)
            return "UNKNOWN";
        return dataType switch
        {
            SimpleDataTypeNode s => s.SizeParam != null
                ? $"{s.TypeName}(?)"
                : s.TypeName ?? "UNKNOWN",
            ArrayDataTypeNode a =>
                $"ARRAY OF {(a.ElementType != null ? GetDataTypeDisplayString(a.ElementType) : "?")}",
            ReferenceDataTypeNode r => $"REF_TO {r.ReferencedTypeName ?? "?"}",
            _ => dataType.ToString() ?? "UNKNOWN",
        };
    }

    private static string GetIECTypeDisplayString(IECType? type)
    {
        if (type == null)
            return "UNKNOWN";
        return type.Kind switch
        {
            IECType.KindTag.BOOL => "BOOL",
            IECType.KindTag.BYTE => "BYTE",
            IECType.KindTag.WORD => "WORD",
            IECType.KindTag.DWORD => "DWORD",
            IECType.KindTag.LWORD => "LWORD",
            IECType.KindTag.SINT => "SINT",
            IECType.KindTag.INT => "INT",
            IECType.KindTag.DINT => "DINT",
            IECType.KindTag.LINT => "LINT",
            IECType.KindTag.USINT => "USINT",
            IECType.KindTag.UINT => "UINT",
            IECType.KindTag.UDINT => "UDINT",
            IECType.KindTag.ULINT => "ULINT",
            IECType.KindTag.REAL => "REAL",
            IECType.KindTag.LREAL => "LREAL",
            IECType.KindTag.TIME => "TIME",
            IECType.KindTag.DATE => "DATE",
            IECType.KindTag.STRUCT => $"STRUCT({type.Fields?.Count ?? 0} fields)",
            IECType.KindTag.ARRAY => $"ARRAY OF {GetIECTypeDisplayString(type.ElementType)}",
            IECType.KindTag.ENUM => type.EnumMemberValues != null
                ? $"ENUM({type.Name})[{string.Join(", ", type.EnumMemberValues.Select(kv => $"{kv.Key}={kv.Value}"))}]"
                : $"ENUM({type.EnumValues?.Count ?? 0} members)",
            // ② 曾错读 ElementType（ARRAY 的字段），对引用恒为 null，
            // 于是所有 REFERENCE 的诊断文本都显示成 "REF_TO UNKNOWN"。
            IECType.KindTag.REFERENCE => $"REF_TO {GetIECTypeDisplayString(type.ReferencedType)}",
            IECType.KindTag.VOID => "VOID",
            _ => type.ToString() ?? "UNKNOWN",
        };
    }

    private static string SummarizeExpression(ExpressionNode expr) =>
        expr switch
        {
            LiteralExprNode lit => lit.RawText ?? (lit.ParsedValue?.ToString() ?? "?"),
            VariableExprNode v => v.Segments.Count > 0
                ? string.Join(".", v.Segments.Select(s => s.Name))
                : "?",
            FunctionCallExprNode fc => $"{fc.Name}(...)",
            BinaryExprNode bin =>
                $"{SummarizeExpression(bin.Left)} {FormatBinaryOp(bin.Op)} {SummarizeExpression(bin.Right)}",
            UnaryExprNode un => $"{FormatUnaryOp(un.Op)}{SummarizeExpression(un.Operand)}",
            _ => expr.GetType().Name.Replace("Node", ""),
        };

    private static string FormatBinaryOp(BinaryOp op) =>
        op switch
        {
            BinaryOp.Add => "+",
            BinaryOp.Sub => "-",
            BinaryOp.Mul => "*",
            BinaryOp.Div => "/",
            BinaryOp.Mod => "MOD",
            BinaryOp.Power => "**",
            BinaryOp.Eq => "=",
            BinaryOp.Neq => "<>",
            BinaryOp.Lt => "<",
            BinaryOp.Gt => ">",
            BinaryOp.Lte => "<=",
            BinaryOp.Gte => ">=",
            BinaryOp.And => "AND",
            BinaryOp.Or => "OR",
            BinaryOp.Xor => "XOR",
            _ => op.ToString(),
        };

    private static string FormatUnaryOp(UnaryOp op) =>
        op switch
        {
            UnaryOp.Not => "NOT ",
            UnaryOp.Neg => "-",
            _ => op.ToString(),
        };
}
