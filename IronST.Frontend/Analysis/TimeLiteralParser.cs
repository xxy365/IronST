namespace IronST.Frontend.Analysis;

using IronST.Frontend.AST;

/// <summary>
/// 时间字面量解析器 -- 将 ST 时间字面量文本解析为数值。
///
/// 存储方案：
///   TIME           -> 总毫秒数（持续时间，与 epoch 无关）
///   DATE           -> 自 1970-01-01 起的天数
///   TIME_OF_DAY    -> 自 00:00:00 起的毫秒数
///   DATE_AND_TIME  -> 自 1970-01-01 起的总毫秒数
/// </summary>
internal static class TimeLiteralParser
{
    /// <summary>
    /// 解析时间字面量，返回 ParsedValue（long）。
    /// 如果解析失败返回 null（不中断分析）。
    /// 仅捕获 FormatException 和 OverflowException，
    /// 不吞没 OutOfMemoryException 等致命异常。
    /// </summary>
    public static long? TryParse(LiteralKind kind, string raw)
    {
        try {
            var text = raw.Trim();
            var hashIdx = text.IndexOf('#');
            if (hashIdx >= 0)
                text = text[(hashIdx + 1)..].Trim();

            if (string.IsNullOrEmpty(text))
                return null;

            // 空白文本或无数字内容视为无效格式
            if (!text.Any(char.IsDigit))
                return null;

            return kind switch
            {
                LiteralKind.Time => ParseDurationToMs(text),
                LiteralKind.Date => ParseDateToDays(text),
                LiteralKind.TimeOfDay => ParseTodToMs(text),
                LiteralKind.DateTime => ParseDtToMs(text),
                _ => null,
            };
        } catch (Exception ex) when (ex is FormatException or OverflowException) {
            // 格式错误或数值溢出不中断分析，返回 null
            return null;
        }
    }

    /// <summary>
    /// TIME: T#1d2h30m5s3ms -> 总毫秒数
    /// </summary>
    public static long ParseDurationToMs(string text)
    {
        long totalMs = 0;
        int i = 0;

        while (i < text.Length) {
            // 跳过非数字字符（如空格、连接符）
            while (i < text.Length && !char.IsDigit(text[i]) && text[i] != '.' && text[i] != '-')
                i++;

            if (i >= text.Length)
                break;

            // 读取数字部分（支持小数）
            int start = i;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
                i++;

            var numStr = text[start..i];
            if (string.IsNullOrEmpty(numStr))
                break;

            if (
                !double.TryParse(
                    numStr,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var numVal
                )
            )
                break;

            // 读取单位部分
            int unitStart = i;
            while (i < text.Length && char.IsLetter(text[i]))
                i++;

            var unit = text[unitStart..i].ToLowerInvariant();
            var ms = unit switch
            {
                "d" => numVal * 86_400_000,
                "h" => numVal * 3_600_000,
                "m" or "min" => numVal * 60_000,
                "s" => numVal * 1_000,
                "ms" => numVal,
                "us" or "µs" => 0.0, // 毫秒精度下截断
                "ns" => 0.0, // 毫秒精度下截断
                _ => numVal * 1_000, // 无单位默认秒
            };

            // 溢出保护：累加前检查是否超出 long 范围
            if (ms > 0 && totalMs > long.MaxValue - (long)ms)
                return long.MaxValue;
            if (ms < 0 && totalMs < long.MinValue - (long)ms)
                return long.MinValue;

            totalMs += (long)ms;
        }

        return totalMs;
    }

    /// <summary>
    /// DATE: D#2024-01-01 -> 自 1970-01-01 起的天数
    /// </summary>
    public static long ParseDateToDays(string text)
    {
        var parts = text.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return 0;

        if (!int.TryParse(parts[0], out var year))
            return 0;
        if (!int.TryParse(parts[1], out var month))
            return 0;
        if (!int.TryParse(parts[2], out var day))
            return 0;

        return DaysSinceEpoch(year, month, day);
    }

    /// <summary>
    /// TIME_OF_DAY: TOD#12:30:00.5 -> 自 00:00:00 起的毫秒数
    /// </summary>
    public static long ParseTodToMs(string text)
    {
        var parts = text.Split(':');
        if (parts.Length < 2)
            return 0;

        if (!int.TryParse(parts[0], out var hour))
            return 0;
        if (!int.TryParse(parts[1], out var minute))
            return 0;

        int second = 0;
        int millis = 0;

        if (parts.Length >= 3) {
            var secPart = parts[2];
            var dotIdx = secPart.IndexOf('.');
            if (dotIdx >= 0) {
                second = int.Parse(secPart[..dotIdx]);
                var fracStr = secPart[(dotIdx + 1)..];
                fracStr = fracStr.Length switch
                {
                    1 => fracStr + "00",
                    2 => fracStr + "0",
                    >= 3 => fracStr[..3],
                    _ => fracStr,
                };
                if (int.TryParse(fracStr, out var frac))
                    millis = frac;
            } else {
                if (int.TryParse(secPart, out var s))
                    second = s;
            }
        }

        return (long)hour * 3_600_000 + (long)minute * 60_000 + (long)second * 1_000 + millis;
    }

    /// <summary>
    /// DATE_AND_TIME: DT#2024-01-01-12:30:00 -> 自 1970-01-01 起的总毫秒数
    /// 支持两种分隔：DT#2024-01-01-12:30:00 和 DT#2024-01-01 12:30:00
    /// </summary>
    public static long ParseDtToMs(string text)
    {
        // 先尝试空格分隔
        var spaceIdx = text.IndexOf(' ');
        if (spaceIdx >= 0) {
            var datePart = text[..spaceIdx];
            var timePart = text[(spaceIdx + 1)..];
            return CombineDateAndTime(datePart, timePart);
        }

        // 尝试按 '-' 分 4 段：2024-01-01-12:30:00
        var parts = text.Split('-', 4);
        if (parts.Length >= 4) {
            var datePart = $"{parts[0]}-{parts[1]}-{parts[2]}";
            return CombineDateAndTime(datePart, parts[3]);
        }

        return 0;
    }

    private static long CombineDateAndTime(string datePart, string timePart)
    {
        var dateParts = datePart.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (dateParts.Length != 3)
            return 0;

        if (!int.TryParse(dateParts[0], out var year))
            return 0;
        if (!int.TryParse(dateParts[1], out var month))
            return 0;
        if (!int.TryParse(dateParts[2], out var day))
            return 0;

        var days = DaysSinceEpoch(year, month, day);
        var dayMs = ParseTodToMs(timePart);

        return days * 86_400_000 + dayMs;
    }

    /// <summary>计算自 1970-01-01 起的天数</summary>
    public static long DaysSinceEpoch(int year, int month, int day)
    {
        var date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (long)(date - epoch).TotalDays;
    }
}