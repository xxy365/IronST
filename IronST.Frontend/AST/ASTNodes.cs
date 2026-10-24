using IronST.Common.Diagnostics;
using IronST.Common.Types;
using IronST.Common.Symbol;

namespace IronST.Frontend.AST;

// ═══════════════════════════════════════════════════════
//  基础
// ═══════════════════════════════════════════════════════

public abstract class ASTNode
{
    public SourceSpan Span { get; set; } = SourceSpan.Unknown;
}

public abstract class ExpressionNode : ASTNode
{
    public IECType? ResolvedType { get; set; }
}

public abstract class StatementNode : ASTNode { }

// ═══════════════════════════════════════════════════════
//  顶层 & POU
// ═══════════════════════════════════════════════════════

// compilationUnit : (typeDecl | programDecl | functionDecl | functionBlockDecl | configurationDecl)* EOF
public class CompilationUnitNode : ASTNode
{
    public List<TypeDeclNode> TypeDeclarations { get; } = new();
    public List<POUNode> POUs { get; } = new();
    public List<ConfigurationNode> Configurations { get; } = new();
}

public abstract class POUNode : ASTNode
{
    public string Name { get; set; } = "";
    public List<VarDeclBlockNode> VarBlocks { get; } = new();
    public List<StatementNode> Body { get; } = new();
}

// programDecl : PROGRAM id varDeclBlock* statementList END_PROGRAM
public class ProgramNode : POUNode { }

// functionDecl : FUNCTION id : returnType varDeclBlock* statementList END_FUNCTION
public class FunctionNode : POUNode
{
    public string ReturnTypeName { get; set; } = "";
    public IECType? ReturnType { get; set; }
}

// ═══════════════════════════════════════════════════════
//  Function Block（保真交错顺序）
// ═══════════════════════════════════════════════════════

// functionBlockDecl : FUNCTION_BLOCK id varDeclBlock* (statement | ; | actionDecl)* END_FUNCTION_BLOCK
// BodyItems 按源码顺序存储 statement 和 actionDecl 的交替序列，
// 保持与源码中 (statement | ; | actionDecl)* 完全一致的顺序。

public abstract class FBBodyItemNode : ASTNode { }

public class FBStatementItemNode : FBBodyItemNode
{
    public FBStatementItemNode(StatementNode statement) { Statement = statement; }
    public StatementNode Statement { get; }
}

public class FBActionItemNode : FBBodyItemNode
{
    public FBActionItemNode(ActionNode action) { Action = action; }
    public ActionNode Action { get; }
}

public class FunctionBlockNode : POUNode
{
    /// <summary>
    /// 按源码顺序存储的 body 项列表。
    /// 包含 FBStatementItemNode 和 FBActionItemNode 的交错序列。
    /// </summary>
    public List<FBBodyItemNode> BodyItems { get; } = new();

    /// <summary>
    /// 便捷属性：提取所有语句（忽略 action）。
    /// </summary>
    public IEnumerable<StatementNode> Statements =>
        BodyItems.OfType<FBStatementItemNode>().Select(i => i.Statement);

    /// <summary>
    /// 便捷属性：提取所有 Action（忽略语句）。
    /// </summary>
    public IEnumerable<ActionNode> Actions =>
        BodyItems.OfType<FBActionItemNode>().Select(i => i.Action);
}

// actionDecl : ACTION id statementList END_ACTION
public class ActionNode : ASTNode
{
    public string Name { get; set; } = "";
    public List<StatementNode> Body { get; } = new();
}

// ═══════════════════════════════════════════════════════
//  Configuration
// ═══════════════════════════════════════════════════════

// configurationDecl : CONFIGURATION id varDeclBlock* resourceDecl* END_CONFIGURATION
public class ConfigurationNode : ASTNode
{
    public string Name { get; set; } = "";
    public List<VarDeclBlockNode> VarBlocks { get; } = new();
    public List<ResourceNode> Resources { get; } = new();
}

// resourceDecl : RESOURCE id ON id varDeclBlock* taskDecl* programConfig* END_RESOURCE
public class ResourceNode : ASTNode
{
    public string Name { get; set; } = "";
    public string TaskControllerName { get; set; } = "";
    public List<VarDeclBlockNode> VarBlocks { get; } = new();
    public List<TaskNode> Tasks { get; } = new();
    public List<ProgramConfigNode> ProgramConfigs { get; } = new();
}

// taskDecl : TASK id (paramAssignment, ...)? ;
public class TaskNode : ASTNode
{
    public string Name { get; set; } = "";
    public List<CallParamNode> Parameters { get; } = new();
}

// programConfig : PROGRAM (id (WITH id)?)? : typeName (paramAssignment, ...)? ;
public class ProgramConfigNode : ASTNode
{
    public string? ConfigName { get; set; }
    public string? TaskBinding { get; set; }
    public string ProgramTypeName { get; set; } = "";
    public List<CallParamNode> Parameters { get; } = new();
}

// ═══════════════════════════════════════════════════════
//  变量声明
// ═══════════════════════════════════════════════════════

// varDeclBlock : varScope varModifier? varDeclItem+ END_VAR
//            |  VAR_CONFIG varConfigItem+ END_VAR
public class VarDeclBlockNode : ASTNode
{
    public VarScope Scope { get; set; }
    public VarModifier Modifier { get; set; }

    /// <summary>
    /// true 表示此块是 VAR_CONFIG ... END_VAR，
    /// 此时使用 ConfigItems 而非 Items。
    /// </summary>
    public bool IsConfigBlock { get; set; }

    /// <summary>普通声明项（VAR / VAR_INPUT / ...）</summary>
    public List<VarDeclItemNode> Items { get; } = new();

    /// <summary>配置声明项（仅 VAR_CONFIG 块使用）</summary>
    public List<VarConfigItemNode> ConfigItems { get; } = new();
}

// varDeclItem : varName (',' varName)* : dataType (:= initialValue)? ;
public class VarDeclItemNode : ASTNode
{
    public VarDeclItemNode(DataTypeNode typeName) { TypeName = typeName; }

    public List<VarNameNode> Names { get; } = new();
    public DataTypeNode TypeName { get; }

    /// <summary>
    /// expression | structInitValue | arrayInitValue
    /// </summary>
    public ASTNode? InitialValue { get; set; }
}

// varName : id (AT DIRECT_VARIABLE)?
public class VarNameNode : ASTNode
{
    public string Name { get; set; } = "";
    public string? DirectAddress { get; set; }
}

// varConfigItem（VAR_CONFIG 块中的单条配置项）
// 将程序实例变量映射到物理地址并可设初始值
public class VarConfigItemNode : ASTNode
{
    public string Name { get; set; } = "";

    /// <summary>赋值形式的 VarConfigItem 不设置 TypeName，故为可空。</summary>
    public DataTypeNode? TypeName { get; set; }

    public ExpressionNode? InitialValue { get; set; }
    public string? DirectAddress { get; set; }
}

// ═══════════════════════════════════════════════════════
//  初始值（结构体 / 数组初始化）
// ═══════════════════════════════════════════════════════

// structInitValue : '(' field ':=' initialValue (',' field ':=' initialValue)* ')'
public class StructInitValueNode : ASTNode
{
    public List<(string Name, ASTNode Value)> Fields { get; } = new();
}

// arrayInitValue : '[' initialValue (',' initialValue)* ']'
public class ArrayInitValueNode : ASTNode
{
    public List<ASTNode> Values { get; } = new();
}

// ═══════════════════════════════════════════════════════
//  类型声明
// ═══════════════════════════════════════════════════════

// typeDecl : TYPE (id : typeDef ;)+ END_TYPE
// 一个 TYPE 块可定义多个类型，每个 id : typeDef 对对应一个 TypeDeclNode
public class TypeDeclNode : ASTNode
{
    public TypeDeclNode(string name, TypeDefNode definition) { Name = name; Definition = definition; }

    public string Name { get; set; }
    public TypeDefNode Definition { get; }
}

// typeDef : structDef | enumDef | subrangeDef | dataType
public abstract class TypeDefNode : ASTNode { }

// structDef : STRUCT varDeclItem+ END_STRUCT
public class StructDefNode : TypeDefNode
{
    public List<VarDeclItemNode> Members { get; } = new();
}

// enumDef : '(' enumItem (',' enumItem)* ')'
public class EnumDefNode : TypeDefNode
{
    public List<EnumItemNode> Items { get; } = new();
}

// enumItem : id ('=' expression)?
public class EnumItemNode : ASTNode
{
    public string Name { get; set; } = "";
    public ExpressionNode? ExplicitValue { get; set; }
}

// subrangeDef : typeName '(' subrangeBound .. subrangeBound ')'
public class SubrangeDefNode : TypeDefNode
{
    public string BaseTypeName { get; set; } = "";
    public long LowerBound { get; set; }
    public long UpperBound { get; set; }
}

// typeDef 的 dataType 变体: 简单类型别名
public class SimpleTypeDefNode : TypeDefNode
{
    public SimpleTypeDefNode(DataTypeNode underlyingType) { UnderlyingType = underlyingType; }

    public DataTypeNode UnderlyingType { get; }
}

// ═══════════════════════════════════════════════════════
//  数据类型引用
// ═══════════════════════════════════════════════════════

// dataType : typeName ('(' expression ')' | '[' expression ']')?
//          | ARRAY '[' subrange (',' subrange)* ']' OF dataType
//          | REFERENCE TO typeName
public abstract class DataTypeNode : ASTNode { }

// typeName ('(' expression ')' | '[' expression ']')?   如 STRING[80], STRING(80)
public class SimpleDataTypeNode : DataTypeNode
{
    public string TypeName { get; set; } = "";
    public ExpressionNode? SizeParam { get; set; }
}

// ARRAY '[' subrange (',' subrange)* ']' OF dataType
public class ArrayDataTypeNode : DataTypeNode
{
    public ArrayDataTypeNode(DataTypeNode elementType) { ElementType = elementType; }

    public List<(ExpressionNode Lower, ExpressionNode Upper)> Dimensions { get; } = new();
    public DataTypeNode ElementType { get; }
}

// REFERENCE TO typeName
public class ReferenceDataTypeNode : DataTypeNode
{
    public string ReferencedTypeName { get; set; } = "";
}

// ═══════════════════════════════════════════════════════
//  表达式枚举
// ═══════════════════════════════════════════════════════

public enum BinaryOp
{
    Add, Sub,       // addExpr:  + -
    Mul, Div, Mod,  // mulExpr:  * / MOD
    Power,          // powerExpr: **
    Eq, Neq,        // comparisonExpr: = <>
    Lt, Gt, Lte, Gte,
    And, Or, Xor,   // andExpr / xorExpr / expression(OR)
}

public enum UnaryOp { Not, Neg }

public enum LiteralKind
{
    Integer, Real, BasedInteger, Boolean, String,
    Time, Date, TimeOfDay, DateTime,
}

// ═══════════════════════════════════════════════════════
//  表达式
// ═══════════════════════════════════════════════════════

// binaryExpr : left op right
public class BinaryExprNode : ExpressionNode
{
    public BinaryExprNode(BinaryOp op, ExpressionNode left, ExpressionNode right)
    { Op = op; Left = left; Right = right; }

    public BinaryOp Op { get; set; }
    public ExpressionNode Left { get; }
    public ExpressionNode Right { get; }
}

// unaryExpr : (NOT | -) unaryExpr
public class UnaryExprNode : ExpressionNode
{
    public UnaryExprNode(UnaryOp op, ExpressionNode operand)
    { Op = op; Operand = operand; }

    public UnaryOp Op { get; set; }
    public ExpressionNode Operand { get; }
}

// literal : INTEGER | BASED_INTEGER | REAL | TRUE | FALSE | STRING | TIME | DATE | TOD | DT
public class LiteralExprNode : ExpressionNode
{
    public LiteralKind Kind { get; set; }
    public string RawText { get; set; } = "";
    public object? ParsedValue { get; set; }

    /// <summary>默认构造 —— 仅供 ASTFactory 内部使用。</summary>
    public LiteralExprNode() { }

    public LiteralExprNode(LiteralKind kind, string raw, object? parsed)
    { Kind = kind; RawText = raw; ParsedValue = parsed; }
}

// typedLiteral : typeName '#' typedLiteralValue
public class TypedLiteralExprNode : ExpressionNode
{
    public string TypeName { get; set; } = "";
    public string RawValue { get; set; } = "";
}

// ═══════════════════════════════════════════════════════
//  变量访问
// ═══════════════════════════════════════════════════════

// variable : IDENTIFIER suffix* ('.' IDENTIFIER suffix*)*
// suffix   : '^'                     → dereference
//          | '[' expressionList ']'   → array subscript
public class VariableExprNode : ExpressionNode
{
    public List<VariableSegmentNode> Segments { get; } = new();
}

public class VariableSegmentNode : ASTNode
{
    public string Name { get; set; } = "";
    public List<VariableSuffixNode> Suffixes { get; } = new();
}

public class VariableSuffixNode : ASTNode
{
    /// <summary>true = '^'（解引用）</summary>
    public bool IsDereference { get; set; }

    /// <summary>non-null = '[' expressionList ']'（数组下标）</summary>
    public List<ExpressionNode>? Subscripts { get; set; }
}

// ═══════════════════════════════════════════════════════
//  函数 / 调用
// ═══════════════════════════════════════════════════════

// callStmt : (builtinType | variable) '(' paramList? ')'
// postfixExpr 也产生此节点: builtinType '(' paramList? ')' ('[' expressionList ']')*
public class FunctionCallExprNode : ExpressionNode
{
    public string Name { get; set; } = "";
    public List<CallParamNode> Arguments { get; } = new();

    /// <summary>
    /// builtinType(args)[i, j] — 类型转换后的数组下标访问
    /// </summary>
    public List<ExpressionNode>? Subscripts { get; set; }
}

// paramAssignment : id => variable | id := expression | expression
public class CallParamNode : ASTNode
{
    public CallParamNode(ExpressionNode value) { Value = value; }

    public string? Name { get; set; }         // ← arg.Name → 形参名（命名参数）
    public ExpressionNode Value { get; }      // ← arg.Value → 实参表达式
    public bool IsOutputBinding { get; set; } // ← arg.IsOutputBinding → true => "=>", false => ":="
}


// ═══════════════════════════════════════════════════════
//  语句
// ═══════════════════════════════════════════════════════

// assignmentStmt : variable ':=' expression
public class AssignmentNode : StatementNode
{
    public AssignmentNode(ExpressionNode target, ExpressionNode value)
    { Target = target; Value = value; }

    public ExpressionNode Target { get; }
    public ExpressionNode Value { get; }
}

// refAssignStmt : variable 'REF=' expression
public class RefAssignmentNode : StatementNode
{
    public RefAssignmentNode(ExpressionNode target, ExpressionNode value)
    { Target = target; Value = value; }

    public ExpressionNode Target { get; }
    public ExpressionNode Value { get; }
}

// callStmt : (builtinType | variable) '(' paramList? ')'
public class CallStatementNode : StatementNode
{
    public string Name { get; set; } = "";
    public List<CallParamNode> Arguments { get; } = new();
}

// returnStmt : RETURN
public class ReturnNode : StatementNode { }

// exitStmt : EXIT
public class ExitNode : StatementNode { }

// ifStmt : IF expr THEN stmts (ELSIF expr THEN stmts)* (ELSE stmts)? END_IF
public class IfNode : StatementNode
{
    public IfNode(ExpressionNode condition) { Condition = condition; }

    public ExpressionNode Condition { get; }
    public List<StatementNode> ThenBody { get; } = new();
    public List<(ExpressionNode Condition, List<StatementNode> Body)> ElsIfBranches { get; } = new();
    public List<StatementNode>? ElseBody { get; set; }
}

// forStmt : FOR id := expr TO expr (BY expr)? DO stmts END_FOR
public class ForNode : StatementNode
{
    public ForNode(string variable, ExpressionNode from, ExpressionNode to)
    { Variable = variable; From = from; To = to; }

    public string Variable { get; }
    public ExpressionNode From { get; }
    public ExpressionNode To { get; }
    public ExpressionNode? By { get; set; }
    public List<StatementNode> Body { get; } = new();
}

// whileStmt : WHILE expr DO stmts END_WHILE
public class WhileNode : StatementNode
{
    public WhileNode(ExpressionNode condition) { Condition = condition; }

    public ExpressionNode Condition { get; }
    public List<StatementNode> Body { get; } = new();
}

// repeatStmt : REPEAT stmts UNTIL expr END_REPEAT
public class RepeatNode : StatementNode
{
    public List<StatementNode> Body { get; } = new();

    /// <summary>
    /// Visitor 先填充 Body 再设置此字段，无法在构造时确定。
    /// 语义分析阶段应验证此字段非 null。
    /// </summary>
    public ExpressionNode? UntilCondition { get; set; }
}

// caseStmt : CASE expr OF caseElement+ (ELSE stmts)? END_CASE
public class CaseNode : StatementNode
{
    public CaseNode(ExpressionNode selector) { Selector = selector; }

    public ExpressionNode Selector { get; }
    public List<CaseBranchNode> Branches { get; } = new();
    public List<StatementNode>? ElseBody { get; set; }
}

// caseElement : caseLabel (',' caseLabel)* : statementList
public class CaseBranchNode : ASTNode
{
    public List<CaseLabelNode> Labels { get; } = new();
    public List<StatementNode> Body { get; } = new();
}

// caseLabel : subrangeBound (.. subrangeBound)? | id (. id)?
public class CaseLabelNode : ASTNode
{
    public long? SingleValue { get; set; }
    public long? RangeStart { get; set; }
    public long? RangeEnd { get; set; }
    public string? EnumValue { get; set; }
}
