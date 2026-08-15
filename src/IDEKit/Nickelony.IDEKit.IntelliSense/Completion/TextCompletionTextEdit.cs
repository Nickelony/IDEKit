using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Describes the insert and replace ranges supplied by a completion item using zero-based UTF-16
/// document offsets.
/// </summary>
/// <remarks>
/// <para>
/// Ranges use neutral document offsets rather than protocol line and character coordinates so the
/// payload can cross provider and editor boundaries without protocol coupling. Providers that
/// receive line and character coordinates translate them to offsets against the request-time
/// document snapshot before constructing an item.
/// </para>
/// <para>
/// Producers that receive protocol payloads apply the protocol's producer-side constraints when
/// building the ranges: the edit's ranges span a single line and contain the position at which
/// completion was requested.
/// </para>
/// <para>
/// <see cref="NewText"/> is the replacement text the edit commits. A commit path uses it when it is
/// present, and falls back to <see cref="TextCompletionItem.InsertText"/> when it is
/// <see langword="null"/>, so an explicit empty replacement stays distinguishable from an absent
/// edit text.
/// </para>
/// <para>
/// The ranges describe where the edit applies against the request-time snapshot. A commit path
/// applies the text at <see cref="InsertRange"/> in insert mode and at <see cref="ReplacementRange"/>
/// in replace mode while the range still fits the current document, and otherwise falls back to its
/// own live replacement segment for a stale range.
/// </para>
/// <para>
/// When <see cref="ReplaceRange"/> is present it starts at the insert range's offset and contains
/// the insert range; the constructor rejects a pair that violates this relation.
/// </para>
/// </remarks>
public readonly record struct TextCompletionTextEdit
{
	private readonly TextRange _insertRange;
	private readonly TextRange? _replaceRange;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionTextEdit"/> record struct.
	/// </summary>
	/// <param name="insertRange">The insert range for the completion edit.</param>
	/// <param name="replaceRange">
	/// The optional replace range; its relation to <paramref name="insertRange"/> is documented on
	/// the type.
	/// </param>
	/// <param name="newText">
	/// The optional replacement text the edit commits, or <see langword="null"/> when the commit uses
	/// <see cref="TextCompletionItem.InsertText"/>.
	/// </param>
	/// <exception cref="ArgumentException">
	/// <paramref name="replaceRange"/> does not start at the insert range's offset, or does not
	/// contain the insert range.
	/// </exception>
	public TextCompletionTextEdit(TextRange insertRange, TextRange? replaceRange = null, string? newText = null)
	{
		ValidateRanges(insertRange, replaceRange, nameof(replaceRange));

		_insertRange = insertRange;
		_replaceRange = replaceRange;
		NewText = newText;
	}

	/// <summary>
	/// Gets the insert range for the completion edit.
	/// </summary>
	public TextRange InsertRange => _insertRange;

	/// <summary>
	/// Gets the optional replace range; <see cref="ReplacementRange"/> falls back to
	/// <see cref="InsertRange"/> when it is <see langword="null"/>.
	/// </summary>
	public TextRange? ReplaceRange => _replaceRange;

	/// <summary>
	/// Gets the optional replacement text the edit commits, or <see langword="null"/> when the commit
	/// uses <see cref="TextCompletionItem.InsertText"/>.
	/// </summary>
	public string? NewText { get; init; }

	/// <summary>
	/// Gets the effective replacement range, falling back to <see cref="InsertRange"/>.
	/// </summary>
	public TextRange ReplacementRange => ReplaceRange ?? InsertRange;

	/// <summary>
	/// Deconstructs the edit into its components.
	/// </summary>
	/// <param name="insertRange">Receives the insert range.</param>
	/// <param name="replaceRange">Receives the optional replace range.</param>
	/// <param name="newText">Receives the optional replacement text.</param>
	public void Deconstruct(out TextRange insertRange, out TextRange? replaceRange, out string? newText)
	{
		insertRange = InsertRange;
		replaceRange = ReplaceRange;
		newText = NewText;
	}

	/// <summary>
	/// Attempts to create a completion text edit, reporting whether the range pair is valid.
	/// </summary>
	/// <remarks>
	/// A tolerant producer (for example a protocol parser) uses this method to detect an edit whose
	/// range pair violates the insert/replace relation and can still consume the replacement through a
	/// single-range edit instead of failing the whole item.
	/// </remarks>
	/// <param name="insertRange">The insert range for the completion edit.</param>
	/// <param name="replaceRange">
	/// The optional replace range; its relation to <paramref name="insertRange"/> is documented on
	/// the type.
	/// </param>
	/// <param name="newText">
	/// The optional replacement text the edit commits, or <see langword="null"/> when the commit uses
	/// <see cref="TextCompletionItem.InsertText"/>.
	/// </param>
	/// <param name="edit">Receives the created edit, or the default value when the pair is invalid.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="replaceRange"/> is valid; otherwise,
	/// <see langword="false"/>.
	/// </returns>
	public static bool TryCreate(TextRange insertRange, TextRange? replaceRange, string? newText, out TextCompletionTextEdit edit)
	{
		if (!IsRangePairValid(insertRange, replaceRange))
		{
			edit = default;
			return false;
		}

		edit = new TextCompletionTextEdit(insertRange, replaceRange, newText);
		return true;
	}

	/// <summary>
	/// Determines whether a replace range value starts at the insert range's offset and contains the
	/// insert range.
	/// </summary>
	/// <param name="insertRange">The insert range.</param>
	/// <param name="replaceRange">The optional replace range.</param>
	/// <returns><see langword="true"/> when the pair is valid; otherwise, <see langword="false"/>.</returns>
	private static bool IsRangePairValid(TextRange insertRange, TextRange? replaceRange)
		=> replaceRange is not { } replace
			|| (replace.Offset == insertRange.Offset && replace.EndOffset >= insertRange.EndOffset);

	/// <summary>
	/// Validates that a replace range starts at the insert range's offset and contains the insert range.
	/// </summary>
	/// <param name="insertRange">The insert range.</param>
	/// <param name="replaceRange">The optional replace range.</param>
	/// <param name="parameterName">The parameter or property name reported by the exception.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="replaceRange"/> does not start at the insert range's offset, or does not
	/// contain the insert range.
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
