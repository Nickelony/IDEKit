using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Describes the word being typed at a caret offset and the range it occupies.</summary>
/// <remarks>
/// An empty word is normalized to the absent state, and the range is stored as supplied; the kernel validates it against the snapshot,
/// because a decision's replacement range is snapshot-relative. <see cref="Word"/> filters completion items and <see cref="Range"/> is the document
/// span replaced on commit, and equality compares the normalized word and the range.
/// </remarks>
public readonly record struct CompletionWordSpan
{
	private readonly string? _word;

	/// <param name="word">
	/// The word being typed at the caret, or <see langword="null"/> when there is none. An empty value is normalized to the same state
	/// as <see langword="null"/>.
	/// </param>
	/// <param name="range">The zero-based range the word occupies, used as the completion replacement range.</param>
	public CompletionWordSpan(string? word, TextRange range)
	{
		_word = string.IsNullOrEmpty(word) ? null : word;
		Range = range;
	}

	/// <summary>The word being typed at the caret, or an empty string when there is none.</summary>
	public string Word => _word ?? string.Empty;

	/// <summary>The zero-based range the word occupies, used as the completion replacement range.</summary>
	public TextRange Range { get; }

	/// <summary>Deconstructs the span into its word and range.</summary>
	/// <param name="word">The word being typed at the caret, or an empty string when there is none.</param>
	/// <param name="range">The zero-based range the word occupies.</param>
	public void Deconstruct(out string word, out TextRange range)
	{
		word = Word;
		range = Range;
	}
}
