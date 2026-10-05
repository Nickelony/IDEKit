using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.LanguageFeatures.Editing;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Formatting;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Formatting;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Editing;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Editing;

#endif

/// <summary>
/// Applies the edits that a <see cref="WorkspaceEdit"/>, such as the one of a code action or a rename, makes to the document of an editor.
/// </summary>
public static class TextEditorWorkspaceEditExtensions
{
	private static readonly DocumentFormatterService s_applier = new();

	/// <summary>Applies the edits that <paramref name="edit"/> makes to the document <paramref name="documentId"/> names, as one undo step.</summary>
	/// <param name="textEditor">The editor that shows the document.</param>
	/// <param name="edit">The edit whose document edits are applied.</param>
	/// <param name="documentId">The identifier of the editor's document; the document edits that name it ordinally are applied.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edits are applied, or <see langword="null"/> to edit the editor's document directly. A supplied target
	/// must hold the same content as the editor's document.
	/// </param>
	/// <returns>
	/// <see cref="TextEditConversionOutcome.Converted"/> when the edits were applied, which includes an edit
	/// with none for the document; otherwise, why nothing changed.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/>, <paramref name="edit"/>, or <paramref name="documentId"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="InvalidOperationException">
	/// The editor has no document assigned, or a supplied target's content does not match the editor's document.
	/// </exception>
	/// <remarks>
	/// The edits address the editor's current text, so a host rejects an edit that was computed for older
	/// text before it applies it. One edit that ends before it starts, or two that overlap, change nothing. The caret, the selection and the scroll
	/// position stay with the text around them, as they do when the document is formatted.
	/// </remarks>
	public static TextEditConversionOutcome ApplyDocumentEdits(
		this TextEditor textEditor,
		WorkspaceEdit edit,
		string documentId,
		ITextEditTarget? editTarget = null
	)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(edit);

		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		if (textEditor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		List<TextEdit> edits =
		[
			.. edit
				.DocumentEdits.Where(documentEdit => string.Equals(documentEdit.DocumentId, documentId, StringComparison.Ordinal))
				.SelectMany(documentEdit => documentEdit.TextEdits),
		];

		TextEditConversionOutcome outcome = TextEditConversion.Convert(textEditor.Text, edits, out PreparedTextEdits prepared);

		if (outcome == TextEditConversionOutcome.Converted && prepared.Operations.Count > 0)
			s_applier.ApplyEdits(textEditor, prepared, editTarget);

		return outcome;
	}
}
