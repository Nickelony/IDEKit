using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.CodeActions;

/// <summary>
/// Describes the editor state a code-action request is built from.
/// </summary>
/// <remarks>
/// <para>
/// A host builds one context for a request that settled after the debounce delay, from the editor's
/// document, caret, and selection, and passes it to its request builder, which maps it to the
/// <see cref="TextCodeActionRequest"/> the provider is asked about. The offsets are zero-based and
/// validated against the document text length: every offset lies within the document and the
/// selection offsets are ordered, so <see cref="SelectionStartOffset"/> is never greater than
/// <see cref="SelectionEndOffset"/>. The selection offsets equal <see cref="CaretOffset"/> when the
/// selection is empty, but the type does not require it because a selection does not have to
/// contain the caret. A multi-range selection is collapsed to the selection's surrounding segment, so
/// <see cref="SelectionStartOffset"/> and <see cref="SelectionEndOffset"/> describe one range; a host
/// that must act on each range separately has to read the editor's selection itself.
/// </para>
/// <para>
/// The document text is carried as a <see cref="TextCodeActionDocumentText"/> so it can be materialized
/// on first read: a controller that builds the context for every settled request does not copy the whole
/// document unless the host's request builder actually reads <see cref="DocumentText"/>.
/// </para>
/// <para>
/// The context is editor-state input for the host's request policy, not a provider contract: the
/// host decides which document range the provider is asked about - the caret position, the caret's
/// diagnostic span, or the selection - and returns that range as a
/// <see cref="TextCodeActionRequest"/>.
/// </para>
/// <para>
/// The type is a class, not a record struct: every offset is validated against the document text
/// length at construction, so the all-default value - which would carry empty text with offsets at
/// zero - cannot exist. Equality is structural over the three offsets only; the document text is
/// deliberately excluded so comparing two contexts never materializes the lazily-carried text.
/// <see cref="ToString"/> likewise excludes the text.
/// </para>
/// </remarks>
public sealed class TextCodeActionContext : IEquatable<TextCodeActionContext>
{
	private readonly TextCodeActionDocumentText _document;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCodeActionContext"/> class from
	/// already-materialized document text.
	/// </summary>
	/// <param name="documentText">The document text snapshot the offsets refer to.</param>
	/// <param name="caretOffset">The zero-based offset of the caret.</param>
	/// <param name="selectionStartOffset">
	/// The zero-based start offset of the selection, or the caret offset when the selection is empty.
	/// </param>
	/// <param name="selectionEndOffset">
	/// The zero-based end offset of the selection, or the caret offset when the selection is empty.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// An offset is negative or greater than the document text length, or
	/// <paramref name="selectionStartOffset"/> is greater than <paramref name="selectionEndOffset"/>.
	/// </exception>
	public TextCodeActionContext(
		string documentText,
		int caretOffset,
		int selectionStartOffset,
		int selectionEndOffset)
		: this(
			TextCodeActionDocumentText.FromText(documentText ?? throw new ArgumentNullException(nameof(documentText))),
			caretOffset,
			selectionStartOffset,
			selectionEndOffset)
	{
	}

	/// <summary>
	/// Creates a context from a document text that is materialized on demand.
	/// </summary>
	/// <remarks>
	/// The offsets are validated against <see cref="TextCodeActionDocumentText.TextLength"/>, so they can
	/// be rejected without materializing the text.
	/// </remarks>
	/// <param name="document">The document text the offsets refer to.</param>
	/// <param name="caretOffset">The zero-based offset of the caret.</param>
	/// <param name="selectionStartOffset">
	/// The zero-based start offset of the selection, or the caret offset when the selection is empty.
	/// </param>
	/// <param name="selectionEndOffset">
	/// The zero-based end offset of the selection, or the caret offset when the selection is empty.
	/// </param>
	/// <returns>The context.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="document"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// An offset is negative or greater than the document text length, or
	/// <paramref name="selectionStartOffset"/> is greater than <paramref name="selectionEndOffset"/>.
	/// </exception>
	public static TextCodeActionContext FromDocument(
		TextCodeActionDocumentText document,
		int caretOffset,
		int selectionStartOffset,
		int selectionEndOffset)
		=> new(document, caretOffset, selectionStartOffset, selectionEndOffset);

	private TextCodeActionContext(
		TextCodeActionDocumentText document,
		int caretOffset,
		int selectionStartOffset,
		int selectionEndOffset)
	{
		ArgumentNullException.ThrowIfNull(document);

		int textLength = document.TextLength;

		TextOffsetValidation.ValidateOffset(textLength, caretOffset, nameof(caretOffset), "caret");
		TextOffsetValidation.ValidateOffset(textLength, selectionStartOffset, nameof(selectionStartOffset), "selection start");
		TextOffsetValidation.ValidateOffset(textLength, selectionEndOffset, nameof(selectionEndOffset), "selection end");
		TextOffsetValidation.ValidateOrderedRange(selectionStartOffset, selectionEndOffset, nameof(selectionStartOffset), "selection");

		_document = document;
		CaretOffset = caretOffset;
		SelectionStartOffset = selectionStartOffset;
		SelectionEndOffset = selectionEndOffset;
	}

	/// <summary>Gets the document text snapshot the offsets refer to.</summary>
	/// <remarks>
	/// The read materializes the text on first access and caches it afterwards; the value is the text
	/// captured when the context was created, so it does not observe later document edits. Equality and
	/// <see cref="ToString"/> do not read this member, so neither materializes the text.
	/// </remarks>
	public string DocumentText => _document.Text;

	/// <summary>Gets the zero-based offset of the caret.</summary>
	public int CaretOffset { get; }

	/// <summary>
	/// Gets the zero-based start offset of the selection, or the caret offset when the selection is empty.
	/// </summary>
	public int SelectionStartOffset { get; }

	/// <summary>
	/// Gets the zero-based end offset of the selection, or the caret offset when the selection is empty.
	/// </summary>
	public int SelectionEndOffset { get; }

	/// <summary>
	/// Determines whether this context equals another by value: the caret and selection offsets only.
	/// </summary>
	/// <remarks>
	/// The document text is excluded, so two contexts built from different documents that share the
	/// same offsets compare equal and neither comparison materializes the carried text.
	/// </remarks>
	/// <param name="other">The context to compare against.</param>
	/// <returns><see langword="true"/> when the three offsets match; otherwise, <see langword="false"/>.</returns>
	public bool Equals(TextCodeActionContext? other)
		=> other is not null
			&& CaretOffset == other.CaretOffset
			&& SelectionStartOffset == other.SelectionStartOffset
			&& SelectionEndOffset == other.SelectionEndOffset;

	/// <inheritdoc/>
	public override bool Equals(object? obj) => Equals(obj as TextCodeActionContext);

	/// <summary>
	/// Determines whether two contexts are equal by value.
	/// </summary>
	/// <param name="left">The left context.</param>
	/// <param name="right">The right context.</param>
	/// <returns><see langword="true"/> when the contexts are equal; otherwise, <see langword="false"/>.</returns>
	public static bool operator ==(TextCodeActionContext? left, TextCodeActionContext? right)
		=> left is null ? right is null : left.Equals(right);

	/// <summary>
	/// Determines whether two contexts are unequal by value.
	/// </summary>
	/// <param name="left">The left context.</param>
	/// <param name="right">The right context.</param>
	/// <returns><see langword="true"/> when the contexts are unequal; otherwise, <see langword="false"/>.</returns>
	public static bool operator !=(TextCodeActionContext? left, TextCodeActionContext? right) => !(left == right);

	/// <inheritdoc/>
	public override int GetHashCode() => HashCode.Combine(CaretOffset, SelectionStartOffset, SelectionEndOffset);

	/// <inheritdoc/>
	/// <remarks>Excludes the document text, so the diagnostic string never materializes the text.</remarks>
	public override string ToString()
		=> $"TextCodeActionContext {{ CaretOffset = {CaretOffset}, "
			+ $"SelectionStartOffset = {SelectionStartOffset}, SelectionEndOffset = {SelectionEndOffset} }}";
}
