namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Describes how the commit text of a completion item is interpreted.
/// </summary>
/// <remarks>
/// <para>
/// The values mirror the LSP <c>InsertTextFormat</c> numbers, so a language provider that receives
/// a protocol format maps it one-to-one. A provider that cannot act on an unknown protocol value
/// treats it like <see cref="PlainText"/>.
/// </para>
/// <para>
/// When an item's format is <see cref="Snippet"/>, its commit text (<see cref="TextCompletionItem.InsertText"/>
/// or <see cref="TextCompletionTextEdit.NewText"/> when present) carries LSP snippet syntax with tabstops,
/// placeholders, and escapes. Hosts expand that text at commit time with
/// <see cref="TextSnippetExpander"/>; the library guarantees only the plain-text delivery of the
/// snippet, while tab navigation and undo grouping stay host behavior.
/// </para>
/// </remarks>
public enum TextCompletionInsertTextFormat
{
	/// <summary>
	/// The commit text is plain text that is inserted as supplied.
	/// </summary>
	PlainText = 1,

	/// <summary>
	/// The commit text is a snippet with placeholders, tabstops, and escapes.
	/// </summary>
	Snippet = 2
}
