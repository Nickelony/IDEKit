using System.Globalization;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.WorkspaceSymbols;

/// <summary>
/// Lists the symbols of the whole workspace that match the query, in the mode that the prefix <c>#</c> selects, and opens the one the user picks.
/// </summary>
/// <remarks>
/// <para>
/// The providers filter, as VS Code lets its servers filter: the mode sends the first word of the typed
/// query to every provider that <c>getProviders</c> returns, and lists the symbols that come back, the ones of the first provider first, in the order
/// each provider gave them. A symbol that two providers list alike (the same document, kind, container, name and start) is listed once, and the first
/// provider keeps it. The words after the first one must match the container or the path of the document, and the controller ranks
/// the rows by the first word without hiding one. A blank query sends nothing and lists nothing, because a server may answer it with every symbol
/// of its workspace, as LuaLS does. The request of every provider carries the token of the query, so a query that a newer one supersedes cancels
/// them all.
/// </para>
/// <para>
/// A provider that throws costs its own symbols, and the symbols of the other providers still list. The failures of one listing
/// reach <c>reportFault</c> after the providers answered, in the order of the providers, on the dispatcher thread. The query waits
/// <see cref="WorkspaceSymbolQuickPickOptions.QueryDebounceDelay"/> after typing, because each query costs a request per provider.
/// </para>
/// <para>
/// A row shows the name of the symbol, the name of the symbol that contains it and the path of its document as the description, and the kind
/// as the icon key, as <see cref="DocumentSymbolKindExtensions.ToIconKey"/> spells it. Accepting a row opens the symbol through <c>openAsync</c>
/// on the dispatcher thread. A symbol without a range goes through the <see cref="IWorkspaceSymbolResolver"/> of the provider that listed it first,
/// when that provider implements the contract, and a resolution that fails opens the symbol as it was.
/// </para>
/// <para>
/// <c>getProviders</c> runs on the dispatcher thread each time the mode lists, so a host whose language servers start and stop answers
/// with the providers that exist now. A provider of the language-server framework starts its stopped server for the query.
/// </para>
/// </remarks>
public sealed class WorkspaceSymbolQuickPickProvider : IQuickPickProvider
{
	private readonly Func<IReadOnlyList<IWorkspaceSymbolProvider>> _getProviders;
	private readonly Func<WorkspaceSymbol, CancellationToken, Task> _openAsync;
	private readonly WorkspaceSymbolQuickPickOptions _options;
	private readonly Action<Exception>? _reportFault;

	/// <param name="getProviders">Gets the providers to search, in the order their symbols list. It runs on the dispatcher thread.</param>
	/// <param name="openAsync">
	/// Opens the document of a symbol at its range, or at its start when the range stayed unknown. It runs on the dispatcher thread, so it may touch
	/// the UI.
	/// </param>
	/// <param name="options">The options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="reportFault">
	/// Receives each failure of a provider that the mode contained, and must not throw. It runs on the dispatcher thread, so it may touch the UI.
	/// A <see langword="null"/> value drops the failures.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="getProviders"/> or <paramref name="openAsync"/> is <see langword="null"/>.</exception>
	public WorkspaceSymbolQuickPickProvider(
		Func<IReadOnlyList<IWorkspaceSymbolProvider>> getProviders,
		Func<WorkspaceSymbol, CancellationToken, Task> openAsync,
		WorkspaceSymbolQuickPickOptions? options = null,
		Action<Exception>? reportFault = null
	)
	{
		ArgumentNullException.ThrowIfNull(getProviders);
		ArgumentNullException.ThrowIfNull(openAsync);

		_getProviders = getProviders;
		_openAsync = openAsync;
		_options = options ?? WorkspaceSymbolQuickPickOptions.Default;
		_reportFault = reportFault;
	}

	/// <inheritdoc/>
	public string Prefix => "#";

	/// <inheritdoc/>
	public string Name => _options.Name;

	/// <inheritdoc/>
	public string Placeholder => _options.Placeholder;

	/// <inheritdoc/>
	public QuickPickFilterMode FilterMode => QuickPickFilterMode.RankOnly;

	/// <inheritdoc/>
	public string EmptyText => _options.EmptyText;

	/// <inheritdoc/>
	public TimeSpan? QueryDebounceDelay => _options.QueryDebounceDelay;

	/// <inheritdoc/>
	public string GetFilterText(string query)
	{
		ArgumentNullException.ThrowIfNull(query);
		return SymbolQuerySplitter.Split(query).Name;
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		(string name, _) = SymbolQuerySplitter.Split(request.Query);

		if (name.Length == 0)
			return [];

		IReadOnlyList<IWorkspaceSymbolProvider> providers = _getProviders();
		var asks = new Task<Answer>[providers.Count];

		for (int index = 0; index < providers.Count; index++)
			asks[index] = AskAsync(providers[index], name, cancellationToken);

		// The wait resumes on the dispatcher thread, so the failures below reach the host there, one after another and in the order of the providers.
		Answer[] answers = await Task.WhenAll(asks).ConfigureAwait(true);

		foreach (Answer answer in answers)
		{
			if (answer.Fault is not null)
				_reportFault?.Invoke(answer.Fault);
		}

		return CreateItems(providers, answers, new SymbolQueryMatcher(request.Query));
	}

	/// <inheritdoc/>
	public async Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Item.Tag is not WorkspaceSymbolEntry entry)
			return;

		WorkspaceSymbol symbol = entry.Symbol;

		if (symbol.Location.Range is null && entry.Provider is IWorkspaceSymbolResolver resolver)
		{
			try
			{
				// The open below touches the UI, so the wait resumes on the dispatcher thread whatever thread the resolver finishes on.
				symbol = await resolver.ResolveAsync(symbol, cancellationToken).ConfigureAwait(true);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception exception)
			{
				_reportFault?.Invoke(exception);
			}
		}

		await _openAsync(symbol, cancellationToken).ConfigureAwait(true);
	}

	private static async Task<Answer> AskAsync(IWorkspaceSymbolProvider provider, string name, CancellationToken cancellationToken)
	{
		try
		{
			return new Answer(
				await provider.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest(name), cancellationToken).ConfigureAwait(false) ?? [],
				null
			);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			return new Answer([], exception);
		}
	}

	private List<QuickPickItem> CreateItems(IReadOnlyList<IWorkspaceSymbolProvider> providers, Answer[] answers, SymbolQueryMatcher matcher)
	{
		var items = new List<QuickPickItem>();
		var seen = new Dictionary<string, HashSet<string>>(_options.PathComparison.Comparer);

		for (int index = 0; index < answers.Length; index++)
		{
			foreach (WorkspaceSymbol symbol in answers[index].Symbols)
			{
				string description = Describe(symbol);

				if (!matcher.MatchesContainerWords(symbol.Name, description))
					continue;

				// Two providers, or one server that lists a symbol twice, give one row.
				string identity = IdentityOf(symbol);

				if (!seen.TryGetValue(symbol.Location.DocumentId, out HashSet<string>? identities))
					seen[symbol.Location.DocumentId] = identities = new HashSet<string>(StringComparer.Ordinal);

				if (!identities.Add(identity))
					continue;

				items.Add(
					new QuickPickItem($"{symbol.Location.DocumentId}|{identity}", symbol.Name)
					{
						Description = description,
						IconKey = symbol.Kind.ToIconKey(),
						Tag = new WorkspaceSymbolEntry(providers[index], symbol),
					}
				);
			}
		}

		return items;
	}

	private static string IdentityOf(WorkspaceSymbol symbol)
	{
		string start = symbol.Location.Range is { } range ? $"{range.Start.Line}:{range.Start.Character}" : "?";
		return string.Create(CultureInfo.InvariantCulture, $"{(int)symbol.Kind}|{symbol.ContainerName}|{symbol.Name}|{start}");
	}

	private string Describe(WorkspaceSymbol symbol)
	{
		string path = DescribePath(symbol.Location.DocumentId);
		return symbol.ContainerName is { } container ? $"{container}{_options.ContainerSeparator}{path}" : path;
	}

	// The path of the document below its root, written with slashes, or the path as the provider spelled it when it is no local path.
	private string DescribePath(string documentId)
	{
		if (!LocalPath.TryNormalize(documentId, out string normalized))
			return documentId;

		string fileName = Path.GetFileName(normalized);
		string? folder = Path.GetDirectoryName(normalized);

		if (string.IsNullOrEmpty(folder))
			return fileName;

		string? described = LocalPath.DescribeFolder(_options.RootPaths, folder, _options.PathComparison);

		return string.IsNullOrEmpty(described) ? fileName : $"{described}/{fileName}";
	}

	// What one provider answered: its symbols, or the failure that cost them.
	private readonly record struct Answer(IReadOnlyList<WorkspaceSymbol> Symbols, Exception? Fault);
}
