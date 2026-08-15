using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Signatures;

/// <summary>
/// Describes a signature help request against an immutable document snapshot.
/// </summary>
/// <remarks>
/// <para>
/// Signature help requests use a zero-based document offset so they can be constructed directly
/// from editor caret positions without converting to line and character coordinates.
/// </para>
/// <para>
/// <see cref="Context"/> describes how the request was triggered and carries the previously shown
/// payload for retriggers, so providers can keep the selected overload stable without tracking
/// their own session state. The context is optional because hosts that resolve signature help
/// without editor state have nothing to describe.
/// </para>
/// </remarks>
public sealed record TextSignatureHelpRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextSignatureHelpRequest"/> record.
	/// </summary>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="caretOffset">The zero-based UTF-16 caret offset within that snapshot.</param>
	/// <param name="context">The trigger context, or <see langword="null"/> when unavailable.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="caretOffset"/> is negative or greater than the length of <paramref name="documentText"/>.
	/// </exception>
	public TextSignatureHelpRequest(string documentText, int caretOffset, TextSignatureHelpContext? context = null)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		TextOffsetValidation.ValidateOffset(documentText, caretOffset, nameof(caretOffset), "caret");

		DocumentText = documentText;
		CaretOffset = caretOffset;
		Context = context;
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
	/// Gets the trigger context, or <see langword="null"/> when unavailable.
	/// </summary>
	public TextSignatureHelpContext? Context { get; }
}
