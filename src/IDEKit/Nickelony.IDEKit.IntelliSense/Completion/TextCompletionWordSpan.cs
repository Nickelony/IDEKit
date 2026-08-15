using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Describes the word being typed at a caret offset and the range it occupies.
/// </summary>
/// <remarks>
/// <para>
/// The word is normalized (an empty value behaves like an absent one); the range is stored as
/// supplied, and the completion session kernel validates it against the snapshot when the span is
/// passed to <see cref="TextCompletionSessionKernel.GetDecision"/> - a range outside the snapshot is
/// rejected there because a decision's replacement range is snapshot-relative. The
/// <see cref="Word"/> is used to filter completion items and the <see cref="Range"/> defines the
/// document span replaced when a completion is committed. Languages with word or replacement-range
/// rules that differ from the kernel default supply their own <see cref="TextCompletionWordSpan"/> through a
/// <see cref="TextCompletionWordLocator"/> or as the per-call <c>wordSpan</c> argument.
/// </para>
/// <para>
/// <see cref="Word"/> reports an empty string for a <see langword="null"/> or empty assigned value,
/// so <c>default(TextCompletionWordSpan)</c> behaves like an explicitly empty word and is safe to
/// pass to filtering code. Its range is <c>(0, 0)</c>, so when the kernel receives the default as an
/// explicit <c>wordSpan</c> it collapses the replacement range at the document start and never
/// invokes the configured locator. Equality compares the normalized word and the range.
/// </para>
/// </remarks>
public readonly record struct TextCompletionWordSpan
{
	private readonly string? _word;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionWordSpan"/> struct.
	/// </summary>
	/// <param name="word">
	/// The word being typed at the caret, or <see langword="null"/> when there is none. An empty
	/// value is normalized to the same state as <see langword="null"/>.
	/// </param>
	/// <param name="range">The zero-based range the word occupies, used as the completion replacement range.</param>
	public TextCompletionWordSpan(string? word, TextRange range)
	{
		_word = string.IsNullOrEmpty(word) ? null : word;
		Range = range;
	}

	/// <summary>
	/// Gets the word being typed at the caret, or an empty string when there is none.
	/// </summary>
	public string Word => _word ?? string.Empty;

	/// <summary>
	/// Gets the zero-based range the word occupies, used as the completion replacement range.
	/// </summary>
	public TextRange Range { get; }

	/// <summary>
	/// Deconstructs the span into its word and range.
	/// </summary>
	/// <param name="word">The word being typed at the caret, or an empty string when there is none.</param>
	/// <param name="range">The zero-based range the word occupies.</param>
	public void Deconstruct(out string word, out TextRange range)
	{
		word = Word;
		range = Range;
	}
}
