using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Answers every outline request with one list of symbols, and records each request with the token it carried.</summary>
/// <param name="symbols">The outline to answer with.</param>
/// <remarks>
/// A test that assigns <see cref="Gate"/> holds every answer until it releases the gate or the request token is canceled.
/// </remarks>
internal sealed class RecordingDocumentSymbolProvider(params DocumentSymbol[] symbols) : IDocumentSymbolProvider
{
	private readonly object _sync = new();
	private readonly List<(DocumentSymbolRequest Request, CancellationToken Token)> _calls = [];

	/// <summary>The outline the provider answers with.</summary>
	public IReadOnlyList<DocumentSymbol> Symbols { get; set; } = symbols;

	/// <summary>When set, every answer waits for the gate.</summary>
	public TaskCompletionSource<bool>? Gate { get; set; }

	/// <summary>When set, every request throws the exception it creates, after it yielded to a pool thread.</summary>
	public Func<Exception>? Failure { get; set; }

	/// <summary>The number of outline requests the provider received.</summary>
	public int CallCount
	{
		get
		{
			lock (_sync)
				return _calls.Count;
		}
	}

	/// <summary>The requests in the order they arrived.</summary>
	public IReadOnlyList<DocumentSymbolRequest> Requests
	{
		get
		{
			lock (_sync)
				return [.. _calls.Select(call => call.Request)];
		}
	}

	/// <summary>The tokens of the requests in the order they arrived.</summary>
	public IReadOnlyList<CancellationToken> Tokens
	{
		get
		{
			lock (_sync)
				return [.. _calls.Select(call => call.Token)];
		}
	}

	public async Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(
		DocumentSymbolRequest request,
		CancellationToken cancellationToken = default
	)
	{
		lock (_sync)
			_calls.Add((request, cancellationToken));

		if (Gate is { } gate)
			await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

		if (Failure is { } failure)
		{
			await Task.Delay(5, cancellationToken).ConfigureAwait(false);
			throw failure();
		}

		return Symbols;
	}
}
