using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.SemanticTokens;

/// <summary>Subscribes an editor of one document to the semantic tokens a source pushes.</summary>
public static class SemanticTokensSourceExtensions
{
	/// <summary>Delivers the current semantic tokens of a document, then each change of them, to a callback on the UI thread.</summary>
	/// <param name="source">The source that pushes the semantic tokens of every document it knows.</param>
	/// <param name="documentId">The document whose tokens reach the callback.</param>
	/// <param name="dispatcher">The dispatcher of the thread that runs the callback.</param>
	/// <param name="onChanged">Receives each list of tokens, the first time with the tokens the source holds when the call returns.</param>
	/// <param name="documentIdComparer">
	/// Compares the document ids of the source's updates with <paramref name="documentId"/>, or <see langword="null"/> for an ordinal comparison.
	/// </param>
	/// <returns>The subscription, which stops the deliveries when it is disposed on any thread.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="source"/>, <paramref name="documentId"/>, <paramref name="dispatcher"/> or <paramref name="onChanged"/>
	/// is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// The first delivery lets a second editor of an open document color the tokens the source already published. A language-server provider
	/// names each document by the id the host opened it with, so the ordinal default matches its updates. A source whose current read throws
	/// keeps no handler, and the exception reaches the caller.
	/// </remarks>
	public static IDisposable Subscribe(
		this ISemanticTokensSource source,
		string documentId,
		IUiDispatcher dispatcher,
		Action<IReadOnlyList<SemanticToken>> onChanged,
		IEqualityComparer<string>? documentIdComparer = null
	)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(documentId);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(onChanged);

		var subscription = new DocumentUpdateSubscription<SemanticToken>(
			documentId,
			documentIdComparer: documentIdComparer ?? StringComparer.Ordinal,
			dispatcher,
			onChanged
		);

		void handler(object? _, SemanticTokensUpdatedEventArgs e) => subscription.HandleUpdated(e.DocumentId, e.SemanticTokens);

		source.SemanticTokensUpdated += handler;

		try
		{
			subscription.Start(() => source.SemanticTokensUpdated -= handler, source.GetSemanticTokens(documentId));
		}
		catch
		{
			// A source that fails to read its current tokens leaves no handler behind for a subscription the caller never received.
			source.SemanticTokensUpdated -= handler;
			throw;
		}

		return subscription;
	}
}
