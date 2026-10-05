using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Formatting;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Formatting;

/// <summary>Turns a formatting provider's answer into the validated edit batch that an editor applies.</summary>
/// <remarks>
/// The provider's line and character edits for one document become offset edits against the text of the request,
/// and <see cref="TextEditKernel"/> validates them as one batch, so a batch with an edit outside the document or two conflicting edits
/// is rejected in full. The editor binding applies the batch.
/// </remarks>
public static class FormattingPlanner
{
	/// <summary>Plans the edits that the answer prescribes for one document.</summary>
	/// <param name="documentText">The document text that the request carried.</param>
	/// <param name="documentId">The identity of the document; only the edits of this document are planned.</param>
	/// <param name="response">The provider's answer, or <see langword="null"/> when it declined.</param>
	/// <param name="edits">Receives the validated batch, or an empty batch when the method returns <see langword="false"/>.</param>
	/// <param name="failure">
	/// Receives <see cref="FormattingOutcome.NothingToApply"/> when the answer holds no effective edit
	/// for the document, or <see cref="FormattingOutcome.Rejected"/> when its edits cannot be applied together; it is only meaningful when the method
	/// returns <see langword="false"/>.
	/// </param>
	/// <returns><see langword="true"/> when the batch holds at least one effective edit.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentText"/> or <paramref name="documentId"/> is <see langword="null"/>.
	/// </exception>
	public static bool TryPlan(
		string documentText,
		string documentId,
		WorkspaceEdit? response,
		out PreparedTextEdits edits,
		out FormattingOutcome failure
	)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(documentId);

		edits = TextEditKernel.Prepare([]).Edits;
		failure = FormattingOutcome.NothingToApply;

		TextDocumentEdit? documentEdit = response?.DocumentEdits.FirstOrDefault(edit =>
			string.Equals(edit.DocumentId, documentId, StringComparison.Ordinal)
		);

		if (documentEdit is not { TextEdits.Count: > 0 })
			return false;

		if (TextEditConversion.Convert(documentText, documentEdit.TextEdits, out PreparedTextEdits prepared) != TextEditConversionOutcome.Converted)
		{
			failure = FormattingOutcome.Rejected;
			return false;
		}

		if (prepared.Operations.Count == 0)
			return false;

		edits = prepared;
		return true;
	}
}
