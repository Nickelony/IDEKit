using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Computes shared completion-session decisions from a provider, a document snapshot, and the word being typed at the caret.</summary>
/// <remarks>
/// Custom word and replacement-range rules come from a <see cref="CompletionWordLocator"/> or a per-call <see cref="CompletionWordSpan"/>,
/// and a language-specific <see cref="CompletionItemFilter"/> may be injected. The kernel holds no per-request state and adopts an injected filter's
/// list without a copy; locators and filters must be thread-safe when the kernel is shared.
/// </remarks>
public sealed class CompletionSessionKernel
{
	private readonly CompletionWordLocator _wordLocator;
	private readonly CompletionItemFilter _filter;

	/// <param name="wordLocator">
	/// The strategy that locates the word being typed and its replacement range, or <see langword="null"/> to use the default locator,
	/// which resolves the identifier run before the caret with <see cref="IdentifierCharacterPolicy.Default"/>.
	/// </param>
	/// <param name="filter">
	/// The strategy that reduces completion items to those matching the word being typed,
	/// or <see langword="null"/> to use <see cref="CompletionFilter.FilterByWord"/>. The strategy must return a non-<see langword="null"/> list.
	/// </param>
	public CompletionSessionKernel(CompletionWordLocator? wordLocator = null, CompletionItemFilter? filter = null)
	{
		_wordLocator = wordLocator ?? LocateDefaultWord;
		_filter = filter ?? CompletionFilter.FilterByWord;
	}

	/// <summary>
	/// Computes a completion session decision by asking the provider for the snapshot's completion items, with the word and replacement range
	/// that the configured <see cref="CompletionWordLocator"/> finds at the caret.
	/// </summary>
	/// <param name="documentId">The identifier of the document the snapshot belongs to.</param>
	/// <param name="provider">The completion provider to invoke.</param>
	/// <param name="snapshot">The document snapshot the caret refers to.</param>
	/// <param name="caretOffset">The zero-based caret offset within <paramref name="snapshot"/>.</param>
	/// <param name="context">
	/// The reason the completion request was triggered, or <see langword="null"/> when the caller does not specify one; the request carries
	/// <see cref="CompletionContext.Invoked"/> in that case.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the provider request.</param>
	/// <returns>
	/// One of the following decisions:
	/// <list type="bullet">
	/// <item><description>An opening decision that carries the filtered items and the replacement range.</description></item>
	/// <item>
	/// <description>
	/// <see cref="CompletionSessionDirective.NoMatches"/> when the provider returned items but the current word filtered every one out.
	/// </description>
	/// </item>
	/// <item><description><see cref="CompletionSessionDirective.None"/> when the provider returned no items.</description></item>
	/// </list>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/>, <paramref name="provider"/> or <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before the provider answers.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="caretOffset"/> is negative or greater than the snapshot text length.</exception>
	/// <exception cref="InvalidOperationException">
	/// The provider or the configured completion item filter returned <see langword="null"/>
	/// instead of an item list or returned a list containing a <see langword="null"/> item, or the configured word locator returned a range
	/// that lies outside the snapshot. All are return-contract violations; see <see cref="ICompletionProvider"/>, <see cref="CompletionItemFilter"/>,
	/// and <see cref="CompletionWordLocator"/>.
	/// </exception>
	public Task<CompletionSessionDirective> GetDirectiveAsync(
		string documentId,
		ICompletionProvider provider,
		ITextSnapshot snapshot,
		int caretOffset,
		CompletionContext? context = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		ArgumentNullException.ThrowIfNull(provider);
		ArgumentNullException.ThrowIfNull(snapshot);

		(CompletionRequest request, CompletionWordSpan word) = ValidateAndBuildRequest(documentId, snapshot, caretOffset, context, wordSpan: null);

		return GetDecisionCoreAsync(provider, request, word, cancellationToken);
	}

	/// <summary>
	/// Computes a completion session decision by asking the provider for the snapshot's completion items, with a word and replacement range
	/// the caller supplies in place of the configured <see cref="CompletionWordLocator"/>.
	/// </summary>
	/// <param name="documentId">The identifier of the document the snapshot belongs to.</param>
	/// <param name="provider">The completion provider to invoke.</param>
	/// <param name="snapshot">The document snapshot the caret refers to.</param>
	/// <param name="caretOffset">The zero-based caret offset within <paramref name="snapshot"/>.</param>
	/// <param name="wordSpan">
	/// The word and replacement range. The range lies within the snapshot and contains the caret, as the LSP requires of a completion's
	/// replacement range.
	/// </param>
	/// <param name="context">
	/// The reason the completion request was triggered, or <see langword="null"/> when the caller does not specify one; the request carries
	/// <see cref="CompletionContext.Invoked"/> in that case.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the provider request.</param>
	/// <returns>The decision, as <see cref="GetDirectiveAsync"/> returns it.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/>, <paramref name="provider"/> or <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="documentId"/> is blank, or the range of <paramref name="wordSpan"/> does not contain the caret.
	/// </exception>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled before the provider answers.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="caretOffset"/> is negative or greater than the snapshot text length, or the range of <paramref name="wordSpan"/> lies
	/// outside the snapshot.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The provider or the configured completion item filter returned <see langword="null"/> instead of an item list or returned a list containing
	/// a <see langword="null"/> item; see <see cref="ICompletionProvider"/> and <see cref="CompletionItemFilter"/>.
	/// </exception>
	public Task<CompletionSessionDirective> GetDirectiveForWordSpanAsync(
		string documentId,
		ICompletionProvider provider,
		ITextSnapshot snapshot,
		int caretOffset,
		CompletionWordSpan wordSpan,
		CompletionContext? context = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		ArgumentNullException.ThrowIfNull(provider);
		ArgumentNullException.ThrowIfNull(snapshot);

		(CompletionRequest request, CompletionWordSpan word) = ValidateAndBuildRequest(documentId, snapshot, caretOffset, context, wordSpan);

		return GetDecisionCoreAsync(provider, request, word, cancellationToken);
	}

	/// <summary>
	/// Moves a decision that was computed for an earlier snapshot onto the word at the caret now, when the caret has only moved on within that word
	/// since the request.
	/// </summary>
	/// <param name="directive">The decision, computed against <paramref name="requestSnapshot"/>.</param>
	/// <param name="requestSnapshot">The snapshot the decision was computed against.</param>
	/// <param name="snapshot">The snapshot of the document now.</param>
	/// <param name="caretOffset">The zero-based caret offset within <paramref name="snapshot"/>.</param>
	/// <param name="retargeted">The decision to apply when the method returns <see langword="true"/>; otherwise, the default value.</param>
	/// <returns>
	/// <see langword="true"/> when the decision still describes the word at the caret; otherwise, <see langword="false"/>, and the caller discards
	/// the decision.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="requestSnapshot"/> or <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="caretOffset"/> is negative or greater than the snapshot text length.</exception>
	/// <exception cref="InvalidOperationException">The configured word locator returned a range that lies outside the snapshot.</exception>
	/// <remarks>
	/// <para>
	/// The configured <see cref="CompletionWordLocator"/> finds the word at the caret now. The decision stays valid when that word
	/// starts where the decision's replacement range starts, ends no earlier than the range does, and begins with the text the range held
	/// in the request snapshot. The returned decision then replaces the grown word, so a list opens filtered by the text typed since the request,
	/// and the items need no new request because the filter over a longer word only narrows them.
	/// </para>
	/// <para>
	/// A caret that left the word, moved back into it, or sits in text that an edit before the word shifted fails the test.
	/// A decision without a replacement range carries nothing to move and is returned unchanged.
	/// </para>
	/// </remarks>
	public bool TryRetarget(
		CompletionSessionDirective directive,
		ITextSnapshot requestSnapshot,
		ITextSnapshot snapshot,
		int caretOffset,
		out CompletionSessionDirective retargeted
	)
	{
		ArgumentNullException.ThrowIfNull(requestSnapshot);
		ArgumentNullException.ThrowIfNull(snapshot);

		TextOffsetValidation.ValidateOffset(
			textLength: snapshot.TextLength,
			offset: caretOffset,
			parameterName: nameof(caretOffset),
			offsetDescription: "caret"
		);

		retargeted = directive;

		if (directive.ReplacementRange is not { } range)
			return true;

		TextRange word = _wordLocator(snapshot, caretOffset).Range;

		if (!word.FitsWithin(snapshot.TextLength))
			throw new InvalidOperationException("The completion word locator returned a range outside the snapshot.");

		if (
			word.Offset != range.Offset
			|| word.EndOffset < range.EndOffset
			|| !range.FitsWithin(requestSnapshot.TextLength)
			|| !string.Equals(
				requestSnapshot.GetText(range.Offset, range.Length),
				snapshot.GetText(range.Offset, range.Length),
				StringComparison.Ordinal
			)
		)
		{
			retargeted = default;
			return false;
		}

		if (word != range)
			retargeted = new CompletionSessionDirective(directive.ShouldClose, directive.Items, word);

		return true;
	}

	private async Task<CompletionSessionDirective> GetDecisionCoreAsync(
		ICompletionProvider provider,
		CompletionRequest request,
		CompletionWordSpan word,
		CancellationToken cancellationToken
	)
	{
		cancellationToken.ThrowIfCancellationRequested();

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(request, cancellationToken).ConfigureAwait(false);

		// A provider that ignores the token still must not turn a canceled request into a decision.
		cancellationToken.ThrowIfCancellationRequested();

		return CreateDecision(items, word);
	}

	private (CompletionRequest Request, CompletionWordSpan Word) ValidateAndBuildRequest(
		string documentId,
		ITextSnapshot snapshot,
		int caretOffset,
		CompletionContext? context,
		CompletionWordSpan? wordSpan
	)
	{
		// The caret is validated before the snapshot text is materialized, so an out-of-range caret
		// never pays for the document-sized copy and custom word locators only ever see in-range values.
		TextOffsetValidation.ValidateOffset(
			textLength: snapshot.TextLength,
			offset: caretOffset,
			parameterName: nameof(caretOffset),
			offsetDescription: "caret"
		);

		CompletionWordSpan word = wordSpan ?? _wordLocator(snapshot, caretOffset);

		// The replacement range is snapshot-relative whoever produced it: an explicit span is caller
		// input, while a locator result that leaves the snapshot is a return-contract violation. Both
		// are checked before the request text is materialized, so an invalid range skips the copy.
		if (wordSpan is not null)
		{
			TextOffsetValidation.ValidateRange(
				textLength: snapshot.TextLength,
				range: word.Range,
				parameterName: nameof(wordSpan),
				rangeDescription: "word span"
			);

			// The caret sits inside the word it completes, so a span elsewhere, such as an unset default one, is a caller error.
			if (caretOffset < word.Range.Offset || caretOffset > word.Range.EndOffset)
				throw new ArgumentException("The word span must contain the caret.", nameof(wordSpan));
		}
		else if (!word.Range.FitsWithin(snapshot.TextLength))
		{
			throw new InvalidOperationException("The completion word locator returned a range outside the snapshot.");
		}

		var request = new CompletionRequest(documentId, documentText: snapshot.GetText(0, snapshot.TextLength), caretOffset, context);

		return (request, word);
	}

	private CompletionSessionDirective CreateDecision(IReadOnlyList<CompletionItem> items, CompletionWordSpan wordSpan)
	{
		if (items is null)
			throw new InvalidOperationException("The completion provider returned null instead of an item list.");

		ValidateItems(items, "completion provider");

		IReadOnlyList<CompletionItem> filtered =
			_filter(items, wordSpan.Word) ?? throw new InvalidOperationException("The configured completion item filter returned null.");

		ValidateItems(filtered, "configured completion item filter");

		// The filtered list is freshly owned per call (or transferred by the filter), so the decision
		// adopts it directly instead of copying it again through Open. An empty provider result and a
		// word that filtered every candidate out are reported separately so a host can dismiss a
		// session that narrowed to nothing while leaving one with no candidates unchanged.
		if (filtered.Count > 0)
			return new CompletionSessionDirective(false, filtered, wordSpan.Range);

		return items.Count == 0 ? CompletionSessionDirective.None : CompletionSessionDirective.NoMatches;
	}

	/// <summary>
	/// Rejects an item list that carries a <see langword="null"/> element, so the failure names the offending producer instead of surfacing later
	/// inside a host that reads an item.
	/// </summary>
	/// <param name="items">The item list to validate.</param>
	/// <param name="producer">The producer description used in the exception message.</param>
	/// <exception cref="InvalidOperationException">An element is <see langword="null"/>.</exception>
	private static void ValidateItems(IReadOnlyList<CompletionItem> items, string producer)
	{
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i] is null)
				throw new InvalidOperationException($"The {producer} returned a null item at index {i}.");
		}
	}

	private static CompletionWordSpan LocateDefaultWord(ITextSnapshot snapshot, int caretOffset)
	{
		// Resolves the identifier run that ends at the caret; the character rule comes from
		// IdentifierCharacterPolicy.Default so completion, hover, and navigation share one definition.
		TextRange? span = IdentifierOperations.FindTokenSpan(
			snapshot,
			offset: caretOffset,
			policy: IdentifierCharacterPolicy.Default,
			mode: IdentifierSpanMode.EndingAtOffset
		);

		if (span is null)
		{
			// No identifier precedes the caret, so the replacement range collapses at the caret.
			// The caret offset was validated against the snapshot before the locator ran.
			return new CompletionWordSpan(string.Empty, new TextRange(caretOffset, 0));
		}

		TextRange range = span.Value;

		return new CompletionWordSpan(snapshot.GetText(range.Offset, range.Length), range);
	}
}
