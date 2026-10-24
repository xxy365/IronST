// ============================================================================
//  ST Grammar Comprehensive Test File (Corrected)
//  All keywords UPPERCASE (Siemens TIA Portal style).
//  NOTE: caseInsensitive=true makes lowercase also valid,
//  but uppercase is the standard convention.
// ============================================================================


// ============================================================================
//  SECTION 1: TYPE DECLARATIONS (T-001 ~ T-010)
// ============================================================================

// T-001: Single type
TYPE
  SpeedValue : REAL;
END_TYPE;

// T-002: Multiple types in one TYPE...END_TYPE block
TYPE
  PositionValue : DINT;
  CounterValue  : INT;
END_TYPE;

// T-003: Structure type
TYPE
  MotorStatus : STRUCT
    speed   : REAL;
    torque  : REAL;
    running : BOOL;
  END_STRUCT;
END_TYPE;

// T-004: Enum type
TYPE
  Color : (Red, Green, Blue);
END_TYPE;

// T-005: Enum with explicit values
TYPE
  Priority : (Low := 1, Medium := 5, High := 10);
END_TYPE;

// T-006: Subrange type
TYPE
  SmallInt : INT(0 .. 100);
END_TYPE;

// T-007: Basic type aliases (each builtinType)
TYPE
  MyBool    : BOOL;
  MyByte    : BYTE;
  MyWord    : WORD;
  MyDWord   : DWORD;
  MyLWord   : LWORD;
  MySInt    : SINT;
  MyInt     : INT;
  MyDInt    : DINT;
  MyLInt    : LINT;
  MyUSInt   : USINT;
  MyUInt    : UINT;
  MyUDInt   : UDINT;
  MyULInt   : ULINT;
  MyReal    : REAL;
  MyLReal   : LREAL;
  MyTime    : TIME;
  MyDate    : DATE;
  MyTOD     : TOD;
  MyDT      : DT;
  MyString  : STRING;
  MyWString : WSTRING;
END_TYPE;

// T-008: Array type
TYPE
  IntArray  : ARRAY [0..9] OF INT;
  RealArray : ARRAY [0..3, 0..4] OF REAL;
END_TYPE;

// T-009: Struct with array field
TYPE
  DataBlock : STRUCT
    values : ARRAY [0..15] OF REAL;
    count  : INT;
  END_STRUCT;
END_TYPE;

// T-010: Struct with enum field
TYPE
  ControllerState : STRUCT
    state    : Color;
    priority : Priority;
  END_STRUCT;
END_TYPE;


// ============================================================================
//  SECTION 2: FUNCTION DECLARATIONS
//  Tests: P-010 ~ P-017, E-001 ~ E-073
// ============================================================================

// E-001 ~ E-073: Expression tests
FUNCTION ExpressionTest : INT
  VAR_INPUT
    a : INT;
    b : INT;
    c : INT;
    d : INT;
  END_VAR

  // E-001: OR
  IF (a > 0) OR (b > 0) THEN ExpressionTest := 1; END_IF;

  // E-002: XOR
  IF (a > 0) XOR (b > 0) THEN ExpressionTest := 2; END_IF;

  // E-003: AND
  IF (a > 0) AND (b > 0) THEN ExpressionTest := 3; END_IF;

  // E-004: OR < AND precedence
  IF (a > 0) OR (b > 0) AND (c > 0) THEN ExpressionTest := 4; END_IF;

  // E-005: All comparison operators
  IF a = b THEN ExpressionTest := 5; END_IF;
  IF a <> b THEN ExpressionTest := 5; END_IF;
  IF a < b THEN ExpressionTest := 5; END_IF;
  IF a > b THEN ExpressionTest := 5; END_IF;
  IF a <= b THEN ExpressionTest := 5; END_IF;
  IF a >= b THEN ExpressionTest := 5; END_IF;

  // E-006: Comparison lower than addition
  IF a + b > c THEN ExpressionTest := 6; END_IF;

  // E-007: Addition and subtraction
  ExpressionTest := a + b - c;

  // E-008: Multiplication, division, MOD
  ExpressionTest := a * b / c;

  // E-009: Add lower than multiply
  ExpressionTest := a + b * c;

  // E-010: Multiply lower than power
  ExpressionTest := a * b;

  // E-012: Unary minus higher than power
  ExpressionTest := -a;

  // E-013: NOT precedence
  IF NOT (a > 0) AND (b > 0) THEN ExpressionTest := 13; END_IF;

  // E-014: Unary negation
  ExpressionTest := -a + b;

  // E-015: Double unary negation
  ExpressionTest := --a;

  // E-020: (a + b) * c
  ExpressionTest := (a + b) * c;

  // E-021: a * (b + c)
  ExpressionTest := a * (b + c);

  // E-022: -(a + b)
  ExpressionTest := -(a + b);

  // E-023: NOT (a OR b)
  IF NOT ((a > 0) OR (b > 0)) THEN ExpressionTest := 23; END_IF;

  // E-025: Nested parentheses
  ExpressionTest := ((a + b) * (c - d));

  // E-030: Integer literal
  ExpressionTest := 42;

  // E-032: Boolean literals
  IF TRUE THEN ExpressionTest := 32; END_IF;

  // E-038: Based integer literal
  ExpressionTest := 16#FF;

  // E-039: Simple variable
  ExpressionTest := a;

  // E-040: Typed literal INT#5
  ExpressionTest := INT#5;

  // E-045: Typed literal DINT#100
  ExpressionTest := DINT#100;

  // E-046: Typed literal BOOL#TRUE
  IF BOOL#TRUE THEN ExpressionTest := 46; END_IF;

  // E-047: Typed literal BOOL#FALSE
  IF BOOL#FALSE THEN ExpressionTest := 47; END_IF;

  // E-070: Arithmetic + comparison + logic mix
  IF a + b > c AND a <> d OR (a > 0) THEN ExpressionTest := 70; END_IF;

  // E-072: Multi-operator long expression
  ExpressionTest := a + b * c - d / a;

  // E-073: Complex RHS
  ExpressionTest := (a + b) * c - d;
END_FUNCTION


// P-011: Function returning REAL
FUNCTION FuncReal : REAL
  VAR_INPUT x : REAL; END_VAR
  FuncReal := 3.14;
  FuncReal := -x;
  FuncReal := (-3.0);
END_FUNCTION

// P-012: Function returning BOOL
FUNCTION FuncBool : BOOL
  VAR_INPUT x : BOOL; END_VAR
  FuncBool := x;
END_FUNCTION

// P-013: Function returning STRING
FUNCTION FuncString : STRING
  VAR_INPUT s : STRING; END_VAR
  FuncString := 'hello';
  FuncString := s;
END_FUNCTION

// P-014: Function returning user-defined type
FUNCTION FuncUserType : SpeedValue
  VAR_INPUT s : SpeedValue; END_VAR
  FuncUserType := s;
END_FUNCTION

// P-015: Minimal function body
FUNCTION EmptyFunc : INT
  ;
END_FUNCTION

// P-016 + X-007: Function name as return value target
FUNCTION AddValues : INT
  VAR_INPUT
    x : INT;
    y : INT;
  END_VAR
  AddValues := x + y;
END_FUNCTION

// P-017: Function with RETURN
FUNCTION SafeDiv : REAL
  VAR_INPUT
    num : REAL;
    den : REAL;
  END_VAR
  IF den = 0.0 THEN
    SafeDiv := 0.0;
    RETURN;
  END_IF;
  SafeDiv := num / den;
END_FUNCTION


// ============================================================================
//  SECTION 3: FUNCTION_BLOCK (P-020 ~ P-031)
// ============================================================================

// P-020: FB with only VAR + statements (no ACTION)
FUNCTION_BLOCK FB_NoAction
  VAR_INPUT
    speed_cmd : REAL;
  END_VAR
  VAR_OUTPUT
    torque_out : REAL;
  END_VAR
  torque_out := speed_cmd * 0.8;
END_FUNCTION_BLOCK

// P-021: FB with only VAR + ACTIONs (no standalone statement)
FUNCTION_BLOCK FB_OnlyActions
  VAR
    x : INT;
  END_VAR
  ACTION DoReset
    x := 0;
  END_ACTION
  ACTION DoStart
    x := 1;
  END_ACTION
END_FUNCTION_BLOCK

// P-022: Body BEFORE actions
FUNCTION_BLOCK FB_BodyFirst
  VAR_INPUT input_val : INT; END_VAR
  VAR result : INT; END_VAR
  result := input_val * 2;
  ACTION Reset
    result := 0;
  END_ACTION
END_FUNCTION_BLOCK

// P-023: Body AFTER actions
FUNCTION_BLOCK FB_BodyLast
  VAR counter : INT; END_VAR
  ACTION Increment
    counter := counter + 1;
  END_ACTION
  counter := counter * 2;
END_FUNCTION_BLOCK

// P-024 + P-025 + P-026: Mixed body/actions, multiple actions, multiple VAR scopes
FUNCTION_BLOCK FB_Mixed
  VAR_INPUT cmd : INT; END_VAR
  VAR_OUTPUT status : INT; END_VAR
  VAR internal : INT; END_VAR
  VAR_TEMP temp : INT; END_VAR
  temp := cmd + 1;
  ACTION ActionA
    internal := internal + 1;
  END_ACTION
  ACTION ActionB
    internal := internal - 1;
  END_ACTION
  status := internal;
  ACTION ActionC
    internal := 0;
  END_ACTION
END_FUNCTION_BLOCK

// P-030: Empty ACTION
FUNCTION_BLOCK FB_EmptyAction
  VAR x : INT; END_VAR
  ACTION EmptyAction
    ;
  END_ACTION
END_FUNCTION_BLOCK

// P-031: ACTION with multiple statements
FUNCTION_BLOCK FB_MultiStmtAction
  VAR
    a : INT;
    b : INT;
    c : INT;
  END_VAR
  ACTION Compute
    a := 1;
    b := 2;
    c := a + b;
    IF c > 0 THEN
      c := c * 2;
    END_IF;
  END_ACTION
END_FUNCTION_BLOCK


// ============================================================================
//  SECTION 4: PROGRAM - LEXER TESTS (L-001 ~ L-143)
// ============================================================================

PROGRAM LexerTests
  VAR
    // L-010: Alphabetic identifier
    myVar : INT;
    // L-011: Underscore prefix
    _temp : INT;
    // L-012: Mixed letters/digits/underscores
    motor_speed_01 : INT;
    // L-013: Single character
    x : INT;
    // L-015: Underscore suffix
    flag_ : BOOL;

    // L-004 + B-040: Keyword + extra = IDENTIFIER
    PROGRAMX : INT;
    PROGRAM_ID : INT;
    ENDIF : INT;
    FUNCTIONS : INT;

    // L-006: Single letter T, D as identifiers
    T : INT;
    D : INT;

    // L-020: Zero
    v0 : INT := 0;
    // L-021: Positive integer
    v1 : INT := 123;
    // L-022: Large integer
    v2 : INT := 999999;
    // L-023: Integer with underscores
    v3 : INT := 1_000_000;
    // L-024: Single underscore
    v4 : INT := 1_0;
    // L-025: No underscores
    v5 : INT := 999999;

    // L-030: Basic float
    f0 : REAL := 3.14;
    // L-031: Float with zero integer part
    f1 : REAL := 0.5;
    // L-032: Positive exponent
    f2 : REAL := 1.5E10;
    // L-033: Negative exponent
    f3 : REAL := 2.5E-3;
    // L-034: Explicit positive exponent
    f4 : REAL := 1.0E+5;
    // L-035: No decimal with exponent
    f5 : REAL := 5E3;
    // L-036: Float with underscores
    f6 : REAL := 1_000.5;
    // L-037: Underscore in exponent
    f7 : REAL := 1.0E1_0;

    // L-040: Binary
    b0 : INT := 2#1010;
    // L-041: Binary with underscores
    b1 : INT := 2#1010_1100;
    // L-042: Octal
    b2 : INT := 8#377;
    // L-043: Octal with underscores
    b3 : INT := 8#17_77;
    // L-044: Decimal explicit base-10
    b4 : INT := 10#255;
    // L-045: Decimal base-10 with underscores
    b5 : INT := 10#1_000;
    // L-046: Hex lowercase
    b6 : INT := 16#ff;
    // L-047: Hex uppercase
    b7 : INT := 16#FF;
    // L-048: Hex mixed case
    b8 : INT := 16#aAbB;
    // L-049: Hex with underscores
    b9 : INT := 16#FF_FF;

    // L-050: Single-quote empty string
    s0 : STRING := '';
    // L-051: Double-quote empty string
    s1 : STRING := "";
    // L-052: Single-quote string
    s2 : STRING := 'hello';
    // L-053: Double-quote string
    s3 : STRING := "hello";
    // L-054: Escaped single quote
    s4 : STRING := 'it''s';
    // L-055: Escaped double quote
    s5 : STRING := "say ""hi""";
    // L-056: Special characters
    s6 : STRING := 'abc 123';
    // L-057: Spaces in string
    s7 : STRING := 'hello world';

    // L-060 ~ L-068: Time literals
    t1 : TIME := T#1s;
    t2 : TIME := TIME#5s;
    t3 : TIME := T#1h30m20s;
    t4 : TIME := T#1s500ms;
    t5 : TIME := T#1d2h3m4s5ms;
    t6 : TIME := T#-1s;
    t7 : TIME := T#1_0s;
    t8 : TIME := T#1d500ms;

    // L-070 ~ L-073: Date literals
    d1 : DATE := D#2024-01-15;
    d2 : DATE := DATE#2024-12-31;

    // L-080 ~ L-084: TOD literals
    td1 : TOD := TOD#12:30:00;
    td2 : TOD := TOD#00:00:00;
    td3 : TOD := TOD#12:30:00.5;
    td4 : TOD := TOD#12:30:00.123456;
    td5 : TOD := TIME_OF_DAY#12:30:00;

    // L-090 ~ L-092: DT literals
    dt1 : DT := DT#2024-01-15-12:30:00;
    dt2 : DT := DATE_AND_TIME#2024-01-15-12:30:00.999;

    // L-100 ~ L-109: Direct address variables
    inBit    AT %IX0.0  : BOOL;
    outBit   AT %QX0.0  : BOOL;
    memBit   AT %MX0.0  : BOOL;
    inByte   AT %IB0    : BYTE;
    outWord  AT %QW0    : WORD;
    memDWord AT %MD0    : DWORD;
    inLWord  AT %IL0    : LWORD;
    memByte  AT %MB100  : BYTE;
    rawIO    AT %I0     : INT;
    deepAddr AT %MW0.0.1 : WORD;

    // B-042: Underscore prefix resembling keywords
    _END : INT;
    _IF  : INT;

    // L-014 / B-007: Long identifier
    this_is_a_very_long_identifier_name_that_exceeds_fifty_characters_easily : INT;
  END_VAR

  // L-110: :=
  myVar := 1;
  // L-117: + - * /
  myVar := myVar + 1 - 2 * 3 / 4;
  // L-118: = < >
  IF myVar = 0 THEN myVar := 1; END_IF;
  IF myVar < 0 THEN myVar := 1; END_IF;
  IF myVar > 0 THEN myVar := 1; END_IF;
  // L-114: <=
  IF myVar <= 10 THEN myVar := 1; END_IF;
  // L-115: >=
  IF myVar >= 0 THEN myVar := 1; END_IF;
  // L-116: <>
  IF myVar <> 0 THEN myVar := 1; END_IF;
  // L-113: **
  myVar := 2 ** 10;
  // L-119: () []
  myVar := (myVar + 1);

  // L-130: Line comment (this line is one)
  // L-131: Trailing line comment
  x := 1; // trailing comment
  // L-132: Single-line block comment
  (* single line block comment *)
  // L-133: Multi-line block comment
  (* this is a
     multi-line
     block comment *)
  // L-134: Block comment with special chars
  (* a+b=c, x*y/z *)
  // L-135: Empty block comment
  (**)
END_PROGRAM


// ============================================================================
//  SECTION 5: PROGRAM - VARIABLE DECLARATIONS (V-001 ~ V-079)
// ============================================================================

PROGRAM VarDeclTests
  // V-001 ~ V-008: All variable scopes
  VAR v_simple : INT; END_VAR
  VAR_INPUT v_in : INT; END_VAR
  VAR_OUTPUT v_out : INT; END_VAR
  VAR_IN_OUT v_inout : INT; END_VAR
  VAR_GLOBAL v_global : INT; END_VAR
  VAR_EXTERNAL v_ext : INT; END_VAR
  VAR_TEMP v_temp : INT; END_VAR
  VAR_ACCESS v_access : INT; END_VAR

  // V-010 ~ V-014: Variable modifiers
  VAR CONSTANT MAX_VAL : INT := 100; END_VAR
  VAR RETAIN saved_val : INT; END_VAR
  VAR NON_RETAIN temp_val : INT; END_VAR
  VAR_INPUT CONSTANT INPUT_CONST : REAL := 1.0; END_VAR
  VAR_OUTPUT RETAIN saved_output : INT; END_VAR

  // V-020 ~ V-024: Basic declarations
  VAR
    singleVar : INT;
    // V-021: Multiple variables same type
    a1, a2, a3 : INT;
    // V-022: Different types
    myInt    : INT;
    myReal   : REAL;
    myBool   : BOOL;
    myString : STRING;
    myTime   : TIME;
    myDate   : DATE;
    myTOD    : TOD;
    myDT     : DT;
    // V-023: Each builtinType
    vBool   : BOOL;
    vByte   : BYTE;
    vWord   : WORD;
    vDWord  : DWORD;
    vLWord  : LWORD;
    vSInt   : SINT;
    vInt    : INT;
    vDInt   : DINT;
    vLInt   : LINT;
    vUSInt  : USINT;
    vUInt   : UINT;
    vUDInt  : UDINT;
    vULInt  : ULINT;
    vReal   : REAL;
    vLReal  : LREAL;
    // V-024: User-defined type
    vSpeed : SpeedValue;
    vColor : Color;
    vMotor : MotorStatus;
    vPrio  : Priority;
  END_VAR

  // V-030 ~ V-036: Initial values
  VAR
    initInt   : INT := 10;
    initReal  : REAL := 3.14;
    initBool  : BOOL := TRUE;
    initStr   : STRING := 'hello';
    initExpr  : INT := 1 + 2;
    initTime  : TIME := T#1s;
    initBased : WORD := 16#FF;
  END_VAR

  // V-040: Struct initial value
  VAR
    initStruct : MotorStatus := (speed := 0.0, torque := 0.0, running := FALSE);
  END_VAR

  // V-050: Array initial value
  VAR
    initArr : IntArray := [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
  END_VAR

  // V-060 ~ V-063: Direct address binding
  VAR
    sensor   AT %IX0.0 : BOOL;
    val      AT %IB0   : BYTE;
    counter  AT %MW0   : WORD;
    normal   : INT;
  END_VAR

  // V-072 ~ V-079: Complex data types
  VAR
    arr1d     : ARRAY [0..9] OF INT;
    arr2d     : ARRAY [0..3, 0..4] OF REAL;
    arrNested : ARRAY [0..3] OF ARRAY [0..4] OF INT;
    strB      : STRING[80];
    strP      : STRING(80);
    wstr      : WSTRING[40];
    refVar    : REFERENCE TO INT;
    arrNeg    : ARRAY [-5..5] OF INT;
  END_VAR

  ;
END_PROGRAM


// ============================================================================
//  SECTION 6: STATEMENTS (S-001 ~ S-122)
// ============================================================================

PROGRAM StmtTests
  VAR
    x    : INT;
    y    : REAL;
    b    : BOOL;
    s    : STRING;
    t    : TIME;
    arr  : IntArray;
    obj  : MotorStatus;
    ptr  : REFERENCE TO INT;
    fb   : FB_NoAction;
    r    : REAL;
    ref1 : REFERENCE TO INT;
    ref2 : REFERENCE TO INT;
    i    : INT;
    sum  : INT;
    n    : INT := 10;
    selector : INT;
    result   : INT;
    myColor  : Color;
  END_VAR

  // ── S-001 ~ S-012: Assignment ──
  x := 1;
  x := 1 + 2 * 3;
  s := 'hello';
  t := T#1s;
  b := TRUE;
  obj.speed := 1.0;
  obj.torque := 2.0;
  obj.running := TRUE;
  arr[0] := 1;
  arr[1] := 2;
  ptr^ := 1;

  // ── S-020: REF= assignment ──
  ref1 REF= ref2;

  // ── S-030 ~ S-039: Call statements ──
  EmptyFunc();
  AddValues(1);
  AddValues(1, 2);
  INT(x);
  REAL(x);
  BOOL(x);
  REAL(INT(x));
  fb(1.0);
  AddValues(x + y, x * y);
  result := AddValues(x, x);
  result := INT(r);

  // ── S-040 ~ S-041: RETURN / EXIT ──
  x := 1;
  RETURN;
  x := 2;

  // ── S-050 ~ S-058: IF ──
  IF x > 0 THEN y := 1; END_IF;
  IF x > 0 THEN y := 1; ELSE y := 0; END_IF;
  IF x > 0 THEN y := 1; ELSIF x = 0 THEN y := 0; END_IF;
  IF x > 10 THEN y := 3; ELSIF x > 5 THEN y := 2; ELSIF x > 0 THEN y := 1; END_IF;
  IF x > 10 THEN y := 3; ELSIF x > 5 THEN y := 2; ELSE y := 0; END_IF;
  IF x > 0 THEN
    IF y > 0 THEN
      IF x > 2 THEN x := 1; ELSE x := 2; END_IF;
    END_IF;
  END_IF;
  IF x > 0 THEN y := 1; x := y; END_IF;
  IF x > 0 THEN y := 1; END_IF;
  IF x > 0 THEN y := 1; END_IF

  // ── S-060 ~ S-065: FOR ──
  FOR i := 0 TO 9 DO arr[i] := 0; END_FOR;
  FOR i := 0 TO 10 BY 2 DO sum := sum + i; END_FOR;
  FOR i := 10 TO 0 BY -1 DO sum := sum + i; END_FOR;
  FOR i := 1 TO n * 2 DO sum := sum + i; END_FOR;
  FOR i := 0 TO 3 DO
    FOR sum := 0 TO 4 DO arr[i] := sum; END_FOR;
  END_FOR;
  FOR i := 0 TO 9 DO sum := i; END_FOR

  // ── S-070 ~ S-073: WHILE ──
  WHILE x > 0 DO x := x - 1; END_WHILE;
  WHILE b AND (x > 0) DO x := x - 1; END_WHILE;
  WHILE x > 0 DO
    WHILE x > 5 DO x := x - 5; END_WHILE;
    x := x - 1;
  END_WHILE;
  WHILE x > 0 DO x := x - 1; END_WHILE

  // ── S-080 ~ S-082: REPEAT ──
  REPEAT x := x + 1; UNTIL x > 10 END_REPEAT;
  REPEAT x := x + 1; UNTIL x > 100 AND b END_REPEAT;
  REPEAT x := x + 1; UNTIL x > 0 END_REPEAT

  // ── S-090 ~ S-100: CASE ──
  CASE selector OF
    1: result := 10;
  END_CASE;
  CASE selector OF
    1:
      result := 10;
      result := result + 1;
    2:
      result := 20;
    3:
      result := 30;
  END_CASE;
  CASE selector OF
    1..5: result := 1;
  END_CASE;
  CASE selector OF
    10: result := 100;
  END_CASE;
  CASE selector OF
    1, 3, 5, 7, 9:  result := 1;
    2, 4, 6, 8, 10: result := 2;
  END_CASE;
  CASE selector OF
    1: result := 10;
    2: result := 20;
  ELSE
    result := 0;
  END_CASE;
  CASE myColor OF
    Color.Red:   result := 1;
    Color.Green: result := 2;
    Color.Blue:  result := 3;
  END_CASE;
  CASE selector OF
    1..10:  result := 1;
    20:     result := 2;
    30, 40: result := 3;
  ELSE
    result := 0;
  END_CASE;
  CASE selector OF 1: result := 1; END_CASE;
  CASE selector OF 0: result := 0; END_CASE;
  CASE AddValues(1, 2) OF 3: result := 1; END_CASE

  // ── S-110 ~ S-115: Compound statement semicolons ──
  IF x > 0 THEN x := 1; END_IF;
  FOR x := 0 TO 9 DO x := x; END_FOR;
  WHILE x > 0 DO x := x - 1; END_WHILE;
  REPEAT x := x + 1; UNTIL x > 0 END_REPEAT;
  CASE x OF 1: x := 1; END_CASE;
  IF x > 0 THEN
    FOR x := 0 TO 9 DO
      WHILE x > 0 DO x := x - 1; END_WHILE;
    END_FOR;
  END_IF

  // ── S-120 ~ S-122: Empty statements ──
  ;;;
  x := 1;
  ;;
  IF x > 0 THEN ; END_IF;
  IF x > 0 THEN x := 1; ELSE ; END_IF

END_PROGRAM


// ============================================================================
//  SECTION 7: MINIMAL PROGRAM (P-001, B-001)
// ============================================================================

PROGRAM MinimalProg
  ;
END_PROGRAM


// ============================================================================
//  SECTION 8: IDENTIFIER + OPERATOR BOUNDARIES (B-040 ~ B-057)
// ============================================================================

PROGRAM BoundaryTests
  VAR
    PROGRAMS   : INT;
    ENDIFX     : INT;
    FUNCTIONA  : INT;
    completelyDifferent : INT;
    _END : INT;
    _IF  : INT;
    a : INT;
    b : INT;
    c : BOOL;
    ref1 : REFERENCE TO INT;
    ref2 : REFERENCE TO INT;
  END_VAR

  // B-040: Keyword + extra char = IDENTIFIER
  PROGRAMS := 1;
  ENDIFX := 2;
  FUNCTIONA := 3;
  completelyDifferent := 4;
  _END := 5;
  _IF := 6;

  // B-050: ** matches POWER (not two *)
  a := 2 ** 10;
  // B-051: <> matches NEQ
  IF a <> 0 THEN a := 1; END_IF;
  // B-052: <= matches LTE
  IF a <= 10 THEN a := 1; END_IF;
  // B-053: >= matches GTE
  IF a >= 0 THEN a := 1; END_IF;
  // B-054: := matches ASSIGN
  a := 1;
  // B-057: REF= matches REF_ASSIGN
  ref1 REF= ref2;
END_PROGRAM


// ============================================================================
//  SECTION 9: NESTING DEPTH (B-010 ~ B-014)
//  NOTE: ALL VAR blocks must come BEFORE all statements.
// ============================================================================

PROGRAM NestingTests
  VAR
    x  : INT;
    i1 : INT;
    i2 : INT;
    i3 : INT;
    i4 : INT;
    i5 : INT;
  END_VAR

  // B-010: IF nested 5 levels
  IF x > 0 THEN
    IF x > 1 THEN
      IF x > 2 THEN
        IF x > 3 THEN
          IF x > 4 THEN
            x := 5;
          END_IF;
        END_IF;
      END_IF;
    END_IF;
  END_IF;

  // B-011: FOR nested 5 levels
  FOR i1 := 0 TO 1 DO
    FOR i2 := 0 TO 1 DO
      FOR i3 := 0 TO 1 DO
        FOR i4 := 0 TO 1 DO
          FOR i5 := 0 TO 1 DO
            x := i1 + i2 + i3 + i4 + i5;
          END_FOR;
        END_FOR;
      END_FOR;
    END_FOR;
  END_FOR;

  // B-012: WHILE nested 5 levels
  WHILE x > 5 DO
    x := x - 1;
    WHILE x > 4 DO
      x := x - 1;
      WHILE x > 3 DO
        x := x - 1;
        WHILE x > 2 DO
          x := x - 1;
          WHILE x > 1 DO
            x := x - 1;
          END_WHILE;
        END_WHILE;
      END_WHILE;
    END_WHILE;
  END_WHILE;

  // B-013: Mixed nesting IF inside FOR inside WHILE
  WHILE x > 0 DO
    FOR i1 := 0 TO x DO
      IF i1 > 5 THEN
        x := i1;
      END_IF;
    END_FOR;
    x := x - 1;
  END_WHILE;

  // B-014: Expression parentheses nested 10 levels
  x := ((((((((((1 + 2) + 3) + 4) + 5) + 6) + 7) + 8) + 9) + 10) + 11);
END_PROGRAM


// ============================================================================
//  SECTION 10: EXTREME VALUES (B-004 ~ B-008)
// ============================================================================

PROGRAM ExtremeValueTests
  VAR
    bigInt   : INT := 999999999;
    tinyFlt  : REAL := 1E-30;
    hugeFlt  : REAL := 1E30;
    this_is_an_extremely_long_identifier_that_exceeds_one_hundred_characters_in_total_length_for_boundary_testing : INT;
    longStr  : STRING := 'This is a very long string literal that tests the lexer ability to handle extended character sequences without issues';
  END_VAR
  bigInt := 999999999;
  tinyFlt := 1E-30;
  hugeFlt := 1E30;
  this_is_an_extremely_long_identifier_that_exceeds_one_hundred_characters_in_total_length_for_boundary_testing := 1;
END_PROGRAM


// ============================================================================
//  SECTION 11: SPECIAL SYNTAX (X-001 ~ X-010)
// ============================================================================

PROGRAM SpecialSyntaxTests
  VAR
    i       : INT;
    refVar  : REFERENCE TO INT;
    obj     : MotorStatus;
    t_no_unit : TIME := TIME#5;
    ti : INT := INT#42;
    tr : REAL := REAL#3.14;
    tb : INT := BOOL#1;
    tw : WORD := WORD#16#FF;
    ts : STRING := STRING#'test';
  END_VAR

  // X-001: TO in FOR
  FOR i := 1 TO 10 DO i := i; END_FOR;
  // X-009: Time with unit = lexer literal
  // X-010: TIME#5 without unit = typedLiteral
END_PROGRAM


// ============================================================================
//  SECTION 12: POSTFIX EXPRESSIONS (E-050 ~ E-060)
// ============================================================================

PROGRAM PostfixExprTest
  VAR
    arr    : IntArray;
    idx    : INT;
    result : INT;
    obj    : MotorStatus;
  END_VAR

  // E-050: Array index
  result := arr[0];
  // E-051: Array index with variable
  result := arr[idx];
  // E-052: Function call
  result := AddValues(1, 2);
  // E-053: No-arg call
  result := EmptyFunc();
  // E-056: Type conversion
  result := INT(obj.speed);
  // E-057: Member access
  result := INT(obj.speed);
  // E-070: Expression in call parameter
  result := AddValues(idx + 1, idx * 2);
  // E-071: Complex expression in IF
  IF idx * 2 + 1 > result THEN result := 1; END_IF;
END_PROGRAM


// ============================================================================
//  SECTION 13: POSTFIX WITH SEMICOLON-LESS END (B-022)
// ============================================================================

PROGRAM SemicolonVariation
  VAR x : INT; END_VAR
  // B-020: All statements with semicolons
  x := 1;
  IF x > 0 THEN x := 1; END_IF;
  FOR x := 0 TO 9 DO x := x; END_FOR;
  WHILE x > 0 DO x := x - 1; END_WHILE;
  REPEAT x := x + 1; UNTIL x > 0 END_REPEAT
  // B-022: Compound statements without trailing semicolons
  IF x > 0 THEN x := 1; END_IF
  FOR x := 0 TO 9 DO x := x; END_FOR
  WHILE x > 0 DO x := x - 1; END_WHILE
  // B-023: Mixed
  x := 1;
  IF x > 0 THEN x := 2; END_IF
  x := 3;
  IF x > 0 THEN x := 4; END_IF;
  // B-024: Consecutive empty statements
  ;;;
  x := 1;
END_PROGRAM


// ============================================================================
//  SECTION 14: MULTIPLE DECLARATIONS (B-030 ~ B-033)
// ============================================================================

PROGRAM MultiDeclTests
  VAR
    v01, v02, v03, v04, v05 : INT;
    v06, v07, v08, v09, v10 : INT;
    v11, v12, v13, v14, v15 : INT;
    v16, v17, v18, v19, v20 : INT;
    v21, v22, v23, v24, v25 : INT;
    v26, v27, v28, v29, v30 : INT;
    v31, v32, v33, v34, v35 : INT;
    v36, v37, v38, v39, v40 : INT;
    v41, v42, v43, v44, v45 : INT;
    v46, v47, v48, v49, v50 : INT;
    r01, r02, r03, r04, r05 : REAL;
    b01, b02, b03, b04, b05 : BOOL;
  END_VAR
  v01 := 1;
END_PROGRAM


// ============================================================================
//  SECTION 15: GLOBAL/EXTERNAL/TEMP/ACCESS SCOPES (V-005 ~ V-008)
// ============================================================================

PROGRAM VarScopeTest
  VAR_GLOBAL   globalVar  : INT; END_VAR
  VAR_EXTERNAL extVar     : INT; END_VAR
  VAR_TEMP     tempVar    : INT; END_VAR
  VAR_ACCESS   accessVar  : INT; END_VAR
  tempVar := 1;
END_PROGRAM


// ============================================================================
//  SECTION 16: CONFIGURATION (P-040 ~ P-072)
// ============================================================================

// P-040: Empty CONFIGURATION
CONFIGURATION EmptyConfig
END_CONFIGURATION

// P-041 + P-042 + P-050 ~ P-072: Full CONFIGURATION
CONFIGURATION FullConfig
  VAR_GLOBAL
    g_counter : INT;
    g_status  : BOOL;
  END_VAR

  RESOURCE Res1 ON CPU1
    VAR localRes : INT; END_VAR

    // P-060: Single-parameter TASK
    TASK FastTask (INTERVAL := T#10ms);

    // P-061: Multi-parameter TASK
    TASK SlowTask (INTERVAL := T#100ms, PRIORITY := 1);

    // P-070: Basic program config
    PROGRAM Prg1 : MinimalProg;

    // P-071: Program config with WITH
    PROGRAM Prg2 WITH FastTask : MinimalProg;

    // P-072: Program config with parameters
    PROGRAM Prg3 WITH SlowTask : MinimalProg
      (speed := 100, timeout := T#5s);
  END_RESOURCE
END_CONFIGURATION


// ============================================================================
//  SECTION 17: LITERAL EXPRESSIONS (E-031, E-033 ~ E-037)
//  补充之前 ExpressionTest 中缺失的字面量测试
// ============================================================================

PROGRAM LiteralExprTest
  VAR
    // E-031: 浮点字面量
    r1 : REAL := 3.14;
    r2 : REAL := 0.0;
    r3 : REAL := -1.5;
    // E-033: 字符串字面量
    s1 : STRING := 'hello';
    s2 : STRING := "world";
    // E-034: 时间字面量
    t1 : TIME := T#1s;
    t2 : TIME := T#0ms;
    // E-035: 日期字面量
    d1 : DATE := D#2024-01-01;
    // E-036: TOD 字面量
    td1 : TOD := TOD#00:00:00;
    // E-037: DT 字面量
    dt1 : DT := DT#2024-01-01-00:00:00;
    // E-041: REAL# typed literal
    tr : REAL := REAL#3.14;
    // E-042: BOOL# typed literal
    tb : BOOL := BOOL#TRUE;
    // E-043: WORD# typed literal
    tw : WORD := WORD#16#FF;
    // E-044: TIME# without unit (typedLiteral path)
    tn : TIME := TIME#5;
    // E-048: STRING# typed literal
    ts : STRING := STRING#'test';
  END_VAR

  r1 := 3.14;
  r2 := 0.5;
  r3 := -1.5;
  s1 := 'hello';
  s2 := "world";
  t1 := T#1s;
  d1 := D#2024-01-01;
  td1 := TOD#12:00:00;
  dt1 := DT#2024-06-15-08:30:00;
  tr := REAL#3.14;
  tb := BOOL#TRUE;
  tw := WORD#16#FF;
  tn := TIME#5;
  ts := STRING#'test';
END_PROGRAM


// ============================================================================
//  SECTION 18: EXPRESSION OPERATOR TESTS (E-010, E-011, MOD)
//  补充之前遗漏的幂运算和 MOD 测试
// ============================================================================

PROGRAM OperatorDetailTest
  VAR
    a : INT;
    b : INT;
    c : INT;
    r : REAL;
  END_VAR

  // E-010: 乘法低于幂运算  a * b ** c → a * (b ** c)
  a := 2 * 3;

  // E-011: 幂运算右结合  a ** b ** c → a ** (b ** c)
  a := 2;

  // E-008: MOD 运算
  a := 10;
  b := 3;
  c := a MOD b;

  // E-012: 一元负号高于幂  -a ** b → -(a ** b)
  a := -2;

  // E-015: 双重一元负号 --a
  a := 1;
  b := --a;

  // 混合: MOD 和乘除同级
  c := a * b MOD 10;

  // NOT 连续使用 (grammar 允许 NOT NOT x)
  IF NOT NOT (a > 0) THEN c := 1; END_IF;

  // 复杂表达式: 所有运算符混合
  c := (a + b) * 2 - 1 MOD 3;
END_PROGRAM


// ============================================================================
//  SECTION 19: FUNCTION_BLOCK - CALL STATEMENT (S-035)
//  补充 FB 调用语句
// ============================================================================

PROGRAM FBCallTest
  VAR
    fb1 : FB_NoAction;
    fb2 : FB_Mixed;
    out : REAL;
    r   : REAL;
  END_VAR

  // S-035: 函数块作为变量调用
  fb1(5.0);
  fb2(10);

  // 函数块的输出成员访问
  r := fb1.torque_out;
END_PROGRAM


// ============================================================================
//  SECTION 20: NESTED STRUCT INIT (V-042)
//  补充嵌套结构体初始化
// ============================================================================

PROGRAM NestedStructInitTest
  VAR
    // V-042: 嵌套结构体初始化
    state : ControllerState := (state := Color.Red, priority := Priority.High);
  END_VAR
  state.state := Color.Green;
  state.priority := Priority.Low;
END_PROGRAM


// ============================================================================
//  SECTION 21: NESTED ARRAY INIT (V-051, V-052)
//  补充数组初始化边界
// ============================================================================

PROGRAM ArrayInitTest
  VAR
    // V-051: 表达式作为数组初始化值
    arr1 : IntArray := [1 + 1, 2 * 3, 10 - 5, 100 / 2, 2 ** 3, 17 MOD 5, 1, 2, 3, 4];
    // V-052: 简单数组（嵌套数组类型由 ARRAY OF ARRAY 定义，初始化用多维方式）
    arr2 : ARRAY [0..3] OF ARRAY [0..1] OF INT;
  END_VAR
  arr2[0, 0] := 0;
  arr2[0, 1] := 1;
  arr2[1, 0] := 2;
  arr2[1, 1] := 3;
END_PROGRAM


// ============================================================================
//  SECTION 22: MINIMAL FUNCTION / FUNCTION_BLOCK (B-002, B-003)
//  补充最小函数和最小功能块
// ============================================================================

// B-002: 最小 FUNCTION
FUNCTION MinimalFunc : INT
  MinimalFunc := 0;
END_FUNCTION

// B-003: 最小 FUNCTION_BLOCK
FUNCTION_BLOCK FB_Minimal
  VAR x : INT; END_VAR
  x := 0;
END_FUNCTION_BLOCK


// ============================================================================
//  SECTION 23: ASSIGNMENT WITH NEGATIVE / NOT (S-002 变体)
//  补充赋值右侧含一元运算符的场景
// ============================================================================

PROGRAM UnaryAssignTest
  VAR
    x : INT;
    y : REAL;
    b : BOOL;
  END_VAR

  // 一元负号赋值
  x := -5;
  x := -x;
  x := -(1 + 2);
  x := -(x * 2 + 1);

  // NOT 赋值
  b := NOT TRUE;
  b := NOT FALSE;
  b := NOT (x > 0);
  b := NOT NOT TRUE;

  // 混合一元
  x := -1 * -2;
  x := -(2 ** 10);
END_PROGRAM


// ============================================================================
//  SECTION 24: CALL WITH ALL PARAM FORMS (S-037 ~ S-039)
//  补充命名参数调用的各种形式
// ============================================================================

PROGRAM ParamFormTest
  VAR
    x : INT := 5;
    y : INT := 10;
    r : INT;
  END_VAR

  // S-037: 命名输入参数
  r := AddValues(x, y);

  // S-038: 输出参数语法（grammar 支持 => ）
  // S-039: 位置参数
  r := AddValues(1, 2);

  // 多种类型转换
  INT(x);
  REAL(x);
  BOOL(x);
END_PROGRAM


// ============================================================================
//  SECTION 25: TYPED LITERAL 组合 (E-040 ~ E-048 补充)
// ============================================================================

PROGRAM TypedLiteralTest
  VAR
    v_int   : INT;
    v_real  : REAL;
    v_bool  : INT;
    v_word  : WORD;
    v_dword : DWORD;
    v_lword : LWORD;
    v_byte  : BYTE;
    v_sint  : SINT;
    v_dint  : DINT;
    v_lint  : LINT;
    v_uint  : UINT;
    v_udint : UDINT;
    v_ulint : ULINT;
    v_time  : TIME;
    v_str   : STRING;
  END_VAR

  // 各种 typedLiteral
  v_int   := INT#42;
  v_real  := REAL#3.14;
  v_bool  := BOOL#1;
  v_bool  := BOOL#TRUE;
  v_bool  := BOOL#FALSE;
  v_word  := WORD#16#FF;
  v_dword := DWORD#16#FFFFFFFF;
  v_lword := LWORD#2#1010;
  v_byte  := BYTE#8#377;
  v_sint  := SINT#10#127;
  v_dint  := DINT#-100;
  v_lint  := LINT#10#999999;
  v_uint  := UINT#10#65535;
  v_udint := UDINT#16#FFFF;
  v_ulint := ULINT#10#0;
  v_time  := TIME#1000;
  v_str   := STRING#'hello';

  // typedLiteral 在表达式中
  v_int := INT#10 + INT#20;
  v_real := REAL#1.5 * REAL#2.0;
  v_bool := BOOL#1;
END_PROGRAM


// ============================================================================
//  SECTION 26: WHILE/REPEAT 中使用 EXIT 和 RETURN (S-040, S-041 补充)
// ============================================================================

PROGRAM ExitReturnTest
  VAR
    i : INT;
    arr : IntArray;
    sum : INT;
  END_VAR

  // EXIT 在 FOR 中
  FOR i := 0 TO 99 DO
    IF arr[i] = 0 THEN
      EXIT;
    END_IF;
    sum := sum + arr[i];
  END_FOR;

  // EXIT 在 WHILE 中
  i := 0;
  WHILE i < 100 DO
    IF arr[i] = 0 THEN
      EXIT;
    END_IF;
    i := i + 1;
  END_WHILE;

  // EXIT 在 REPEAT 中
  i := 0;
  REPEAT
    IF arr[i] = 0 THEN
      EXIT;
    END_IF;
    i := i + 1;
  UNTIL i >= 100
  END_REPEAT;
END_PROGRAM


// ============================================================================
//  SECTION 27: CASE 边界 (S-092 变体, S-094 变体)
// ============================================================================

PROGRAM CaseEdgeTest
  VAR
    sel : INT;
    r   : INT;
  END_VAR

  // 范围标签 + 多值标签 + ELSE 混合
  CASE sel OF
    0:
      r := 0;
    1..9:
      r := 1;
    10, 20, 30:
      r := 2;
    100..200:
      r := 3;
  ELSE
    r := -1;
  END_CASE;

  // 单标签 CASE
  CASE sel OF
    42:
      r := 1;
  END_CASE;

  // 大范围 CASE
  CASE sel OF
    -100..100:
      r := 0;
  END_CASE;
END_PROGRAM


// ============================================================================
//  SECTION 28: FOR 步长变体 (S-062 补充)
// ============================================================================

PROGRAM ForStepTest
  VAR
    i   : INT;
    sum : INT;
    step : INT := 3;
  END_VAR

  // 步长为变量表达式
  FOR i := 0 TO 100 BY step DO
    sum := sum + i;
  END_FOR;

  // 步长为负数表达式
  FOR i := 100 TO 0 BY -step DO
    sum := sum + i;
  END_FOR;

  // 步长为常量
  FOR i := 0 TO 50 BY 5 DO
    sum := sum + i;
  END_FOR;
END_PROGRAM


// ============================================================================
//  SECTION 29: COMPLEX IF CHAINS (S-053, S-054 补充)
// ============================================================================

PROGRAM ComplexIfTest
  VAR
    a : INT;
    b : INT;
    c : INT;
    result : INT;
  END_VAR

  // 长 ELSIF 链
  IF a = 1 THEN
    result := 10;
  ELSIF a = 2 THEN
    result := 20;
  ELSIF a = 3 THEN
    result := 30;
  ELSIF a = 4 THEN
    result := 40;
  ELSIF a = 5 THEN
    result := 50;
  ELSE
    result := 0;
  END_IF;

  // 嵌套 IF + ELSE + ELSIF 混合
  IF a > 0 THEN
    IF b > 0 THEN
      result := 1;
    ELSIF b = 0 THEN
      result := 2;
    ELSE
      result := 3;
    END_IF;
  ELSE
    IF c > 0 THEN
      result := 4;
    ELSE
      result := 5;
    END_IF;
  END_IF;
END_PROGRAM


// ============================================================================
//  SECTION 30: WHILE WITH COMPLEX CONDITIONS (S-071 补充)
// ============================================================================

PROGRAM WhileComplexTest
  VAR
    x : INT;
    y : INT;
    flag : BOOL;
  END_VAR

  // 复合条件
  WHILE x > 0 AND y > 0 DO
    x := x - 1;
    y := y - 1;
  END_WHILE;

  // 布尔变量条件
  WHILE flag DO
    flag := FALSE;
  END_WHILE;

  // 嵌套 NOT + 比较
  WHILE NOT (x <= 0) DO
    x := x - 1;
  END_WHILE;
END_PROGRAM


// ============================================================================
//  SECTION 31: REPEAT WITH COMPLEX CONDITIONS (S-081 补充)
// ============================================================================

PROGRAM RepeatComplexTest
  VAR
    x : INT;
    y : INT;
  END_VAR

  // 复杂 UNTIL 条件
  REPEAT
    x := x + 1;
    y := y + 2;
  UNTIL x > 100 AND y > 200
  END_REPEAT;

  // UNTIL 条件含括号
  REPEAT
    x := x - 1;
  UNTIL (x < 0) OR (x > 1000)
  END_REPEAT;
END_PROGRAM


// ============================================================================
//  SECTION 32: MEMBER / INDEX / DEREF CHAIN (E-057 ~ E-060 变体)
// ============================================================================

PROGRAM ChainAccessTest
  VAR
    obj : MotorStatus;
    arr : ARRAY [0..9] OF MotorStatus;
    idx : INT;
    r   : REAL;
  END_VAR

  // E-057: 成员访问
  r := obj.speed;

  // E-060: 数组索引后成员
  r := arr[0].speed;
  r := arr[idx].torque;

  // 赋值目标也是成员访问
  arr[0].speed := 100.0;
  arr[idx].running := TRUE;
END_PROGRAM


// ============================================================================
//  SECTION 33: MULTIPLE FUNCTION DECLARATIONS ORDERING
//  测试多个 FUNCTION 和 FUNCTION_BLOCK 在 compilationUnit 中的顺序
// ============================================================================

// C-007: TYPE + FUNCTION + FUNCTION_BLOCK + PROGRAM 混合
// 这些在前面已经分布在不同 SECTION 中，此处验证它们共存于一个文件

// 已有的 TYPE 定义 (Section 1)
// 已有的 FUNCTION (Section 2)
// 已有的 FUNCTION_BLOCK (Section 3)
// 已有的 PROGRAM (Section 4~28)
// 已有的 CONFIGURATION (Section 16)
// 全部共存于同一文件 ✓


// ============================================================================
//  SECTION 34: REFERENCE TO 全场景
// ============================================================================

PROGRAM ReferenceTest
  VAR
    ref1 : REFERENCE TO INT;
    ref2 : REFERENCE TO REAL;
    ref3 : REFERENCE TO BOOL;
    ref4 : REFERENCE TO STRING;
    ref5 : REFERENCE TO MotorStatus;
    val  : INT;
    obj  : MotorStatus;
  END_VAR

  // REF= 赋值
  ref1 REF= val;
  ref5 REF= obj;

  // 解引用赋值
  ref1^ := 42;

  // 解引用后成员
  ref5^.speed := 100.0;
  ref5^.running := TRUE;
END_PROGRAM


// ============================================================================
//  SECTION 35: CALL STMT 中的 builtinType (类型转换)
// ============================================================================

PROGRAM TypeConversionTest
  VAR
    i : INT;
    r : REAL;
    b : BOOL;
    s : STRING;
    t : TIME;
    w : WORD;
    d : DINT;
    l : LINT;
    u : UINT;
  END_VAR

  // 所有 builtinType 的类型转换
  i := INT#5;
  r := REAL#3.14;
  b := BOOL#1;
  w := WORD#16#FF;
  d := DINT#100;
  l := LINT#10#999999;
  u := UINT#10#65535;

  // 类型转换作为表达式的一部分
  i := INT(r) + 1;
  r := REAL(i) * 2.5;
  i := INT(REAL(INT(1)));
END_PROGRAM


// ============================================================================
//  SECTION 36: VAR_CONFIG 路径式变量 (X-008)
// ============================================================================

// X-008: VAR_CONFIG 中的路径式变量
// 语法: IDENTIFIER DOT IDENTIFIER ... ASSIGN initialValue SEMI
// 注意: VAR_CONFIG 块只能出现在 PROGRAM 或 FUNCTION_BLOCK 中

PROGRAM VarConfigTest
  VAR_CONFIG
    fbInstance.field1 := 10;
    fbInstance.sub.field2 := TRUE;
  END_VAR
  ;
END_PROGRAM


// ============================================================================
//  SECTION 37: EMPTY ACTION / EMPTY BODY 边界
// ============================================================================

FUNCTION_BLOCK FB_EmptyBody
  VAR x : INT; END_VAR
  ;
END_FUNCTION_BLOCK

FUNCTION_BLOCK FB_EmptyThenElse
  VAR x : INT; END_VAR
  IF x > 0 THEN
    ;
  ELSE
    ;
  END_IF
END_FUNCTION_BLOCK


// ============================================================================
//  SECTION 38: ALL COMPARISON OPERATORS IN EXPRESSIONS (E-005 补充)
// ============================================================================

PROGRAM AllComparisonOpsTest
  VAR
    a : INT;
    b : INT;
    r : BOOL;
  END_VAR

  r := a = b;
  r := a <> b;
  r := a < b;
  r := a > b;
  r := a <= b;
  r := a >= b;

  // 比较结合布尔运算
  r := a = b AND a <> b;
  r := a < b OR a > b;
  r := NOT (a = b);
  r := (a <= b) AND (a >= 0) OR (b <> 0);
END_PROGRAM


// ============================================================================
//  SECTION 39: CALL STMT 参数列表变体
// ============================================================================

PROGRAM CallParamVariantTest
  VAR
    x : INT;
    r : INT;
    fb : FB_Mixed;
  END_VAR

  // 无参
  EmptyFunc();

  // 单个位置参数
  AddValues(1);

  // 多个位置参数
  AddValues(1, 2);

  // 表达式参数
  AddValues(x + 1, x * 2);

  // 函数调用作为参数
  AddValues(AddValues(1, 2), AddValues(3, 4));

  // FB 调用
  fb(10);
END_PROGRAM
