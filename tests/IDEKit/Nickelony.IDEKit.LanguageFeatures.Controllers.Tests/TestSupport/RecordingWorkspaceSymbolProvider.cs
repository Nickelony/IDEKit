using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Answers every query with one list of workspace symbols and resolves a symbol with a configured answer, and records both calls.</summary>
/// <param name="symbols">The symbols to answer every query with.</param>
/// <remarks>
/// A test that assigns <see cref="Gate"/> holds every answer until it releases the gate or the request token is canceled. A test that assigns
/// <see cref="Failure"/> or <see cref="ResolveFailure"/> makes the matching call throw.
/// </remarks>
internal sealed class RecordingWorkspaceSymbolProvider(params WorkspaceSymbol[] symbols) : IWorkspaceSymbolProvider, IWorkspaceSymbolResolver
{
	private readonly object _sync = new();
	private readonly List<(string Query, CancellationToken Token)> _queries = [];
	private readonly List<WorkspaceSymbol> _resolved = [];

	/// <summary>The symbols the provider answers every query with.</summary>
	public IReadOnlyList<WorkspaceSymbol> Symbols { get; set; } = symbols;

	/// <summary>When set, every answer waits for the gate.</summary>
	public TaskCompletionSource<bool>? Gate { get; set; }

	/// <summary>When set, every query throws it.</summary>
	public Exception? Failure { get; set; }

	/// <summary>
	/// When set, the query and the resolution answer after this delay, on a pool thread, as a provider that waits for a server does.
	/// </summary>
	public TimeSpan? Delay { get; set; }

	/// <summary>The symbol <see cref="ResolveAsync"/> answers with, or <see langword="null"/> to answer the symbol it received.</summary>
	public WorkspaceSymbol? ResolvedSymbol { get; set; }

	/// <summary>When set, every resolution throws it.</summary>
	public Exception? ResolveFailure { get; set; }

	/// <summary>The queries in the order they arrived.</summary>
	public IReadOnlyList<string> Queries
	{
		get
		{
			lock (_sync)
				return [.. _queries.Select(query => query.Query)];
		}
	}

	/// <summary>The tokens of the queries in the order they arrived.</summary>
	public IReadOnlyList<CancellationToken> Tokens
	{
		get
		{
			lock (_sync)
				return [.. _queries.Select(query => query.Token)];
		}
	}

	/// <summary>The symbols that were handed to <see cref="ResolveAsync"/>, in order.</summary>
	public IReadOnlyList<WorkspaceSymbol> ResolveCalls
	{
		get
		{
			lock (_sync)
				return [.. _resolved];
		}
	}

	public async Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(
		WorkspaceSymbolRequest request,
		CancellationToken cancellationToken = default
	)
	{
		lock (_sync)
			_queries.Add((request.Query, cancellationToken));

		if (Gate is { } gate)
			await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

		if (Delay is { } delay)
			await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

		if (Failure is { } failure)
			throw failure;

		return Symbols;
	}

	public async Task<WorkspaceSymbol> ResolveAsync(WorkspaceSymbol symbol, CancellationToken cancellationToken = default)
	{
		lock (_sync)
			_resolved.Add(symbol);

		if (Delay is { } delay)
			await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

		if (ResolveFailure is { } failure)
			throw failure;

		return ResolvedSymbol ?? symbol;
	}
}
