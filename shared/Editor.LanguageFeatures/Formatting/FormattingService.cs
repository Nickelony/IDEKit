using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Formatting;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Formatting;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Formatting;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Formatting;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Formatting;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Formatting;

#endif

/// <summary>Formats an editor's document with the edits that an <see cref="IFormattingProvider"/> answers.</summary>
/// <param name="documentFormatterService">
/// The service that applies the validated batch, or <see langword="null"/> to use a <see cref="DocumentFormatterService"/>.
/// </param>
/// <remarks>
/// <see cref="FormattingPlanner"/> turns the provider's answer into one validated batch, so a batch with an edit outside the document
/// or two conflicting edits is applied in full or not at all. The apply half is the base binding's <see cref="IDocumentFormatterService"/>,
/// which applies the batch as one undo step and keeps the caret, the selection, and the scroll position. The editor's thread calls and resumes
/// the asynchronous method.
/// </remarks>
public sealed class FormattingService(IDocumentFormatterService? documentFormatterService = null)
{
	private readonly IDocumentFormatterService _documentFormatterService = documentFormatterService ?? new DocumentFormatterService();

	/// <summary>Asks the provider to format the editor's document and applies the answer.</summary>
	/// <param name="editor">The editor to update.</param>
	/// <param name="provider">The provider that answers the formatting request.</param>
	/// <param name="documentId">The identity of the document that the provider's edits address.</param>
	/// <param name="options">The tab size and indentation choice of the request.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edits are applied, or <see langword="null"/> to edit the editor's document directly. A supplied target
	/// must hold the same content as the editor's document.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>How the formatting ended.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="editor"/>, <paramref name="provider"/>, or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="InvalidOperationException">
	/// The editor has no document assigned, or a supplied target's content does not match the editor's document.
	/// </exception>
	/// <exception cref="OperationCanceledException">The token was canceled before the provider answered.</exception>
	/// <remarks>
	/// The provider's edits for <paramref name="documentId"/> are the only ones applied, and the document text is compared with the text
	/// of the request after the provider answers, so an edit the user made in the meantime reports <see cref="FormattingOutcome.Stale"/>
	/// instead of being overwritten.
	/// </remarks>
	public async Task<FormattingOutcome> FormatDocumentAsync(
		TextEditor editor,
		IFormattingProvider provider,
		string documentId,
		FormattingOptions options,
		ITextEditTarget? editTarget = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(provider);

		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
		ArgumentNullException.ThrowIfNull(options);

		if (editor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		string requestedText = editor.Text;

		WorkspaceEdit? response = await provider
			.GetFormattingEditsAsync(new FormattingRequest(documentId, requestedText, options), cancellationToken)
			.ConfigureAwait(true);

		if (editor.Document is null || !string.Equals(editor.Text, requestedText, StringComparison.Ordinal))
			return FormattingOutcome.Stale;

		if (!FormattingPlanner.TryPlan(documentText: requestedText, documentId, response, out PreparedTextEdits edits, out FormattingOutcome failure))
			return failure;

		_documentFormatterService.ApplyEdits(editor, edits, editTarget);

		return FormattingOutcome.Applied;
	}
}
