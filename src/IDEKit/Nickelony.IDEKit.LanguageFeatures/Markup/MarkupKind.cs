namespace Nickelony.IDEKit.LanguageFeatures.Markup;

/// <summary>Describes how a host should interpret a text value carried by a language-feature payload.</summary>
/// <remarks>
/// <see cref="PlainText"/> is displayed as-is and <see cref="Markdown"/> may be rendered as Markdown. The members mirror the LSP <c>MarkupKind</c>
/// serialized values (<c>plaintext</c> and <c>markdown</c>), and the language-server layer owns that mapping. The vocabulary is closed:
/// an unsupported protocol format maps to <see cref="PlainText"/> at the boundary and the distinction is lost.
/// </remarks>
public enum MarkupKind
{
	PlainText,
	Markdown,
}
