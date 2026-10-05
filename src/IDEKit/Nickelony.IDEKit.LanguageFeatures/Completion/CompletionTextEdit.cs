using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Completion;

/// <summary>Describes the insert and replace ranges supplied by a completion item using zero-based UTF-16 document offsets.</summary>
/// <remarks>
/// Neutral offsets let the payload cross provider and editor boundaries without protocol coupling: a provider that receives protocol line
/// and character coordinates translates them against the request-time snapshot before constructing an item. <see cref="NewText"/> is the committed
/// text (falling back to <see cref="CompletionItem.InsertText"/>), and <see cref="ReplaceRange"/> must start at <see cref="InsertRange"/>'s offset
/// and contain it. <see cref="ReplacementRange"/> falls back to the insert range when <see cref="ReplaceRange"/> is <see langword="null"/>.
/// </remarks>
public readonly record struct CompletionTextEdit
{
	/// <param name="insertRange">The insert range for the completion edit.</param>
	/// <param name="replaceRange">The optional replace range; its relation to <paramref name="insertRange"/> is documented on the type.</param>
	/// <param name="newText">
	/// The optional replacement text the edit commits, or <see langword="null"/> when the commit uses <see cref="CompletionItem.InsertText"/>.
	/// </param>
	/// <exception cref="ArgumentException">
	/// <paramref name="replaceRange"/> does not start at the insert range's offset, or does not contain the insert range.
	/// </exception>
	public CompletionTextEdit(TextRange insertRange, TextRange? replaceRange = null, string? newText = null)
	{
		ValidateRanges(insertRange, replaceRange, nameof(replaceRange));

		InsertRange = insertRange;
		ReplaceRange = replaceRange;
		NewText = newText;
	}

	public TextRange InsertRange { get; }

	/// <summary>
	/// The optional replace range; <see cref="ReplacementRange"/> falls back to <see cref="InsertRange"/> when it is <see langword="null"/>.
	/// </summary>
	public TextRange? ReplaceRange { get; }

	/// <summary>
	/// The optional replacement text the edit commits, or <see langword="null"/> when the commit uses <see cref="CompletionItem.InsertText"/>.
	/// </summary>
	public string? NewText { get; init; }

	/// <summary>The effective replacement range, falling back to <see cref="InsertRange"/>.</summary>
	public TextRange ReplacementRange => ReplaceRange ?? InsertRange;

	/// <summary>Deconstructs the edit into its components.</summary>
	/// <param name="insertRange">Receives the insert range.</param>
	/// <param name="replaceRange">Receives the optional replace range.</param>
	/// <param name="newText">Receives the optional replacement text.</param>
	public void Deconstruct(out TextRange insertRange, out TextRange? replaceRange, out string? newText)
	{
		insertRange = InsertRange;
		replaceRange = ReplaceRange;
		newText = NewText;
	}

	/// <summary>Attempts to create a completion text edit, reporting whether the range pair is valid.</summary>
	/// <param name="insertRange">The insert range for the completion edit.</param>
	/// <param name="replaceRange">The optional replace range; its relation to <paramref name="insertRange"/> is documented on the type.</param>
	/// <param name="newText">
	/// The optional replacement text the edit commits, or <see langword="null"/> when the commit uses <see cref="CompletionItem.InsertText"/>.
	/// </param>
	/// <param name="edit">Receives the created edit, or the default value when the pair is invalid.</param>
	/// <returns><see langword="true"/> when <paramref name="replaceRange"/> is valid; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// A tolerant producer (for example a protocol parser) uses this method to detect an edit whose range pair violates the insert/replace relation
	/// and can still consume the replacement through a single-range edit instead of failing the whole item.
	/// </remarks>
	public static bool TryCreate(TextRange insertRange, TextRange? replaceRange, string? newText, out CompletionTextEdit edit)
	{
		if (!IsRangePairValid(insertRange, replaceRange))
		{
			edit = default;
			return false;
		}

		edit = new CompletionTextEdit(insertRange, replaceRange, newText);
		return true;
	}

	/// <summary>Determines whether a replace range value starts at the insert range's offset and contains the insert range.</summary>
	/// <param name="insertRange">The insert range.</param>
	/// <param name="replaceRange">The optional replace range.</param>
	/// <returns><see langword="true"/> when the pair is valid; otherwise, <see langword="false"/>.</returns>
	private static bool IsRangePairValid(TextRange insertRange, TextRange? replaceRange) =>
		replaceRange is not { } replace || (replace.Offset == insertRange.Offset && replace.EndOffset >= insertRange.EndOffset);

	/// <summary>Validates that a replace range starts at the insert range's offset and contains the insert range.</summary>
	/// <param name="insertRange">The insert range.</param>
	/// <param name="replaceRange">The optional replace range.</param>
	/// <param name="parameterName">The parameter or property name reported by the exception.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="replaceRange"/> does not start at the insert range's offset, or does not contain the insert range.
	/// </exception>
	private static void ValidateRanges(TextRange insertRange, TextRange? replaceRange, string parameterName)
	{
		if (replaceRange is not { } replace)
			return;

		if (replace.Offset != insertRange.Offset)
			throw new ArgumentException("The replace range must start at the same offset as the insert range.", parameterName);

		if (replace.EndOffset < insertRange.EndOffset)
			throw new ArgumentException("The replace range must contain the insert range.", parameterName);
	}
}
