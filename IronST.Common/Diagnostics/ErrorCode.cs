// src/IronST.Common/Diagnostics/ErrorCode.cs
using System.ComponentModel;
using System.Reflection;

namespace IronST.Common.Diagnostics;

public enum ErrorCode
{
    // ═══ E1xxx: 语法/解析错误 ═══

    [Description("未知语法错误")]
    E1000 = 1000,

    [Description("不匹配的输入")]
    E1001 = 1001,

    [Description("无法匹配的语法结构")]
    E1002 = 1002,

    [Description("多余的输入")]
    E1003 = 1003,

    [Description("无法识别的字符")]
    E1004 = 1004,

    [Description("错误恢复时跳过了标记")]
    E1005 = 1005,

    [Description("缺少关键字")]
    E1006 = 1006,

    [Description("缺少分号")]
    E1007 = 1007,

    [Description("缺少标识符")]
    E1008 = 1008,

    [Description("缺少表达式")]
    E1009 = 1009,

    [Description("未关闭的块，缺少结束关键字")]
    E1010 = 1010,

    // ═══ E2xxx: 语义错误 ═══

    [Description("未知语义错误")]
    E2000 = 2000,

    [Description("未声明的标识符")]
    E2001 = 2001,

    [Description("重复声明")]
    E2002 = 2002,

    [Description("类型不兼容")]
    E2003 = 2003,

    [Description("参数数量不匹配")]
    E2004 = 2004,

    [Description("参数类型不匹配")]
    E2005 = 2005,

    [Description("未知的函数或程序块")]
    E2006 = 2006,

    [Description("未知的结构体类型")]
    E2007 = 2007,

    [Description("结构体字段不存在")]
    E2008 = 2008,

    [Description("赋值目标不可写")]
    E2009 = 2009,

    [Description("编译期除零")]
    E2010 = 2010,

    [Description("函数返回值未赋值")]
    E2011 = 2011,

    [Description("保留关键字不能用作标识符")]
    E2012 = 2012,

    [Description("数组下标越界（编译期可检测）")]
    E2013 = 2013,

    [Description("类型转换溢出")]
    E2014 = 2014,

    [Description("未知的类型")]
    E2015 = 2015,

    [Description("非法的递归值布局")]
    E2016 = 2016,

    [Description("全局变量初始值必须是编译期常量")]
    E2017 = 2017,

    // ═══ E3xxx: 代码生成错误 ═══

    [Description("未知代码生成错误")]
    E3000 = 3000,

    [Description("LLVM IR 生成失败")]
    E3001 = 3001,

    [Description("字节码生成失败")]
    E3002 = 3002,

    [Description("不支持的语言特性")]
    E3003 = 3003,

    [Description("类型转换失败")]
    E3004 = 3004,

    [Description("内部编译器错误")]
    E3005 = 3005,

    // ═══ E4xxx: 运行时错误 ═══

    [Description("未知运行时错误")]
    E4000 = 4000,

    [Description("数组越界")]
    E4001 = 4001,

    [Description("空引用")]
    E4002 = 4002,

    [Description("宿主函数未注册")]
    E4003 = 4003,

    [Description("栈溢出")]
    E4004 = 4004,
}

public static class ErrorCodeExtensions
{
    public static string GetDescription(this ErrorCode code)
    {
        var field = code.GetType().GetField(code.ToString());
        var attr = field?.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? code.ToString();
    }
}
