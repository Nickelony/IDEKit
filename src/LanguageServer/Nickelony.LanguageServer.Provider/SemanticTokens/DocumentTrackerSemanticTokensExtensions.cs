namespace Nickelony.LanguageServer.Provider;

/// <summary>Reads and stores the semantic tokens a server published for the documents of a <see cref="DocumentTracker"/>.</summary>
internal static class DocumentTrackerSemanticTokensExtensions
{
	private static readonly PayloadSlot<VersionFencedCache<SemanticToken>> s_slot = new();

	/// <summary>Returns the cached semantic tokens for the specified file path.</summary>
	/// <param name="documents">The tracker of the documents.</param>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>The cached semantic tokens, or an empty list when none are stored.</returns>
	internal static IReadOnlyList<SemanticToken> GetSemanticTokens(this DocumentTracker documents, string filePath)
	{
		return documents.ReadPayload<VersionFencedCache<SemanticToken>, IReadOnlyList<SemanticToken>>(
			filePath,
			slot: s_slot,
			read: static cache => cache.Items,
			fallbackValue: []
		);
	}

	/// <summary>Stores semantic tokens when they are not stale for the tracked document version.</summary>
	/// <param name="documents">The tracker of the documents.</param>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="version">The document version associated with the tokens.</param>
	/// <param name="semanticTokens">The semantic tokens to cache.</param>
	/// <returns><see langword="true"/> when the token set was stored; otherwise, <see langword="false"/>.</returns>
	internal static bool TryStoreSemanticTokens(
		this DocumentTracker documents,
		string filePath,
		int version,
		IReadOnlyList<SemanticToken> semanticTokens
	) => documents.TryStorePayload(filePath, slot: s_slot, expectedDocumentVersion: version, store: cache => cache.TryStore(version, semanticTokens));
}
