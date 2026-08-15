namespace Nickelony.IDEKit.Core.Highlighting;

/// <summary>
/// Defines the color and font styles for text matched by a <see cref="RegexHighlightingRule"/>
/// or highlighted by a <see cref="RegexHighlightingSpan"/>.
/// </summary>
/// <remarks>
/// <para>
/// The color is carried as a specification string and is parsed by the editor binding when it converts
/// the style to its engine's color type. A missing or blank color leaves the foreground unset so the
/// editor's theme color is used, and an invalid value leaves it unset too unless the binding supplies a
/// fallback color.
/// </para>
/// <para>
/// Bold and italic are applied only when requested, so other styles can still be inherited.
/// </para>
/// </remarks>
/// <param name="ColorValue">
/// A color specification string in a format the editor binding understands, for example
/// <c>#AARRGGBB</c> or a named color.
/// <see langword="null"/> or whitespace-only values leave the foreground unset,
/// while values that cannot be parsed use the binding's fallback color when one is supplied.
/// </param>
/// <param name="IsBold">Whether matched text uses a bold font weight.</param>
/// <param name="IsItalic">Whether matched text uses an italic font style.</param>
public sealed record RegexHighlightingStyle(string? ColorValue = null, bool IsBold = false, bool IsItalic = false);
