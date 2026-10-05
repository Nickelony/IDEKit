using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

/// <summary>Describes the editor state a code-action request is built from.</summary>
/// <remarks>
/// <see cref="Range"/> is an empty range at <see cref="CaretOffset"/> when nothing is selected; a non-empty selection need not contain the caret,
/// and a multi-range selection collapses to one range. The document is an <see cref="ITextSnapshot"/>, so building the context copies no text;
/// a host maps the context to the <c>CodeActionRequest</c> a provider sees.
/// </remarks>
public sealed class CodeActionSelection : IEquatable<CodeActionSelection>
{
	/// <summary>Initializes a new instance of the <see cref="CodeActionSelection"/> class.</summary>
	/// <param name="snapshot">The document snapshot the offsets refer to.</param>
	/// <param name="caretOffset">The zero-based offset of the caret.</param>
	/// <param name="range">The selected range, or an empty range at the caret offset when nothing is selected.</param>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="caretOffset"/> is negative or greater than the snapshot length, or <paramref name="range"/> extends beyond the end
	/// of the snapshot.
	/// </exception>
	public CodeActionSelection(ITextSnapshot snapshot, int caretOffset, TextRange range)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		TextOffsetValidation.ValidateOffset(
			textLength: snapshot.TextLength,
			offset: caretOffset,
			parameterName: nameof(caretOffset),
			offsetDescription: "caret"
		);

		TextOffsetValidation.ValidateRange(textLength: snapshot.TextLength, range, parameterName: nameof(range), rangeDescription: "selection");

		Snapshot = snapshot;
		CaretOffset = caretOffset;
		Range = range;
	}

	/// <summary>The document snapshot the offsets refer to.</summary>
	/// <remarks>
	/// The snapshot is immutable, so it does not observe later document edits. Equality and <see cref="ToString"/> do not read it.
	/// </remarks>
	public ITextSnapshot Snapshot { get; }

	/// <summary>The zero-based offset of the caret.</summary>
	public int CaretOffset { get; }

	/// <summary>The selected range, or an empty range at the caret offset when nothing is selected.</summary>
	public TextRange Range { get; }

	/// <summary>Returns the whole document text of <see cref="Snapshot"/>.</summary>
	/// <returns>A copy of the document text.</returns>
	/// <remarks>
	/// Each call copies the text, so a request builder reads it once per settled request, and only when it builds a request.
	/// </remarks>
	public string GetText() => Snapshot.GetText(0, Snapshot.TextLength);

	/// <summary>Determines whether this context equals another by value: the caret offset and the selection only.</summary>
	/// <param name="other">The context to compare against.</param>
	/// <returns><see langword="true"/> when the caret offset and the selection match; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The snapshot is excluded, so two contexts built from different documents that share the same offsets compare equal.
	/// </remarks>
	public bool Equals(CodeActionSelection? other) => other is not null && CaretOffset == other.CaretOffset && Range == other.Range;

	/// <inheritdoc/>
	public override bool Equals(object? obj) => Equals(obj as CodeActionSelection);

	/// <summary>Determines whether two contexts are equal by value.</summary>
	/// <param name="left">The left context.</param>
	/// <param name="right">The right context.</param>
	/// <returns><see langword="true"/> when the contexts are equal; otherwise, <see langword="false"/>.</returns>
	public static bool operator ==(CodeActionSelection? left, CodeActionSelection? right) => left is null ? right is null : left.Equals(right);

	/// <summary>Determines whether two contexts are unequal by value.</summary>
	/// <param name="left">The left context.</param>
	/// <param name="right">The right context.</param>
	/// <returns><see langword="true"/> when the contexts are unequal; otherwise, <see langword="false"/>.</returns>
	public static bool operator !=(CodeActionSelection? left, CodeActionSelection? right) => !(left == right);

	/// <inheritdoc/>
	public override int GetHashCode() => HashCode.Combine(CaretOffset, Range);

	/// <inheritdoc/>
	/// <remarks>
	/// Excludes the snapshot, so the diagnostic string never reads the text.
	/// </remarks>
	public override string ToString() => $"CodeActionSelection {{ CaretOffset = {CaretOffset}, Range = {Range} }}";
}
