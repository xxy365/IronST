lexer grammar STLexer;

options {
    caseInsensitive = true;
}

// ═══════════════════════════════════════════════════════════════
//  NOTE: All keyword tokens MUST be defined BEFORE IDENTIFIER.
//  With caseInsensitive=true, [a-f] already covers A-F.
// ═══════════════════════════════════════════════════════════════

PROGRAM             : 'PROGRAM';
END_PROGRAM         : 'END_PROGRAM';
FUNCTION            : 'FUNCTION';
END_FUNCTION        : 'END_FUNCTION';
FUNCTION_BLOCK      : 'FUNCTION_BLOCK';
END_FUNCTION_BLOCK  : 'END_FUNCTION_BLOCK';
CONFIGURATION       : 'CONFIGURATION';
END_CONFIGURATION   : 'END_CONFIGURATION';
RESOURCE            : 'RESOURCE';
END_RESOURCE        : 'END_RESOURCE';
TASK                : 'TASK';
ACTION              : 'ACTION';
END_ACTION          : 'END_ACTION';

VAR                 : 'VAR';
END_VAR             : 'END_VAR';
VAR_INPUT           : 'VAR_INPUT';
VAR_OUTPUT          : 'VAR_OUTPUT';
VAR_IN_OUT          : 'VAR_IN_OUT';
VAR_GLOBAL          : 'VAR_GLOBAL';
VAR_EXTERNAL        : 'VAR_EXTERNAL';
VAR_TEMP            : 'VAR_TEMP';
VAR_ACCESS          : 'VAR_ACCESS';
VAR_CONFIG          : 'VAR_CONFIG';
CONSTANT            : 'CONSTANT';
RETAIN              : 'RETAIN';
NON_RETAIN          : 'NON_RETAIN';
AT                  : 'AT';

IF                  : 'IF';
THEN                : 'THEN';
ELSIF               : 'ELSIF';
ELSE                : 'ELSE';
END_IF              : 'END_IF';
FOR                 : 'FOR';
TO                  : 'TO';
BY                  : 'BY';
DO                  : 'DO';
END_FOR             : 'END_FOR';
WHILE               : 'WHILE';
END_WHILE           : 'END_WHILE';
REPEAT              : 'REPEAT';
UNTIL               : 'UNTIL';
END_REPEAT          : 'END_REPEAT';
CASE                : 'CASE';
OF                  : 'OF';
END_CASE            : 'END_CASE';
RETURN              : 'RETURN';
EXIT                : 'EXIT';

AND                 : 'AND';
OR                  : 'OR';
XOR                 : 'XOR';
NOT                 : 'NOT';
MOD                 : 'MOD';

TRUE                : 'TRUE';
FALSE               : 'FALSE';

BOOL                : 'BOOL';
BYTE                : 'BYTE';
WORD                : 'WORD';
DWORD               : 'DWORD';
LWORD               : 'LWORD';
SINT                : 'SINT';
INT                 : 'INT';
DINT                : 'DINT';
LINT                : 'LINT';
USINT               : 'USINT';
UINT                : 'UINT';
UDINT               : 'UDINT';
ULINT               : 'ULINT';
REAL                : 'REAL';
LREAL               : 'LREAL';
TIME                : 'TIME';
DATE                : 'DATE';
TIME_OF_DAY         : 'TIME_OF_DAY';
TOD                 : 'TOD';
DATE_AND_TIME       : 'DATE_AND_TIME';
DT                  : 'DT';
STRING              : 'STRING';
WSTRING             : 'WSTRING';

TYPE                : 'TYPE';
END_TYPE            : 'END_TYPE';
STRUCT              : 'STRUCT';
END_STRUCT          : 'END_STRUCT';
ARRAY               : 'ARRAY';
WITH                : 'WITH';
ON                  : 'ON';
REFERENCE           : 'REFERENCE';

// ─────────────────────────────────────────────────────────────
//  Time / Date Literals (consumed as single tokens)
// ─────────────────────────────────────────────────────────────

TIME_LITERAL
    : ('T' | 'TIME') '#' '-'? TIME_COMPONENT+
    ;

DATE_LITERAL
    : ('D' | 'DATE') '#'
      DIGIT DIGIT DIGIT DIGIT '-' DIGIT DIGIT '-' DIGIT DIGIT
    ;

TOD_LITERAL
    : ('TOD' | 'TIME_OF_DAY') '#'
      DIGIT DIGIT ':' DIGIT DIGIT ':' DIGIT DIGIT ('.' DIGIT+)?
    ;

DT_LITERAL
    : ('DT' | 'DATE_AND_TIME') '#'
      DIGIT DIGIT DIGIT DIGIT '-' DIGIT DIGIT '-' DIGIT DIGIT '-'
      DIGIT DIGIT ':' DIGIT DIGIT ':' DIGIT DIGIT ('.' DIGIT+)?
    ;

// ─────────────────────────────────────────────────────────────
//  Numeric Literals
//  NOTE: REAL_LITERAL must be before INTEGER_LITERAL (longest match).
// ─────────────────────────────────────────────────────────────

BASED_INTEGER
    : '2#'  [01] ('_'? [01])*
    | '8#'  [0-7] ('_'? [0-7])*
    | '10#' [0-9] ('_'? [0-9])*
    | '16#' [0-9a-f] ('_'? [0-9a-f])*
    ;

REAL_LITERAL
    : DIGIT ('_'? DIGIT)* '.' DIGIT ('_'? DIGIT)* ('E' [+\-]? DIGIT ('_'? DIGIT)*)?
    | DIGIT ('_'? DIGIT)* 'E' [+\-]? DIGIT ('_'? DIGIT)*
    ;

INTEGER_LITERAL
    : DIGIT ('_'? DIGIT)*
    ;

STRING_LITERAL
    : '\'' ( ~['\r\n] | '\'\'')* '\''
    | '"'  ( ~["\r\n] | '""')*   '"'
    ;

// ─────────────────────────────────────────────────────────────
//  Operators & Punctuation
// ─────────────────────────────────────────────────────────────

ASSIGN      : ':=';
RARROW      : '=>';
REF_ASSIGN  : 'REF=';
POWER       : '**';
LTE         : '<=';
GTE         : '>=';
NEQ         : '<>';
PLUS        : '+';
MINUS       : '-';
STAR        : '*';
SLASH       : '/';
EQ          : '=';
LT          : '<';
GT          : '>';
LPAREN      : '(';
RPAREN      : ')';
LBRACK      : '[';
RBRACK      : ']';
COMMA       : ',';
SEMI        : ';';
DOTDOT      : '..';
HASH        : '#';
DOT         : '.';
COLON       : ':';
CARET       : '^';

// ─────────────────────────────────────────────────────────────
//  Identifiers & Special
// ─────────────────────────────────────────────────────────────

DIRECT_VARIABLE
    : '%' [IQM] [XBWDL]? [0-9]+ ('.' [0-9]+)*
    ;

IDENTIFIER
    : [a-z_] [a-z0-9_]*
    ;

// ─────────────────────────────────────────────────────────────
//  Skip
// ─────────────────────────────────────────────────────────────

// 注释与空白保留在 HIDDEN channel：
// parser 仍然忽略它们，但 formatter 可以读取并原样保留注释/空行。
BLOCK_COMMENT
    : '(*' .*? '*)' -> channel(HIDDEN)
    ;

LINE_COMMENT
    : '//' ~[\r\n]* -> channel(HIDDEN)
    ;

WS
    : [ \t\r\n]+ -> channel(HIDDEN)
    ;

// ─────────────────────────────────────────────────────────────
//  Fragments
// ─────────────────────────────────────────────────────────────

fragment DIGIT
    : [0-9]
    ;

fragment TIME_COMPONENT
    : [0-9] ('_'? [0-9])* ('MS' | 'D' | 'H' | 'M' | 'S')
    ;
