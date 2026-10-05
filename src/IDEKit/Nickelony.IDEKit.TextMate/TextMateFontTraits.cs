namespace Nickelony.IDEKit.TextMate;

/// <summary>The optional font traits of a parsed theme rule.</summary>
/// <param name="Bold">Whether the rule enables bold text, or <see langword="null"/> when it sets none.</param>
/// <param name="Italic">Whether the rule enables italic text, or <see langword="null"/> when it sets none.</param>
/// <param name="Underline">Whether the rule enables underlining, or <see langword="null"/> when it sets none.</param>
/// <param name="Strikethrough">Whether the rule enables strikethrough, or <see langword="null"/> when it sets none.</param>
/// <remarks>
/// A <see langword="null"/> trait means the rule did not set it, so the resolved style keeps the value it inherited from a shallower
/// scope push or from the theme defaults. A present <c>fontStyle</c> value sets every trait together, so a parsed value is either entirely
/// <see langword="null"/> or fully populated.
/// </remarks>
internal readonly record struct TextMateFontTraits(bool? Bold, bool? Italic, bool? Underline, bool? Strikethrough);
