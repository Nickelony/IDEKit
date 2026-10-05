using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

/// <summary>Describes a signature help request against an immutable document snapshot.</summary>
/// <remarks>
/// The request uses a zero-based document offset, so it can be built from an editor caret without converting to line and character coordinates.
/// <see cref="Context"/> carries the trigger and, on a retrigger, the payload shown by the previous request, so a provider can keep the selected
/// overload stable without session state.
/// </remarks>
public sealed record SignatureHelpRequest
{
	/// <param name="documentId">
	/// The identifier of the document the text belongs to. A language-server provider maps it to the document's file URI, and a local provider
	/// may ignore it.
	/// </param>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="caretOffset">The zero-based UTF-16 caret offset within that snapshot.</param>
	/// <param name="context">The trigger context, or <see langword="null"/> when unavailable.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="caretOffset"/> is negative or greater than the length of <paramref name="documentText"/>.
	/// </exception>
	public SignatureHelpRequest(string documentId, string documentText, int caretOffset, SignatureHelpContext? context = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
		ArgumentNullException.ThrowIfNull(documentText);
		TextOffsetValidation.ValidateOffset(documentText, offset: caretOffset, parameterName: nameof(caretOffset), offsetDescription: "caret");

		DocumentId = documentId;
		DocumentText = documentText;
		CaretOffset = caretOffset;
		Context = context;
	}

	/// <summary>The identifier of the document the text belongs to.</summary>
	public string DocumentId { get; }

	/// <summary>The current document snapshot text.</summary>
	public string DocumentText { get; }

	/// <summary>The zero-based UTF-16 caret offset within <see cref="DocumentText"/>.</summary>
	public int CaretOffset { get; }

	/// <summary>The trigger context, or <see langword="null"/> when unavailable.</summary>
	public SignatureHelpContext? Context { get; }
}
