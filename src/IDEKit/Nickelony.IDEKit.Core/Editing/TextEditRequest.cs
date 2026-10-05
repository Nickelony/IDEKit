using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Describes a single text edit: a replaced range, its replacement text, and an optional caret offset.</summary>
/// <remarks>
/// This is an editor command: a host passes it to an editor binding's <c>ApplyEdit</c> extension, which replaces the range and places the caret.
/// A batch that the edit kernel validates takes <see cref="TextIncrementalEdit"/> instead.
/// </remarks>
public readonly record struct TextEditRequest
{
	/// <param name="range">The range to replace; an empty range is a pure insertion.</param>
	/// <param name="newText">The replacement text.</param>
	/// <exception cref="ArgumentNullException"><paramref name="newText"/> is <see langword="null"/>.</exception>
	public TextEditRequest(TextRange range, string newText)
	{
		ArgumentNullException.ThrowIfNull(newText);

		Range = range;
		NewText = newText;
	}

	/// <summary>The range to replace; an empty range is a pure insertion.</summary>
	public TextRange Range { get; }

	public string NewText { get; }

	/// <summary>
	/// The desired zero-based caret offset after the edit, or <see langword="null"/> to place the caret immediately after <see cref="NewText"/>.
	/// </summary>
	/// <remarks>
	/// A negative offset clamps to zero and an offset past the target document's current length clamps to that length when the request is applied.
	/// </remarks>
	public int? CaretOffsetAfterEdit { get; init; }
}
