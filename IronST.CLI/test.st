TYPE Point :
STRUCT
    X : REAL1;
    Y : REAL;
END_STRUCT
;
END_TYPE;

TYPE Color : (Red, Green, Blue);
END_TYPE;

FUNCTION DistanceSq : REAL
VAR_INPUT
    a : Point;
    b : Point;
END_VAR
VAR
    dx : REAL;
    dy : REAL;
END_VAR
dx := a.X - b.X;
dy := a.Y - b.Y;
DistanceSq := dx * dx + dy * dy;
END_FUNCTION

FUNCTION_BLOCK PID
VAR_INPUT
    setpoint : REAL;
    measured : REAL;
END_VAR
VAR_OUTPUT
    controlOutput : REAL;
END_VAR
VAR_IN_OUT
    integralState : REAL;
END_VAR
VAR
    kp : REAL;
    ki : REAL;
END_VAR
controlOutput := kp * (setpoint - measured);
END_FUNCTION_BLOCK

PROGRAM Main
VAR
    p1 : Point;
    p2 : Point;
    dist : REAL;
    pid : PID;
    sp : REAL;
    pv : REAL;
    output : REAL;
    integral : REAL;
END_VAR
dist := DistanceSq(p1, p2);
pid(setpoint := sp, measured := pv, controlOutput => output, integralState := integral);
END_PROGRAM