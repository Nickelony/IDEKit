using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Describes one text replacement using a zero-based UTF-16 range.</summary>
/// <remarks>
/// This is the validated unit of a prepared edit batch: the edit kernel builds one from each valid batch input. The constructor is the only way
/// to produce a value and the properties are get-only, so no object initializer or <c>with</c> expression can bypass validation.
/// </remarks>
public sealed record TextEditOperation
{
	/// <param name="range">The zero-based range the operation replaces.</param>
	/// <param name="newText">The replacement text. An empty string deletes the range.</param>
	/// <param name="editIndex">The index of the edit within the caller's batch, used for deterministic ordering and diagnostics.</param>
	/// <exception cref="ArgumentNullException"><paramref name="newText"/> is <see langword="null"/>.</exception>
	public TextEditOperation(TextRange range, string newText, int editIndex)
	{
		ArgumentNullException.ThrowIfNull(newText);

		Range = range;
		NewText = newText;
		EditIndex = editIndex;
	}

	/// <summary>The zero-based range the operation replaces.</summary>
	public TextRange Range { get; }

	/// <summary>The replacement text; an empty string deletes the range.</summary>
	public string NewText { get; }

	/// <summary>The index of the edit within the caller's batch, used for deterministic ordering and diagnostics.</summary>
	public int EditIndex { get; }

	/// <summary>Indicates whether the operation changes nothing: it replaces an empty range with an empty string.</summary>
	/// <remarks>
	/// A no-op cannot conflict with another operation or violate batch ordering.
	/// </remarks>
	public bool IsNoOp => Range.IsEmpty && NewText.Length == 0;

	/// <summary>Deconstructs the operation into its source range, replacement text, and edit index.</summary>
	/// <param name="range">The zero-based range the operation replaces.</param>
	/// <param name="newText">The replacement text.</param>
	/// <param name="editIndex">The index of the edit within the caller's batch, used for deterministic ordering and diagnostics.</param>
	public void Deconstruct(out TextRange range, out string newText, out int editIndex)
	{
		range = Range;
		newText = NewText;
		editIndex = EditIndex;
	}
}
