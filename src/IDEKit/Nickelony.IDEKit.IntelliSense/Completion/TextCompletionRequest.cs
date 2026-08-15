using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Describes a completion request against an immutable document snapshot.
/// </summary>
/// <remarks>
/// The shared completion contract addresses the caret by zero-based offset and leaves
/// line and character conversion at the provider boundary. A provider that needs additional
/// contextual state resolves it from <see cref="DocumentText"/> and <see cref="CaretOffset"/> itself,
/// so the request carries no language-specific fields.
/// </remarks>
public sealed record TextCompletionRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionRequest"/> record.
	/// </summary>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="caretOffset">The zero-based UTF-16 caret offset within <paramref name="documentText"/>.</param>
	/// <param name="trigger">
	/// The reason the completion request was triggered, or <see langword="null"/> when the caller
	/// does not specify one; <see cref="TextCompletionTrigger.Invoked"/> is used in that case.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="caretOffset"/> is negative or greater than the length of <paramref name="documentText"/>.
	/// </exception>
	public TextCompletionRequest(
		string documentText,
		int caretOffset,
		TextCompletionTrigger? trigger = null)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		TextOffsetValidation.ValidateOffset(documentText, caretOffset, nameof(caretOffset), "caret");

		DocumentText = documentText;
		CaretOffset = caretOffset;
		Trigger = trigger ?? TextCompletionTrigger.Invoked;
	}

	/// <summary>
	/// Gets the current document snapshot text.
	/// </summary>
	public string DocumentText { get; }

	/// <summary>
	/// Gets the zero-based UTF-16 caret offset within <see cref="DocumentText"/>.
	/// </summary>
	public int CaretOffset { get; }

	/// <summary>
	/// Gets the reason the completion request was triggered; <see cref="TextCompletionTrigger.Invoked"/> when
	/// the caller did not specify one.
	/// </summary>
	public TextCompletionTrigger Trigger { get; }
}
