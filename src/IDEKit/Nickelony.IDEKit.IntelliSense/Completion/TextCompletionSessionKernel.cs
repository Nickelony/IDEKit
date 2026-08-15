using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Computes shared completion-session decisions from a provider, a document snapshot, and the word
/// being typed at the caret.
/// </summary>
/// <remarks>
/// <para>
/// The kernel owns the generic completion-session pipeline (provider invocation, current-word filtering,
/// word and replacement-range handling, and the Open, NoMatches, and None decisions). Languages with custom
/// word or replacement-range rules supply their own <see cref="TextCompletionWordLocator"/> to the constructor,
/// or a per-call <see cref="TextCompletionWordSpan"/>, and may inject a language-specific
/// <see cref="TextCompletionItemFilter"/>. The kernel is synchronous and retains no per-request state, so a
/// custom filter's returned list is passed to the decision without a copy and must not be mutated afterwards,
/// and custom locators and filters must provide their own thread-safety when a kernel is shared. A supplied
/// or located word span must carry a range within the snapshot, which the kernel validates before building
/// the request.
/// </para>
/// <para>
/// Providers receive a <see cref="TextCompletionRequest"/> built from the snapshot text, the caret offset,
/// and the trigger; building the request materializes the snapshot as a string, so a call may allocate a
/// document-sized copy. Because every decision invokes the provider again, a host that re-runs the kernel
/// while typing re-queries the provider, and a host that caches decisions decides how an incomplete provider
/// result is handled.
/// </para>
/// </remarks>
public sealed class TextCompletionSessionKernel
{
	private readonly TextCompletionWordLocator _wordLocator;
	private readonly TextCompletionItemFilter _filter;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionSessionKernel"/> class.
	/// </summary>
	/// <param name="wordLocator">
	/// The strategy that locates the word being typed and its replacement range, or <see langword="null"/> to use
	/// the default locator, which resolves the identifier run before the caret with
	/// <see cref="IdentifierCharacterPolicy.Default"/>.
	/// </param>
	/// <param name="filter">
	/// The strategy that reduces completion items to those matching the word being typed, or <see langword="null"/>
	/// to use <see cref="TextCompletionFilter.FilterByWord"/>. The strategy must return a non-null list.
	/// </param>
	public TextCompletionSessionKernel(
		TextCompletionWordLocator? wordLocator = null,
		TextCompletionItemFilter? filter = null)
	{
		_wordLocator = wordLocator ?? LocateDefaultWord;
		_filter = filter ?? TextCompletionFilter.FilterByWord;
	}

	/// <summary>
	/// Computes a completion session decision by invoking the provider on the calling thread.
	/// </summary>
	/// <param name="snapshot">The document snapshot the caret refers to.</param>
	/// <param name="caretOffset">The zero-based caret offset within <paramref name="snapshot"/>.</param>
	/// <param name="provider">The completion provider to invoke.</param>
	/// <param name="trigger">
	/// The reason the completion request was triggered, or <see langword="null"/> when the caller
	/// does not specify one; the request carries <see cref="TextCompletionTrigger.Invoked"/> in that
	/// case.
	/// </param>
	/// <param name="wordSpan">
	/// An optional explicit word and replacement range; when omitted, the configured
	/// <see cref="TextCompletionWordLocator"/> computes them from <paramref name="snapshot"/> and
	/// <paramref name="caretOffset"/>. Either way the range must lie within the snapshot, because a
	/// decision's replacement range is snapshot-relative: a supplied span outside the snapshot is
	/// rejected with <see cref="ArgumentOutOfRangeException"/>, and a locator result outside the
	/// snapshot is rejected with <see cref="InvalidOperationException"/>. Supplying
	/// <c>default(TextCompletionWordSpan)</c> (rather than <see langword="null"/>) disables the
	/// locator while collapsing the replacement range at the document start, so pass the caret's
	/// empty span instead.
	/// </param>
	/// <returns>
	/// One of the following decisions:
	/// <list type="bullet">
	/// <item><description>An opening decision that carries the filtered items and the replacement range.</description></item>
	/// <item><description><see cref="TextCompletionSessionDecision.NoMatches"/> when the provider returned items but the current word filtered every one out.</description></item>
	/// <item><description><see cref="TextCompletionSessionDecision.None"/> when the provider returned no items.</description></item>
	/// </list>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="snapshot"/> or <paramref name="provider"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="caretOffset"/> is negative or greater than the snapshot text length, or the
	/// supplied <paramref name="wordSpan"/> carries a range that lies outside the snapshot.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The provider returned <see langword="null"/> instead of an item list, the provider or the
	/// configured completion item filter returned a list containing a <see langword="null"/> item, or
	/// the configured word locator returned a range that lies outside the snapshot. All are
	/// return-contract violations; see <see cref="ITextCompletionProvider"/>,
	/// <see cref="TextCompletionItemFilter"/>, and <see cref="TextCompletionWordLocator"/>.
	/// </exception>
	public TextCompletionSessionDecision GetDecision(
		ITextSnapshot snapshot,
		int caretOffset,
		ITextCompletionProvider provider,
		TextCompletionTrigger? trigger = null,
		TextCompletionWordSpan? wordSpan = null)
	{
		(TextCompletionRequest request, TextCompletionWordSpan word) = ValidateAndBuildRequest(
			snapshot, caretOffset, provider, trigger, wordSpan);

		return CreateDecision(provider.GetCompletionItems(request), word);
	}

	private (TextCompletionRequest Request, TextCompletionWordSpan Word) ValidateAndBuildRequest(
		ITextSnapshot snapshot,
		int caretOffset,
		ITextCompletionProvider provider,
		TextCompletionTrigger? trigger,
		TextCompletionWordSpan? wordSpan)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(provider);

		// The caret is validated before the snapshot text is materialized, so an out-of-range caret
		// never pays for the document-sized copy and custom word locators only ever see in-range values.
		TextOffsetValidation.ValidateOffset(snapshot.TextLength, caretOffset, nameof(caretOffset), "caret");

		TextCompletionWordSpan word = wordSpan ?? _wordLocator(snapshot, caretOffset);

		// The replacement range is snapshot-relative whoever produced it: an explicit span is caller
		// input, while a locator result that leaves the snapshot is a return-contract violation. Both
		// are checked before the request text is materialized, so an invalid range skips the copy.
		if (wordSpan is not null)
		{
			TextOffsetValidation.ValidateRange(snapshot.TextLength, word.Range, nameof(wordSpan), "word span");
		}
		else if (word.Range.Offset > snapshot.TextLength || word.Range.EndOffset > snapshot.TextLength)
		{
			throw new InvalidOperationException("The completion word locator returned a range outside the snapshot.");
		}

		var request = new TextCompletionRequest(snapshot.GetText(0, snapshot.TextLength), caretOffset, trigger);

		return (request, word);
	}

	private TextCompletionSessionDecision CreateDecision(
		IReadOnlyList<TextCompletionItem> items,
		TextCompletionWordSpan wordSpan)
	{
		if (items is null)
			throw new InvalidOperationException("The completion provider returned null instead of an item list.");

		ValidateItems(items, "completion provider");

		IReadOnlyList<TextCompletionItem> filtered = _filter(items, wordSpan.Word);

		if (filtered is null)
			throw new InvalidOperationException("The configured completion item filter returned null.");

		ValidateItems(filtered, "configured completion item filter");

		// The filtered list is freshly owned per call (or transferred by the filter), so the decision
		// adopts it directly instead of copying it again through Open. An empty provider result and a
		// word that filtered every candidate out are reported separately so a host can dismiss a
		// session that narrowed to nothing while leaving one with no candidates unchanged.
		if (filtered.Count > 0)
			return new TextCompletionSessionDecision(false, filtered, wordSpan.Range);

		return items.Count == 0
			? TextCompletionSessionDecision.None
			: TextCompletionSessionDecision.NoMatches;
	}

	/// <summary>
	/// Rejects an item list that carries a <see langword="null"/> element, so the failure names the
	/// offending producer instead of surfacing later inside a host that reads an item.
	/// </summary>
	/// <param name="items">The item list to validate.</param>
	/// <param name="producer">The producer description used in the exception message.</param>
	/// <exception cref="InvalidOperationException">An element is <see langword="null"/>.</exception>
	private static void ValidateItems(IReadOnlyList<TextCompletionItem> items, string producer)
	{
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i] is null)
				throw new InvalidOperationException($"The {producer} returned a null item at index {i}.");
		}
	}

	private static TextCompletionWordSpan LocateDefaultWord(ITextSnapshot snapshot, int caretOffset)
	{
		// Resolves the identifier run that ends at the caret; the character rule comes from
		// IdentifierCharacterPolicy.Default so completion, hover, and navigation share one definition.
		TextRange? span = IdentifierOperations.FindTokenSpan(
			snapshot,
			caretOffset,
			IdentifierCharacterPolicy.Default,
			IdentifierSpanMode.EndingAtOffset);

		if (span is null)
		{
			// No identifier precedes the caret, so the replacement range collapses at the caret.
			// The caret offset was validated against the snapshot before the locator ran.
			return new TextCompletionWordSpan(string.Empty, new TextRange(caretOffset, 0));
		}

		TextRange range = span.Value;

		return new TextCompletionWordSpan(snapshot.GetText(range.Offset, range.Length), range);
	}
}
