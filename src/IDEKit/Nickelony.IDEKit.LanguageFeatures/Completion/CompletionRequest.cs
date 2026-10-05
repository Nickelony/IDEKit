using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Completion;

/// <summary>Describes a completion request against an immutable document snapshot.</summary>
/// <remarks>
/// The shared completion contract addresses the caret by zero-based offset and leaves line and character conversion at the provider boundary.
/// A provider that needs additional contextual state resolves it from <see cref="DocumentText"/> and <see cref="CaretOffset"/> itself, so the request
/// carries no language-specific fields.
/// </remarks>
public sealed record CompletionRequest
{
	/// <param name="documentId">
	/// The identifier of the document the text belongs to. A language-server provider maps it to the document's file URI, and a local provider
	/// may ignore it.
	/// </param>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="caretOffset">The zero-based UTF-16 caret offset within <paramref name="documentText"/>.</param>
	/// <param name="context">
	/// The reason the completion request was triggered, or <see langword="null"/> when the caller
	/// does not specify one; <see cref="CompletionContext.Invoked"/> is used in that case.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="caretOffset"/> is negative or greater than the length of <paramref name="documentText"/>.
	/// </exception>
	public CompletionRequest(string documentId, string documentText, int caretOffset, CompletionContext? context = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
		ArgumentNullException.ThrowIfNull(documentText);
		TextOffsetValidation.ValidateOffset(documentText, offset: caretOffset, parameterName: nameof(caretOffset), offsetDescription: "caret");

		DocumentId = documentId;
		DocumentText = documentText;
		CaretOffset = caretOffset;
		Context = context ?? CompletionContext.Invoked;
	}

	/// <summary>The identifier of the document the text belongs to.</summary>
	public string DocumentId { get; }

	/// <summary>The current document snapshot text.</summary>
	public string DocumentText { get; }

	/// <summary>The zero-based UTF-16 caret offset within <see cref="DocumentText"/>.</summary>
	public int CaretOffset { get; }

	/// <summary>
	/// The reason the completion request was triggered; <see cref="CompletionContext.Invoked"/> when the caller did not specify one.
	/// </summary>
	public CompletionContext Context { get; }
}
