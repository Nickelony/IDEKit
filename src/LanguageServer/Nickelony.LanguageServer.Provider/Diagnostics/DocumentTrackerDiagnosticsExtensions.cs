using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Reads and stores the diagnostics a server published for the documents of a <see cref="DocumentTracker"/>.</summary>
internal static class DocumentTrackerDiagnosticsExtensions
{
	private static readonly PayloadSlot<DiagnosticsPayloadCache> s_slot = new();

	/// <summary>Returns the cached diagnostics for the specified file path.</summary>
	/// <param name="documents">The tracker of the documents.</param>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>The cached diagnostics, ordered by start offset and then by severity, or an empty list when none are stored.</returns>
	internal static IReadOnlyList<Diagnostic> GetDiagnostics(this DocumentTracker documents, string filePath)
	{
		return documents.ReadPayload<DiagnosticsPayloadCache, IReadOnlyList<Diagnostic>>(
			filePath,
			slot: s_slot,
			read: static cache => cache.GetProjection(),
			fallbackValue: []
		);
	}

	/// <summary>Returns the cached diagnostics together with the content snapshot their offsets refer to.</summary>
	/// <param name="documents">The tracker of the documents.</param>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>
	/// The cached diagnostic entries and the content snapshot the payload was parsed against; an empty list and <see langword="null"/>
	/// when nothing is stored. Consumers that convert the offsets back to positions must use the returned snapshot, because it can differ
	/// from the caller's current document text.
	/// </returns>
	internal static (IReadOnlyList<DiagnosticEntry> Entries, string? SourceContent) GetDiagnosticsWithSourceContent(
		this DocumentTracker documents,
		string filePath
	)
	{
		return documents.ReadPayload<DiagnosticsPayloadCache, (IReadOnlyList<DiagnosticEntry> Entries, string? SourceContent)>(
			filePath,
			slot: s_slot,
			read: static cache => (cache.Entries.Items, cache.Entries.SourceContent),
			fallbackValue: ([], null)
		);
	}

	/// <summary>Stores a diagnostics payload when it is not stale for the tracked document version.</summary>
	/// <param name="documents">The tracker of the documents.</param>
	/// <param name="publishedDiagnostics">The diagnostics payload to cache.</param>
	/// <param name="expectedDocumentVersion">The tracked document version observed when the payload was parsed.</param>
	/// <param name="sourceContent">The content snapshot the diagnostic offsets were resolved against.</param>
	/// <returns><see langword="true"/> when the payload was stored; otherwise, <see langword="false"/>.</returns>
	internal static bool TryStoreDiagnostics(
		this DocumentTracker documents,
		ParsedDiagnostics publishedDiagnostics,
		int expectedDocumentVersion,
		string sourceContent
	)
	{
		return documents.TryStorePayload(
			filePath: publishedDiagnostics.FilePath,
			slot: s_slot,
			expectedDocumentVersion,

			store: cache =>
			{
				if (!cache.Entries.TryStore(publishedDiagnostics.Version, publishedDiagnostics.Entries, sourceContent))
					return false;

				// Adopt the projection the payload already computed so the cached read and the published payload share
				// one owned snapshot instead of projecting the same entries twice.
				cache.AdoptProjection(publishedDiagnostics.Diagnostics);
				return true;
			}
		);
	}
}
