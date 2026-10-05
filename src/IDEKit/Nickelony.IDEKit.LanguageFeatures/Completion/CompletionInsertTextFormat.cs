namespace Nickelony.IDEKit.LanguageFeatures.Completion;

/// <summary>Describes how the commit text of a completion item is interpreted.</summary>
/// <remarks>
/// The values mirror the LSP <c>InsertTextFormat</c> numbers,
/// so a provider maps a protocol format one-to-one and treats an unknown value as <see cref="PlainText"/>. A <see cref="Snippet"/> commit text
/// (<see cref="CompletionItem.InsertText"/> or <see cref="CompletionTextEdit.NewText"/>) carries snippet syntax that a host expands before insertion;
/// tab navigation and undo grouping stay host behavior.
/// </remarks>
public enum CompletionInsertTextFormat
{
	/// <summary>The commit text is plain text that is inserted as supplied.</summary>
	PlainText = 1,

	/// <summary>The commit text is a snippet with placeholders, tabstops, and escapes.</summary>
	Snippet = 2,
}
