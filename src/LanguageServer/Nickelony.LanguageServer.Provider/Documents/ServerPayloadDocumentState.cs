using Nickelony.IDEKit.IntelliSense.Diagnostics;
using System.Collections.ObjectModel;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Extends the core tracked-document state with the version-fenced caches for the two payloads a language server
/// sends for a tracked document: diagnostics and semantic tokens.
/// </summary>
/// <remarks>
/// <para>
/// The type is internal, so a provider package reaches it through the framework's <c>InternalsVisibleTo</c> grant,
/// and the framework's <see cref="TrackedDocumentStore"/> stays non-generic (a provider's concrete store subclasses
/// it with this state type and casts internally).
/// </para>
/// <para>
/// The state-mutating members below forward to the protected <see cref="TrackedDocumentState"/> operations because
/// the store casts the tracked state to this type and cannot call the protected members directly.
/// </para>
/// </remarks>
internal sealed class ServerPayloadDocumentState : TrackedDocumentState
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ServerPayloadDocumentState"/> class.
	/// </summary>
	/// <param name="initialState">The initial tracked-document state.</param>
	internal ServerPayloadDocumentState(TrackedDocumentInitialState initialState)
		: base(initialState)
	{ }

	/// <summary>Updates the access stamp used for idle-document eviction ordering.</summary>
	internal void Touch(long lastAccessStamp)
		=> SetLastAccessStamp(lastAccessStamp);

	/// <summary>Marks the document as reopened with fresh synchronized content.</summary>
	internal void Reopen(string content)
		=> ReopenDocument(content);

	/// <summary>
	/// Replaces the tracked content and advances the version, returning the previous content.
	/// </summary>
	internal string UpdateContent(string content)
		=> ReplaceContent(content);

	/// <summary>Replaces the tracked file path and URI after a rename.</summary>
	internal void RenameTo(string filePath, string uri)
		=> RenameDocument(filePath, uri);

	/// <summary>Marks the tracked document as closed locally while preserving cached state.</summary>
	internal void MarkClosed()
		=> MarkDocumentClosed();

	/// <summary>
	/// Gets the version-fenced diagnostics cache for the tracked document, including the content snapshot
	/// the diagnostic offsets refer to and each diagnostic's raw protocol code element.
	/// </summary>
	internal VersionFencedPayloadCache<DiagnosticEntry> DiagnosticsCache { get; } = new();

	/// <summary>
	/// Gets the version-fenced semantic-token cache for the tracked document.
	/// </summary>
	internal VersionFencedPayloadCache<SemanticToken> SemanticTokensCache { get; } = new();

	// Cached shared-diagnostic projection of DiagnosticsCache.Items. The projection only changes when the cache
	// stores a new payload (TryStore always replaces Items with a fresh snapshot array) or when a store adopts the
	// payload's own projection, so the source reference is a reliable invalidation key. Every read and store runs
	// under the document store lock, so no additional synchronization is needed.
	private IReadOnlyList<DiagnosticEntry>? _projectedDiagnosticsSource;
	private ReadOnlyCollection<TextDiagnostic>? _diagnosticsProjection;

	/// <summary>
	/// Gets the shared-diagnostic projection of the cached diagnostics, reusing the previous projection while the
	/// cached payload is unchanged.
	/// </summary>
	/// <returns>The cached diagnostics in entry order, backed by a read-only collection.</returns>
	internal ReadOnlyCollection<TextDiagnostic> GetDiagnosticsProjection()
	{
		IReadOnlyList<DiagnosticEntry> items = DiagnosticsCache.Items;

		if (!ReferenceEquals(_projectedDiagnosticsSource, items) || _diagnosticsProjection is null)
		{
			_diagnosticsProjection = DiagnosticEntry.ProjectDiagnostics(items);
			_projectedDiagnosticsSource = items;
		}

		return _diagnosticsProjection;
	}

	/// <summary>
	/// Adopts a projection a caller already computed for the diagnostics it just stored, so the cached read serves
	/// that same owned snapshot instead of projecting the stored entries a second time.
	/// </summary>
	/// <param name="projection">The projection of the entries that are now cached.</param>
	/// <remarks>
	/// The caller stores the same entries the projection was built from and
	/// <see cref="VersionFencedPayloadCache{TItem}"/> snapshots them shallowly, so the adopted projection is
	/// order- and instance-equivalent to a projection of the cached entries.
	/// </remarks>
	internal void AdoptDiagnosticsProjection(ReadOnlyCollection<TextDiagnostic> projection)
	{
		_diagnosticsProjection = projection;
		_projectedDiagnosticsSource = DiagnosticsCache.Items;
	}
}
