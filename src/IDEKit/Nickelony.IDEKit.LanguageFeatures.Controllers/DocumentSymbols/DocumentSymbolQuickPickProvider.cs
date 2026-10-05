using System.Globalization;
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.Matching;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;

/// <summary>Lists the symbols of the active document in the mode that the prefix <c>@</c> selects, or <c>@:</c> when it groups by kind.</summary>
/// <remarks>
/// <para>
/// The mode flattens the outline of <see cref="SymbolDocumentDescriptor.Provider"/>
/// in document order. A row shows the name of the symbol, the name of the symbol that contains it as the description, and the kind as the icon key,
/// as <see cref="DocumentSymbolKindExtensions.ToIconKey"/> spells it. The mode filters and ranks the rows itself, as VS Code does: the first word
/// of the query must match the name, the words after it must match the container, and a query that matches the name as a whole needs no container.
/// The rows with the best score come first, and equal scores keep document order. The grouped mode lists a separator per kind, with the number
/// of symbols of the kind. A symbol without a range is listed dimmed, because nothing can reveal it.
/// </para>
/// <para>
/// The preview selects the name of the active symbol in the editor and scrolls it into view while the user moves through the list. The view is put
/// back when the session ends without an accept, and Enter keeps the selection.
/// </para>
/// <para>
/// The outline is requested once for the provider, the document identifier and the text, and every query of the stay in the mode shares that request,
/// so typing sends no further one. The request ends with the stay: it is canceled and released when the session ends or the typed text switches
/// to another mode, and a newer query only stops waiting for it. An answer without a symbol or with a failure is not kept, because the provider
/// cannot tell a document without symbols from a failed request, so the next query asks again. A failure reaches <c>reportFault</c> once.
/// </para>
/// <para>
/// The mode asks <c>getDocument</c> on the dispatcher thread each time it lists, and keeps the editor it previewed in until the preview ends,
/// so an editor that stops being the active one meanwhile still gets its view back.
/// </para>
/// </remarks>
public sealed class DocumentSymbolQuickPickProvider : IQuickPickProvider
{
	private readonly Func<SymbolDocumentDescriptor?> _getDocument;
	private readonly DocumentSymbolQuickPickOptions _options;
	private readonly Action<Exception>? _reportFault;
	private readonly QuickPickGroup _symbolsGroup;
	private readonly Dictionary<DocumentSymbolKind, QuickPickGroup> _kindGroups;
	private readonly object _outlineGate = new();

	private (SymbolDocumentDescriptor Document, Task<IReadOnlyList<OutlineEntry>> Outline, CancellationTokenSource Cancellation)? _outline;
	private IEditorNavigationTarget? _previewed;
	private Exception? _lastReportedFault;

	/// <param name="getDocument">
	/// Gets the document to list the symbols of, or <see langword="null"/> when no document has an outline. It runs on the dispatcher thread.
	/// </param>
	/// <param name="options">The options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="reportFault">
	/// Receives the failure of the outline request, once. It runs on the dispatcher thread, so it may touch the UI. A <see langword="null"/> value
	/// drops the failure.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="getDocument"/> is <see langword="null"/>.</exception>
	public DocumentSymbolQuickPickProvider(
		Func<SymbolDocumentDescriptor?> getDocument,
		DocumentSymbolQuickPickOptions? options = null,
		Action<Exception>? reportFault = null
	)
	{
		ArgumentNullException.ThrowIfNull(getDocument);

		_getDocument = getDocument;
		_options = options ?? DocumentSymbolQuickPickOptions.Default;
		_reportFault = reportFault;
		_symbolsGroup = new QuickPickGroup("symbols", _options.SymbolsLabel, showsCount: true);
		_kindGroups = CreateKindGroups(_options);
	}

	/// <inheritdoc/>
	public string Prefix => _options.GroupByKind ? "@:" : "@";

	/// <inheritdoc/>
	public string Name => _options.GroupByKind ? _options.GroupedName : _options.Name;

	/// <inheritdoc/>
	public string Placeholder => _options.Placeholder;

	/// <inheritdoc/>
	public QuickPickFilterMode FilterMode => QuickPickFilterMode.None;

	/// <inheritdoc/>
	public string EmptyText => _options.EmptyText;

	/// <inheritdoc/>
	public async Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (_getDocument() is not { } document)
			return [Hint(_options.NoDocumentText)];

		Task<IReadOnlyList<OutlineEntry>> outline = ShareOutline(document);
		IReadOnlyList<OutlineEntry> entries;

		try
		{
			// The wait resumes on the dispatcher thread, so the fault below reaches the host there.
			entries = await outline.WaitAsync(cancellationToken).ConfigureAwait(true);
		}
		catch (Exception exception)
		{
			// A superseded query ends here. The end of the stay cancels the request, and that answer is dropped without a fault.
			cancellationToken.ThrowIfCancellationRequested();

			if (!outline.IsCanceled && !ReferenceEquals(Interlocked.Exchange(ref _lastReportedFault, exception), exception))
				_reportFault?.Invoke(exception);

			entries = [];
		}

		return entries.Count == 0 ? [Hint(_options.NoSymbolsText)] : Rank(document, entries, request.Query);
	}

	/// <inheritdoc/>
	public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		// The accept selects the symbol itself, so it does not depend on the preview having run.
		if (request.Item.Tag is SymbolNavigation navigation)
			navigation.Target.SelectRange(navigation.Range);

		_previewed = null;

		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public void Preview(QuickPickItem item)
	{
		ArgumentNullException.ThrowIfNull(item);

		if (item.Tag is not SymbolNavigation navigation)
			return;

		if (!ReferenceEquals(_previewed, navigation.Target))
		{
			_previewed?.RestoreView();
			navigation.Target.SaveView();
			_previewed = navigation.Target;
		}

		navigation.Target.SelectRange(navigation.Range);
	}

	/// <inheritdoc/>
	public void CancelPreview()
	{
		_previewed?.RestoreView();
		_previewed = null;
	}

	/// <inheritdoc/>
	public void OnDeactivated()
	{
		CancellationTokenSource? cancellation;

		lock (_outlineGate)
			cancellation = TakeOutline();

		Cancel(cancellation);
	}

	// Every query of a stay waits for one request. A request that failed or was canceled is replaced by the next query, and so is one for another text.
	private Task<IReadOnlyList<OutlineEntry>> ShareOutline(SymbolDocumentDescriptor document)
	{
		TaskCompletionSource<IReadOnlyList<OutlineEntry>> source;
		CancellationTokenSource cancellation;
		CancellationTokenSource? replaced;

		lock (_outlineGate)
		{
			if (_outline is { } current && IsSameOutline(current.Document, document) && !current.Outline.IsFaulted && !current.Outline.IsCanceled)
				return current.Outline;

			replaced = TakeOutline();
			source = new TaskCompletionSource<IReadOnlyList<OutlineEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
			cancellation = new CancellationTokenSource();
			_outline = (document, source.Task, cancellation);
		}

		// The provider and the callbacks of a token are a host's, so they run outside the lock.
		Cancel(replaced);
		_ = FetchOutlineAsync(document, source, cancellation);

		return source.Task;
	}

	private async Task FetchOutlineAsync(
		SymbolDocumentDescriptor document,
		TaskCompletionSource<IReadOnlyList<OutlineEntry>> source,
		CancellationTokenSource cancellation
	)
	{
		try
		{
			IReadOnlyList<DocumentSymbol> symbols =
				await document
					.Provider.GetDocumentSymbolsAsync(
						new DocumentSymbolRequest(document.DocumentId, document.DocumentText, string.Empty),
						cancellation.Token
					)
					.ConfigureAwait(false)
				?? [];

			List<OutlineEntry> entries = CreateEntries(document, symbols);

			if (entries.Count == 0)
				Forget(source.Task);

			source.TrySetResult(entries);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			Forget(source.Task);
			source.TrySetCanceled();
		}
		catch (Exception exception)
		{
			// A request that canceled itself, such as a timeout of the provider, is a failure and not the end of the stay.
			Forget(source.Task);
			source.TrySetException(exception);
		}
	}

	// Drops the outline when it is the one that finished, so a newer outline of another text stays.
	private void Forget(Task outline)
	{
		CancellationTokenSource? cancellation = null;

		lock (_outlineGate)
		{
			if (_outline is { } current && ReferenceEquals(current.Outline, outline))
				cancellation = TakeOutline();
		}

		Cancel(cancellation);
	}

	// Callers hold the gate, and cancel the returned source after they leave it.
	private CancellationTokenSource? TakeOutline()
	{
		if (_outline is not { } current)
			return null;

		_outline = null;

		return current.Cancellation;
	}

	private static void Cancel(CancellationTokenSource? cancellation)
	{
		if (cancellation is null)
			return;

		try
		{
			cancellation.Cancel();
		}
		finally
		{
			cancellation.Dispose();
		}
	}

	// The same provider, document and text give the same outline, whichever editor shows the document.
	private static bool IsSameOutline(SymbolDocumentDescriptor first, SymbolDocumentDescriptor second)
	{
		return ReferenceEquals(first.Provider, second.Provider)
			&& string.Equals(first.DocumentId, second.DocumentId, StringComparison.Ordinal)
			&& string.Equals(first.DocumentText, second.DocumentText, StringComparison.Ordinal);
	}

	private static List<OutlineEntry> CreateEntries(SymbolDocumentDescriptor document, IReadOnlyList<DocumentSymbol> symbols)
	{
		var lineMap = TextLineMap.Build(document.DocumentText);
		var entries = new List<OutlineEntry>();

		Flatten(symbols, container: null, lineMap, entries);

		return entries;
	}

	private static void Flatten(IReadOnlyList<DocumentSymbol> symbols, string? container, TextLineMap lineMap, List<OutlineEntry> entries)
	{
		foreach (DocumentSymbol symbol in symbols)
		{
			// The name is what the user looks for, and a symbol that is a few hundred lines long would scroll its end into view instead.
			TextRange? range = symbol.SelectionRange ?? symbol.Range;

			entries.Add(
				new OutlineEntry(
					Name: symbol.Name,
					Kind: symbol.Kind,
					container,
					Range: range is { } textRange ? lineMap.GetPositionRange(textRange) : null
				)
			);

			Flatten(symbols: symbol.Children, container: symbol.Name, lineMap, entries);
		}
	}

	private List<QuickPickItem> Rank(SymbolDocumentDescriptor document, IReadOnlyList<OutlineEntry> entries, string query)
	{
		var matcher = new SymbolQueryMatcher(query);
		var matches = new List<(int Score, int Index, QuickPickItem Item)>(entries.Count);

		for (int index = 0; index < entries.Count; index++)
		{
			OutlineEntry entry = entries[index];

			if (
				matcher.TryMatch(
					name: entry.Name,
					container: entry.Container,
					out int score,
					out IReadOnlyList<MatchRange> nameRanges,
					out IReadOnlyList<MatchRange> containerRanges
				)
			)
			{
				matches.Add((score, index, CreateItem(document, entry, index, labelHighlights: nameRanges, descriptionHighlights: containerRanges)));
			}
		}

		// The best score first, and document order among equal scores, as the symbol picker of VS Code orders its rows.
		matches.Sort(
			static (first, second) => first.Score != second.Score ? second.Score.CompareTo(first.Score) : first.Index.CompareTo(second.Index)
		);

		return [.. matches.Select(static match => match.Item)];
	}

	private QuickPickItem CreateItem(
		SymbolDocumentDescriptor document,
		OutlineEntry entry,
		int index,
		IReadOnlyList<MatchRange> labelHighlights,
		IReadOnlyList<MatchRange> descriptionHighlights
	)
	{
		return new(index.ToString(CultureInfo.InvariantCulture), entry.Name)
		{
			Description = entry.Container,
			IconKey = entry.Kind.ToIconKey(),
			Group = _options.GroupByKind ? _kindGroups[entry.Kind] : _symbolsGroup,
			IsEnabled = entry.Range is not null,
			LabelHighlights = labelHighlights,
			DescriptionHighlights = descriptionHighlights,
			Tag = entry.Range is { } range ? new SymbolNavigation(document.Target, range) : null,
		};
	}

	// The separators of the grouped mode are listed alphabetically by their text, so the order of a group is the rank of its text.
	private static Dictionary<DocumentSymbolKind, QuickPickGroup> CreateKindGroups(DocumentSymbolQuickPickOptions options)
	{
		var comparer = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);

		DocumentSymbolKind[] ranked =
		[
			.. Enum.GetValues<DocumentSymbolKind>().OrderBy(kind => options.GroupLabel(kind) ?? string.Empty, comparer).ThenBy(kind => kind),
		];

		var groups = new Dictionary<DocumentSymbolKind, QuickPickGroup>(ranked.Length);

		for (int rank = 0; rank < ranked.Length; rank++)
		{
			groups[ranked[rank]] = new QuickPickGroup(
				id: ranked[rank].ToIconKey(),
				label: options.GroupLabel(ranked[rank]),
				showsCount: true,
				order: rank
			);
		}

		return groups;
	}

	private static QuickPickItem Hint(string text) => new("hint", text) { IsEnabled = false };

	private readonly record struct OutlineEntry(string Name, DocumentSymbolKind Kind, string? Container, TextPositionRange? Range);
}
