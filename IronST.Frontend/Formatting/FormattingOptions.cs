namespace IronST.Frontend.Formatting;

/// <summary>
/// Options controlling ST source formatting.
/// </summary>
public sealed class FormattingOptions
{
    /// <summary>Number of spaces per indentation level when <see cref="UseTabs"/> is false.</summary>
    public int IndentSize { get; init; } = 4;

    /// <summary>Use one tab per indentation level instead of spaces.</summary>
    public bool UseTabs { get; init; }

    /// <summary>
    /// Line ending to emit. When null (the default), the first line ending found in the
    /// source is reused so LF and CRLF files keep their existing convention.
    /// </summary>
    public string? NewLine { get; init; }

    /// <summary>Keep blank lines between constructs, up to <see cref="MaxBlankLines"/>.</summary>
    public bool PreserveBlankLines { get; init; } = true;

    /// <summary>Maximum number of consecutive blank lines to preserve. Defaults to one.</summary>
    public int MaxBlankLines { get; init; } = 1;

    /// <summary>Ensure the formatted document ends with exactly one line ending.</summary>
    public bool InsertFinalNewLine { get; init; } = true;

    public static FormattingOptions Default { get; } = new();

    internal string IndentUnit =>
        UseTabs ? "\t" : new string(' ', Math.Max(0, IndentSize));
}
