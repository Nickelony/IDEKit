using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class ServerPayloadDocumentStoreTests
{
	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ServerPayloadDocumentStoreTests");

	[TestMethod]
	public void DiagnosticsCache_StoresReadOnlyCopyDetachedFromSourceCollection()
	{
		string filePath = Script("diagnostics.ext");

		var store = new ServerPayloadDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);

		DocumentSnapshot? trackedDocument = store.GetDocumentSnapshot(filePath);
		Assert.IsNotNull(trackedDocument);

		var originalDiagnostic = new TextDiagnostic(TextDiagnosticSeverity.Warning, "Original", 0, 1);
		var replacementDiagnostic = new TextDiagnostic(TextDiagnosticSeverity.Warning, "Replacement", 1, 2);
		DiagnosticEntry[] sourceEntries = [new(originalDiagnostic, EmptyDiagnosticPayload())];

		Assert.IsTrue(store.TryStoreDiagnostics(
			new PublishedDiagnostics(filePath, sourceEntries, version: trackedDocument.Version),

			expectedDocumentVersion: trackedDocument.Version,
			sourceContent: "return 1"));

		sourceEntries[0] = new(replacementDiagnostic, EmptyDiagnosticPayload());

		IReadOnlyList<TextDiagnostic> storedDiagnostics = store.GetDiagnostics(filePath);

		Assert.AreEqual(1, storedDiagnostics.Count);
		Assert.AreSame(originalDiagnostic, storedDiagnostics[0]);
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<TextDiagnostic>)storedDiagnostics)[0] = replacementDiagnostic);

		(IReadOnlyList<DiagnosticEntry> snapshotEntries, string? snapshotContent) = store.GetDiagnosticsSnapshot(filePath);

		Assert.AreSame(originalDiagnostic, snapshotEntries[0].Diagnostic);
		Assert.AreEqual("return 1", snapshotContent);
	}

	[TestMethod]
	public void DiagnosticsCache_DoesNotStorePayloadWhenTrackedDocumentVersionAdvanced()
	{
		string filePath = Script("diagnostics.ext");

		var store = new ServerPayloadDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);

		DocumentSnapshot? staleDocument = store.GetDocumentSnapshot(filePath);

		store.Synchronize(filePath, "return 2");

		Assert.IsNotNull(staleDocument);

		bool stored = store.TryStoreDiagnostics(
			new PublishedDiagnostics(
				filePath,
				[CreateDiagnosticEntry("Stale")],
				version: staleDocument.Version),
			expectedDocumentVersion: staleDocument.Version,
			sourceContent: "return 2");

		Assert.IsFalse(stored);
		Assert.AreEqual(0, store.GetDiagnostics(filePath).Count);
	}

	[TestMethod]
	public void DiagnosticsCache_UnversionedPayloadDoesNotReplaceVersionedContent()
	{
		string filePath = Script("diagnostics.ext");

		var store = new ServerPayloadDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);
		store.Synchronize(filePath, "return 2");

		DocumentSnapshot? document = store.GetDocumentSnapshot(filePath);
		Assert.IsNotNull(document);
		Assert.AreEqual(2, document.Version);

		Assert.IsTrue(store.TryStoreDiagnostics(
			new PublishedDiagnostics(
				filePath,
				[CreateDiagnosticEntry("Versioned")],
				version: document.Version),
			expectedDocumentVersion: document.Version,
			sourceContent: "return 2"));

		// A payload without a server version cannot be shown to be newer than the versioned payload already
		// cached, so it is rejected instead of replacing it without advancing the cached version.
		Assert.IsFalse(store.TryStoreDiagnostics(
			new PublishedDiagnostics(
				filePath,
				[CreateDiagnosticEntry("Unversioned")],
				version: 0),
			expectedDocumentVersion: document.Version,
			sourceContent: "return 2"));

		Assert.AreEqual("Versioned", store.GetDiagnostics(filePath)[0].Message);

		// The versioned payload's stamp is preserved, so a payload for version 1 is still stale and rejected.
		Assert.IsFalse(store.TryStoreDiagnostics(
			new PublishedDiagnostics(
				filePath,
				[CreateDiagnosticEntry("Older")],
				version: 1),
			expectedDocumentVersion: document.Version,
			sourceContent: "return 2"));

		Assert.AreEqual("Versioned", store.GetDiagnostics(filePath)[0].Message);
	}

	[TestMethod]
	public void SemanticTokensCache_ClonesStoredCollections()
	{
		string filePath = Script("semantic.ext");

		var store = new ServerPayloadDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);

		var originalToken = new SemanticToken(0, 0, 6, "variable", []);
		var replacementToken = new SemanticToken(1, 0, 6, "function", []);
		SemanticToken[] sourceTokens = [originalToken];

		Assert.IsTrue(store.TryStoreSemanticTokens(filePath, version: 1, sourceTokens));

		sourceTokens[0] = replacementToken;

		IReadOnlyList<SemanticToken> storedTokens = store.GetSemanticTokens(filePath);

		Assert.AreEqual(1, storedTokens.Count);
		Assert.AreSame(originalToken, storedTokens[0]);
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<SemanticToken>)storedTokens)[0] = replacementToken);
	}

	[TestMethod]
	public void SemanticTokensCache_DoesNotStoreTokensWhenTrackedDocumentVersionAdvanced()
	{
		string filePath = Script("semantic.ext");

		var store = new ServerPayloadDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);

		DocumentSnapshot? staleDocument = store.GetDocumentSnapshot(filePath);

		store.Synchronize(filePath, "return 2");

		Assert.IsNotNull(staleDocument);

		bool stored = store.TryStoreSemanticTokens(
			filePath,
			version: staleDocument.Version,
			[new SemanticToken(0, 0, 6, "variable", [])]);

		Assert.IsFalse(stored);
		Assert.AreEqual(0, store.GetSemanticTokens(filePath).Count);
	}

	[TestMethod]
	public void InvalidateServerSynchronization_MarksTheDocumentClosedAndReopensWithANewVersion()
	{
		string filePath = Script("invalidate.ext");

		var store = new ServerPayloadDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);
		store.Synchronize(filePath, "return 2");

		Assert.IsTrue(store.TryStoreSemanticTokens(filePath, version: 2, [new SemanticToken(0, 0, 6, "variable", [])]));

		Assert.IsTrue(store.InvalidateServerSynchronization(filePath));

		// An invalidated document reopens through the next synchronization instead of reporting a
		// change against a server copy that may never have received the last edit; the reopened version
		// advances, and the invalidated token cache accepts the new payload instead of fencing it out
		// against the pre-failure stamp.
		DocumentSynchronizationRequest? reopenRequest = store.Synchronize(filePath, "return 2", references: DocumentReferenceAcquisition.Open);

		Assert.IsNotNull(reopenRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, reopenRequest.Value.Kind);
		Assert.AreEqual(3, reopenRequest.Value.Document.Version);

		Assert.IsTrue(store.TryStoreSemanticTokens(filePath, version: reopenRequest.Value.Document.Version, [new SemanticToken(0, 0, 6, "function", [])]));
		Assert.AreEqual("function", store.GetSemanticTokens(filePath)[0].Type);

		Assert.IsFalse(store.InvalidateServerSynchronization(Script("unknown.ext")));
	}

	[TestMethod]
	public void CachedReads_NormalizeTheSuppliedPath()
	{
		string filePath = Script("diagnostics.ext");

		var store = new ServerPayloadDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);

		DocumentSnapshot? document = store.GetDocumentSnapshot(filePath);
		Assert.IsNotNull(document);

		Assert.IsTrue(store.TryStoreDiagnostics(
			new PublishedDiagnostics(
				filePath,
				[CreateDiagnosticEntry("Cached")],
				version: document.Version),
			expectedDocumentVersion: document.Version,
			sourceContent: "return 1"));

		// A raw path that normalizes to the tracked path reads the same cached payload.
		Assert.AreEqual(1, store.GetDiagnostics(Path.Combine(s_workspaceRoot, "Scripts", "..", "Scripts", "diagnostics.ext")).Count);
	}

	[TestMethod]
	public void GetDiagnosticsSnapshot_UntrackedPath_ReturnsTheEmptyFallback()
	{
		var store = new ServerPayloadDocumentStore();

		(IReadOnlyList<DiagnosticEntry> diagnostics, string? sourceContent) = store.GetDiagnosticsSnapshot(Script("unknown.ext"));

		Assert.AreEqual(0, diagnostics.Count);
		Assert.IsNull(sourceContent);
	}

	[TestMethod]
	public void InvalidateServerSynchronization_ResetsTheDiagnosticsStampToo()
	{
		string filePath = Script("invalidate-diagnostics.ext");

		var store = new ServerPayloadDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);
		store.Synchronize(filePath, "return 2");

		Assert.IsTrue(store.TryStoreDiagnostics(
			new PublishedDiagnostics(
				filePath,
				[CreateDiagnosticEntry("Newest")],
				version: 2),
			expectedDocumentVersion: 2,
			sourceContent: "return 2"));

		Assert.IsTrue(store.InvalidateServerSynchronization(filePath));

		// The invalidation resets both payload stamps, so the next diagnostics payload is accepted
		// even when it reports a version lower than the pre-failure stamp; the store-level expectation
		// is unknown here, which leaves the cache fence as the deciding guard.
		Assert.IsTrue(store.TryStoreDiagnostics(
			new PublishedDiagnostics(
				filePath,
				[CreateDiagnosticEntry("Reopened")],
				version: 1),
			expectedDocumentVersion: 0,
			sourceContent: "return 2"));

		Assert.AreEqual("Reopened", store.GetDiagnostics(filePath)[0].Message);
	}

	[TestMethod]
	public void DiagnosticsCache_ServesThePublishedProjectionInsteadOfReprojecting()
	{
		string filePath = Script("diagnostics.ext");

		var store = new ServerPayloadDocumentStore();
		store.Synchronize(filePath, "return 1", references: DocumentReferenceAcquisition.Open);

		DocumentSnapshot? trackedDocument = store.GetDocumentSnapshot(filePath);
		Assert.IsNotNull(trackedDocument);

		var published = new PublishedDiagnostics(
			filePath,
			[CreateDiagnosticEntry("Shared")],
			version: trackedDocument.Version);

		Assert.IsTrue(store.TryStoreDiagnostics(published, expectedDocumentVersion: trackedDocument.Version, sourceContent: "return 1"));

		// The published payload and the cached read share one projection rather than each projecting the same entries.
		Assert.AreSame(published.Diagnostics, store.GetDiagnostics(filePath));
	}

	/// <summary>
	/// Returns a synthetic absolute path below the store's test root; nothing here touches the file system.
	/// </summary>
	/// <param name="fileName">The file name to place inside the synthetic <c>Scripts</c> folder.</param>
	/// <returns>An absolute path below the store's test root.</returns>
	private static string Script(string fileName) => Path.Combine(s_workspaceRoot, "Scripts", fileName);

	private static DiagnosticEntry CreateDiagnosticEntry(string message)
		=> new(new TextDiagnostic(TextDiagnosticSeverity.Warning, message, 0, 1), EmptyDiagnosticPayload());

	private static DiagnosticPayload EmptyDiagnosticPayload() => new(null, null, null, null, null);
}
