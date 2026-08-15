namespace Nickelony.IDEKit.IntelliSense;

/// <summary>
/// Describes how a host should interpret a text value carried by an IntelliSense payload.
/// </summary>
/// <remarks>
/// The members describe how a host should interpret a text value: <see cref="PlainText"/> is
/// displayed as-is, and <see cref="Markdown"/> may be rendered as Markdown. The members correspond
/// to the LSP <c>MarkupKind</c> serialized values (<c>plaintext</c> and <c>markdown</c>); the
/// language-server layer (for example the Lua projection) owns that mapping. The type is shared
/// by the payloads that distinguish plain text from Markdown (hover content and completion
/// documentation); payloads that carry plain text only, such as signature documentation, do not use
/// it. The vocabulary is closed: an unsupported protocol format is mapped to
/// <see cref="PlainText"/> at the protocol boundary, and the distinction is lost.
/// </remarks>
public enum TextMarkupKind
{
	/// <summary>
	/// The text is plain text.
	/// </summary>
	PlainText,

	/// <summary>
	/// The text is Markdown.
	/// </summary>
	Markdown
}
