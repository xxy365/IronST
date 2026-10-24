parser grammar STParser;

options {
    tokenVocab = STLexer;
}

// ═══════════════════════════════════════════════════════════════
//  Top Level
//  NOTE: "unused parser rule compilationUnit" is expected (entry point).
// ═══════════════════════════════════════════════════════════════

compilationUnit
    : ( typeDecl
      | programDecl
      | functionDecl
      | functionBlockDecl
      | configurationDecl
      )* EOF
    ;

// ═══════════════════════════════════════════════════════════════
//  Program Organization Units
// ═══════════════════════════════════════════════════════════════

programDecl
    : PROGRAM IDENTIFIER
      varDeclBlock*
      statementList
      END_PROGRAM
    ;

functionDecl
    : FUNCTION IDENTIFIER COLON typeName
      varDeclBlock*
      statementList
      END_FUNCTION
    ;

// [FIX #2] Allow body before actions and mixed ordering.
functionBlockDecl
    : FUNCTION_BLOCK IDENTIFIER
      varDeclBlock*
      (statement | SEMI | actionDecl)*
      END_FUNCTION_BLOCK
    ;

actionDecl
    : ACTION IDENTIFIER
      statementList
      END_ACTION
    ;

// ═══════════════════════════════════════════════════════════════
//  Configuration
// ═══════════════════════════════════════════════════════════════

configurationDecl
    : CONFIGURATION IDENTIFIER
      varDeclBlock*
      resourceDecl*
      END_CONFIGURATION
    ;

resourceDecl
    : RESOURCE IDENTIFIER ON IDENTIFIER
      varDeclBlock*
      taskDecl*
      programConfig*
      END_RESOURCE
    ;

taskDecl
    : TASK IDENTIFIER LPAREN taskParam (COMMA taskParam)* RPAREN SEMI
    ;

taskParam
    : IDENTIFIER ASSIGN expression
    ;

programConfig
    : PROGRAM IDENTIFIER (WITH IDENTIFIER)? COLON IDENTIFIER
      (LPAREN programConfigParam (COMMA programConfigParam)* RPAREN)?
      SEMI
    ;

programConfigParam
    : IDENTIFIER ASSIGN expression
    ;

// ═══════════════════════════════════════════════════════════════
//  Variable Declarations
// ═══════════════════════════════════════════════════════════════

varDeclBlock
    : varScope varModifier? varDeclItem+ END_VAR
    | VAR_CONFIG varConfigItem+ END_VAR
    ;

varScope
    : VAR
    | VAR_INPUT
    | VAR_OUTPUT
    | VAR_IN_OUT
    | VAR_GLOBAL
    | VAR_EXTERNAL
    | VAR_TEMP
    | VAR_ACCESS
    ;

varModifier
    : CONSTANT
    | RETAIN
    | NON_RETAIN
    ;

varDeclItem
    : varName (COMMA varName)* COLON dataType (ASSIGN initialValue)? SEMI
    ;

varConfigItem
    : varDeclItem
    | IDENTIFIER (DOT IDENTIFIER)* ASSIGN initialValue SEMI
    ;

initialValue
    : structInitValue
    | arrayInitValue
    | expression
    ;

structInitValue
    : LPAREN IDENTIFIER ASSIGN initialValue
      (COMMA IDENTIFIER ASSIGN initialValue)* RPAREN
    ;

arrayInitValue
    : LBRACK initialValue (COMMA initialValue)* RBRACK
    ;

varName
    : IDENTIFIER (AT DIRECT_VARIABLE)?
    ;

// ═══════════════════════════════════════════════════════════════
//  Type Declarations
// ═══════════════════════════════════════════════════════════════

// [FIX #4] Allow multiple type definitions in one TYPE...END_TYPE block.
typeDecl
    : TYPE (IDENTIFIER COLON typeDef SEMI)+ END_TYPE SEMI?
    ;

typeDef
    : structDef
    | enumDef
    | subrangeDef
    | dataType
    ;

structDef
    : STRUCT varDeclItem+ END_STRUCT
    ;

// [FIX #8] Support optional explicit enum values.
enumDef
    : LPAREN enumItem (COMMA enumItem)* RPAREN
    ;

enumItem
    : IDENTIFIER (ASSIGN expression)?
    ;

subrangeDef
    : typeName LPAREN subrangeBound DOTDOT subrangeBound RPAREN
    ;

// [FIX #6] STRING sizing: both [80] (IEC) and (80) (extension).
dataType
    : typeName ((LPAREN expression RPAREN) | (LBRACK expression RBRACK))?
    | ARRAY LBRACK subrange (COMMA subrange)* RBRACK OF dataType
    | REFERENCE TO typeName
    ;

builtinType
    : BOOL
    | BYTE   | WORD  | DWORD  | LWORD
    | SINT   | INT   | DINT   | LINT
    | USINT  | UINT  | UDINT  | ULINT
    | REAL   | LREAL
    | TIME
    | DATE
    | TIME_OF_DAY | TOD
    | DATE_AND_TIME | DT
    | STRING | WSTRING
    ;

typeName
    : builtinType
    | IDENTIFIER
    ;

subrange
    : subrangeBound DOTDOT subrangeBound
    ;

subrangeBound
    : MINUS? (INTEGER_LITERAL | BASED_INTEGER)
    ;

// ═══════════════════════════════════════════════════════════════
//  Statements
// ═══════════════════════════════════════════════════════════════

statementList
    : (statement | SEMI)*
    ;

// [FIX #3] SEMI? after compound statements for END_IF; END_FOR; etc.
statement
    : assignmentStmt    SEMI
    | refAssignStmt     SEMI
    | callStmt          SEMI
    | returnStmt        SEMI
    | exitStmt          SEMI
    | ifStmt      SEMI?
    | forStmt     SEMI?
    | whileStmt   SEMI?
    | repeatStmt  SEMI?
    | caseStmt    SEMI?
    ;

refAssignStmt
    : variable REF_ASSIGN expression
    ;

assignmentStmt
    : variable ASSIGN expression
    ;

callStmt
    : (builtinType | variable) LPAREN paramList? RPAREN
    ;

returnStmt
    : RETURN
    ;

exitStmt
    : EXIT
    ;

ifStmt
    : IF expression THEN statementList
      (ELSIF expression THEN statementList)*
      (ELSE statementList)?
      END_IF
    ;

forStmt
    : FOR IDENTIFIER ASSIGN expression
           TO expression
           (BY expression)?
      DO statementList
      END_FOR
    ;

whileStmt
    : WHILE expression DO statementList END_WHILE
    ;

repeatStmt
    : REPEAT statementList UNTIL expression END_REPEAT
    ;

caseStmt
    : CASE expression OF
      caseElement+
      (ELSE statementList)?
      END_CASE
    ;

caseElement
    : caseLabel (COMMA caseLabel)* COLON statementList
    ;

// [FIX #7] Support qualified enum names in CASE labels.
caseLabel
    : subrangeBound (DOTDOT subrangeBound)?
    | IDENTIFIER (DOT IDENTIFIER)?
    ;

paramList
    : paramAssignment (COMMA paramAssignment)*
    ;

paramAssignment
    : IDENTIFIER RARROW variable
    | IDENTIFIER ASSIGN expression
    | expression
    ;

// ═══════════════════════════════════════════════════════════════
//  Expressions
//  Precedence (low → high):
//    OR → XOR → AND → comparison → add → mul → unary → power → postfix → primary
//
//  [FIX #1] mulExpr now calls unaryExpr (not powerExpr).
//  This ensures NOT and unary MINUS are reachable from the
//  main expression chain, not only after **.
// ═══════════════════════════════════════════════════════════════

expression
    : xorExpr (OR xorExpr)*
    ;

xorExpr
    : andExpr (XOR andExpr)*
    ;

andExpr
    : comparisonExpr (AND comparisonExpr)*
    ;

comparisonExpr
    : addExpr (compOp addExpr)?
    ;

compOp
    : EQ | NEQ | LT | GT | LTE | GTE
    ;

addExpr
    : mulExpr ((PLUS | MINUS) mulExpr)*
    ;

// [FIX #1] Uses unaryExpr instead of powerExpr.
mulExpr
    : unaryExpr ((STAR | SLASH | MOD) unaryExpr)*
    ;

// [FIX #1] Power has higher precedence than unary minus.
// -3**2 → -(3**2) = -9
powerExpr
    : postfixExpr (POWER unaryExpr)?
    ;

unaryExpr
    : (NOT | MINUS) unaryExpr
    | powerExpr
    ;

postfixExpr
    : primaryExpr
      ( LPAREN paramList? RPAREN
      | LBRACK expressionList RBRACK
      )*
    | builtinType LPAREN paramList? RPAREN
      (LBRACK expressionList RBRACK)*
    ;

primaryExpr
    : literal
    | typedLiteral
    | variable
    | LPAREN expression RPAREN
    ;

// [FIX] Added MINUS? to support negative typed literals like DINT#-100.
typedLiteral
    : typeName HASH typedLiteralValue
    ;

typedLiteralValue
    : MINUS? (INTEGER_LITERAL | REAL_LITERAL)
    | BASED_INTEGER
    | TRUE
    | FALSE
    | STRING_LITERAL
    ;

variable
    : IDENTIFIER (CARET | LBRACK expressionList RBRACK)*
      (DOT IDENTIFIER (CARET | LBRACK expressionList RBRACK)*)*
    ;

literal
    : INTEGER_LITERAL
    | BASED_INTEGER
    | REAL_LITERAL
    | TRUE
    | FALSE
    | STRING_LITERAL
    | TIME_LITERAL
    | DATE_LITERAL
    | TOD_LITERAL
    | DT_LITERAL
    ;

expressionList
    : expression (COMMA expression)*
    ;
