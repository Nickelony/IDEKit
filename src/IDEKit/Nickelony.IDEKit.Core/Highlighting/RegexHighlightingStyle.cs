using Nickelony.IDEKit.Core.Media;

namespace Nickelony.IDEKit.Core.Highlighting;

/// <summary>
/// Defines the color and font styles for text matched by a <see cref="RegexHighlightingRule"/> or highlighted
/// by a <see cref="RegexHighlightingSpan"/>.
/// </summary>
/// <param name="Foreground">
/// The foreground color, or <see langword="null"/> to leave the foreground unset so the editor's theme color is used. A host that holds a hexadecimal
/// string parses it with <see cref="ArgbColor.TryParse"/>.
/// </param>
/// <param name="IsBold">Whether matched text uses a bold font weight.</param>
/// <param name="IsItalic">Whether matched text uses an italic font style.</param>
/// <remarks>
/// Bold and italic apply only when requested, so an unset font style keeps the inherited one.
/// </remarks>
public sealed record RegexHighlightingStyle(ArgbColor? Foreground = null, bool IsBold = false, bool IsItalic = false);
