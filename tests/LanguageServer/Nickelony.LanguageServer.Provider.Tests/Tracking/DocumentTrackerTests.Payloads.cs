using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.LanguageServer.Provider.Tests;

public sealed partial class DocumentTrackerTests
{
	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "DocumentTrackerTests");

	[TestMethod]
	public void DiagnosticsCache_StoresReadOnlyCopyDetachedFromSourceCollection()
	{
		string filePath = Script("diagnostics.ext");

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);

		DocumentSnapshot? trackedDocument = tracker.GetDocumentSnapshot(filePath);
		Assert.IsNotNull(trackedDocument);

		var originalDiagnostic = new Diagnostic(severity: DiagnosticSeverity.Warning, message: "Original", range: TextRange.FromBounds(0, 1));

		var replacementDiagnostic = new Diagnostic(severity: DiagnosticSeverity.Warning, message: "Replacement", range: TextRange.FromBounds(1, 2));

		DiagnosticEntry[] sourceEntries = [new(originalDiagnostic, EmptyDiagnosticPayload())];

		Assert.IsTrue(
			tracker.TryStoreDiagnostics(
				new ParsedDiagnostics(filePath, sourceEntries, version: trackedDocument.Version),
				expectedDocumentVersion: trackedDocument.Version,
				sourceContent: "return 1"
			)
		);

		sourceEntries[0] = new(replacementDiagnostic, EmptyDiagnosticPayload());

		IReadOnlyList<Diagnostic> storedDiagnostics = tracker.GetDiagnostics(filePath);

		Assert.HasCount(1, storedDiagnostics);
		Assert.AreSame(originalDiagnostic, storedDiagnostics[0]);
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<Diagnostic>)storedDiagnostics)[0] = replacementDiagnostic);

		(IReadOnlyList<DiagnosticEntry> snapshotEntries, string? snapshotContent) = tracker.GetDiagnosticsWithSourceContent(filePath);

		Assert.AreSame(originalDiagnostic, snapshotEntries[0].Diagnostic);
		Assert.AreEqual("return 1", snapshotContent);
	}

	[TestMethod]
	public void DiagnosticsCache_DoesNotStorePayloadWhenTrackedDocumentVersionAdvanced()
	{
		string filePath = Script("diagnostics.ext");

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);

		DocumentSnapshot? staleDocument = tracker.GetDocumentSnapshot(filePath);

		tracker.Synchronize(filePath, "return 2");

		Assert.IsNotNull(staleDocument);

		bool stored = tracker.TryStoreDiagnostics(
			new ParsedDiagnostics(filePath, [CreateDiagnosticEntry("Stale")], version: staleDocument.Version),
			expectedDocumentVersion: staleDocument.Version,
			sourceContent: "return 2"
		);

		Assert.IsFalse(stored);
		Assert.IsEmpty(tracker.GetDiagnostics(filePath));
	}

	[TestMethod]
	public void DiagnosticsCache_UnversionedPayloadDoesNotReplaceVersionedContent()
	{
		string filePath = Script("diagnostics.ext");

		var tracker = new DocumentTracker();

		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);
		tracker.Synchronize(filePath, "return 2");

		DocumentSnapshot? document = tracker.GetDocumentSnapshot(filePath);

		Assert.IsNotNull(document);
		Assert.AreEqual(2, document.Version);

		Assert.IsTrue(
			tracker.TryStoreDiagnostics(
				new ParsedDiagnostics(filePath, [CreateDiagnosticEntry("Versioned")], version: document.Version),
				expectedDocumentVersion: document.Version,
				sourceContent: "return 2"
			)
		);

		// A payload without a server version cannot be shown to be newer than the versioned payload already
		// cached, so it is rejected instead of replacing it without advancing the cached version.
		Assert.IsFalse(
			tracker.TryStoreDiagnostics(
				new ParsedDiagnostics(filePath, [CreateDiagnosticEntry("Unversioned")], version: 0),
				expectedDocumentVersion: document.Version,
				sourceContent: "return 2"
			)
		);

		Assert.AreEqual("Versioned", tracker.GetDiagnostics(filePath)[0].Message);

		// The versioned payload's stamp is preserved, so a payload for version 1 is still stale and rejected.
		Assert.IsFalse(
			tracker.TryStoreDiagnostics(
				new ParsedDiagnostics(filePath, [CreateDiagnosticEntry("Older")], version: 1),
				expectedDocumentVersion: document.Version,
				sourceContent: "return 2"
			)
		);

		Assert.AreEqual("Versioned", tracker.GetDiagnostics(filePath)[0].Message);
	}

	[TestMethod]
	public void SemanticTokensCache_ClonesStoredCollections()
	{
		string filePath = Script("semantic.ext");

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);

		var originalToken = new SemanticToken(new TextRange(0, 6), "variable");
		var replacementToken = new SemanticToken(new TextRange(7, 6), "function");

		SemanticToken[] sourceTokens = [originalToken];

		Assert.IsTrue(tracker.TryStoreSemanticTokens(filePath, version: 1, sourceTokens));

		sourceTokens[0] = replacementToken;

		IReadOnlyList<SemanticToken> storedTokens = tracker.GetSemanticTokens(filePath);

		Assert.HasCount(1, storedTokens);
		Assert.AreSame(originalToken, storedTokens[0]);
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<SemanticToken>)storedTokens)[0] = replacementToken);
	}

	[TestMethod]
	public void SemanticTokensCache_DoesNotStoreTokensWhenTrackedDocumentVersionAdvanced()
	{
		string filePath = Script("semantic.ext");

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);

		DocumentSnapshot? staleDocument = tracker.GetDocumentSnapshot(filePath);

		tracker.Synchronize(filePath, "return 2");

		Assert.IsNotNull(staleDocument);

		bool stored = tracker.TryStoreSemanticTokens(filePath, version: staleDocument.Version, [new SemanticToken(new TextRange(0, 6), "variable")]);

		Assert.IsFalse(stored);
		Assert.IsEmpty(tracker.GetSemanticTokens(filePath));
	}

	[TestMethod]
	public void InvalidateServerSynchronization_MarksTheDocumentClosedAndReopensWithANewVersion()
	{
		string filePath = Script("invalidate.ext");

		var tracker = new DocumentTracker();

		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);
		tracker.Synchronize(filePath, "return 2");

		Assert.IsTrue(tracker.TryStoreSemanticTokens(filePath, version: 2, [new SemanticToken(new TextRange(0, 6), "variable")]));

		Assert.IsTrue(tracker.InvalidateServerSynchronization(filePath));

		// An invalidated document reopens through the next synchronization instead of reporting a
		// change against a server copy that may never have received the last edit; the reopened version
		// advances, and the invalidated token cache accepts the new payload instead of fencing it out
		// against the pre-failure stamp.
		DocumentSynchronizationDirective? reopenRequest = tracker.Synchronize(filePath, "return 2", leases: DocumentLeaseKinds.Open);

		Assert.IsNotNull(reopenRequest);
		Assert.AreEqual(DocumentSynchronizationKind.Open, reopenRequest.Value.Kind);
		Assert.AreEqual(3, reopenRequest.Value.Document.Version);

		Assert.IsTrue(
			tracker.TryStoreSemanticTokens(
				filePath,
				version: reopenRequest.Value.Document.Version,
				[new SemanticToken(new TextRange(0, 6), "function")]
			)
		);

		Assert.AreEqual("function", tracker.GetSemanticTokens(filePath)[0].Type);

		Assert.IsFalse(tracker.InvalidateServerSynchronization(Script("unknown.ext")));
	}

	[TestMethod]
	public void CachedReads_NormalizeTheSuppliedPath()
	{
		string filePath = Script("diagnostics.ext");

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);

		DocumentSnapshot? document = tracker.GetDocumentSnapshot(filePath);
		Assert.IsNotNull(document);

		Assert.IsTrue(
			tracker.TryStoreDiagnostics(
				new ParsedDiagnostics(filePath, [CreateDiagnosticEntry("Cached")], version: document.Version),
				expectedDocumentVersion: document.Version,
				sourceContent: "return 1"
			)
		);

		// A raw path that normalizes to the tracked path reads the same cached payload.
		Assert.HasCount(1, tracker.GetDiagnostics(Path.Combine(s_workspaceRoot, "Scripts", "..", "Scripts", "diagnostics.ext")));
	}

	[TestMethod]
	public void GetDiagnosticsWithSourceContent_UntrackedPath_ReturnsTheEmptyFallback()
	{
		var tracker = new DocumentTracker();

		(IReadOnlyList<DiagnosticEntry> diagnostics, string? sourceContent) = tracker.GetDiagnosticsWithSourceContent(Script("unknown.ext"));

		Assert.IsEmpty(diagnostics);
		Assert.IsNull(sourceContent);
	}

	[TestMethod]
	public void InvalidateServerSynchronization_ResetsTheDiagnosticsStampToo()
	{
		string filePath = Script("invalidate-diagnostics.ext");

		var tracker = new DocumentTracker();

		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);
		tracker.Synchronize(filePath, "return 2");

		Assert.IsTrue(
			tracker.TryStoreDiagnostics(
				new ParsedDiagnostics(filePath, [CreateDiagnosticEntry("Newest")], version: 2),
				expectedDocumentVersion: 2,
				sourceContent: "return 2"
			)
		);

		Assert.IsTrue(tracker.InvalidateServerSynchronization(filePath));

		// The invalidation resets both payload stamps, so the next diagnostics payload is accepted
		// even when it reports a version lower than the pre-failure stamp; the tracker-level expectation
		// is unknown here, which leaves the cache fence as the deciding guard.
		Assert.IsTrue(
			tracker.TryStoreDiagnostics(
				new ParsedDiagnostics(filePath, [CreateDiagnosticEntry("Reopened")], version: 1),
				expectedDocumentVersion: 0,
				sourceContent: "return 2"
			)
		);

		Assert.AreEqual("Reopened", tracker.GetDiagnostics(filePath)[0].Message);
	}

	[TestMethod]
	public void InvalidateServerSynchronization_ResetsTheTokenStampToo()
	{
		string filePath = Script("invalidate-tokens.ext");

		var tracker = new DocumentTracker();

		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);
		tracker.Synchronize(filePath, "return 2");

		Assert.IsTrue(tracker.TryStoreSemanticTokens(filePath, version: 2, [new SemanticToken(new TextRange(0, 6), "variable")]));
		Assert.IsTrue(tracker.InvalidateServerSynchronization(filePath));

		// The invalidation drops the version stamp, so a payload without a version is accepted again; a kept stamp would fence it out.
		Assert.IsTrue(tracker.TryStoreSemanticTokens(filePath, version: 0, [new SemanticToken(new TextRange(0, 6), "function")]));
		Assert.AreEqual("function", tracker.GetSemanticTokens(filePath)[0].Type);
	}

	[TestMethod]
	public void DiagnosticsCache_ServesThePublishedProjectionInsteadOfReprojecting()
	{
		string filePath = Script("diagnostics.ext");

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);

		DocumentSnapshot? trackedDocument = tracker.GetDocumentSnapshot(filePath);
		Assert.IsNotNull(trackedDocument);

		var published = new ParsedDiagnostics(filePath, [CreateDiagnosticEntry("Shared")], version: trackedDocument.Version);

		Assert.IsTrue(tracker.TryStoreDiagnostics(published, expectedDocumentVersion: trackedDocument.Version, sourceContent: "return 1"));

		// The published payload and the cached read share one projection rather than each projecting the same entries.
		Assert.AreSame(published.Diagnostics, tracker.GetDiagnostics(filePath));
	}

	[TestMethod]
	public void Rename_WithNewContent_ClearsTheCachedPayloads()
	{
		string oldFilePath = Script("rename-old.ext");
		string newFilePath = Script("rename-new.ext");

		var tracker = new DocumentTracker();
		tracker.Synchronize(oldFilePath, "return 1", leases: DocumentLeaseKinds.Open);
		StoreCachedPayloads(tracker, oldFilePath, "return 1");

		Assert.AreEqual(DocumentRenameOutcome.Renamed, tracker.Rename(oldFilePath, newFilePath, "return 2").Outcome);

		DocumentSnapshot? renamedDocument = tracker.GetDocumentSnapshot(newFilePath);

		Assert.IsNotNull(renamedDocument);
		Assert.AreEqual("return 2", renamedDocument.Content);
		Assert.AreEqual(2, renamedDocument.Version);

		// The payloads describe text that the rename replaced, so the renamed record starts without them.
		(IReadOnlyList<DiagnosticEntry> entries, string? sourceContent) = tracker.GetDiagnosticsWithSourceContent(newFilePath);

		Assert.IsEmpty(tracker.GetDiagnostics(newFilePath));
		Assert.IsEmpty(entries);
		Assert.IsNull(sourceContent);
		Assert.IsEmpty(tracker.GetSemanticTokens(newFilePath));
	}

	[TestMethod]
	public void Rename_WithUnchangedContent_KeepsTheCachedPayloadsUnderTheNewPath()
	{
		string oldFilePath = Script("rename-same-old.ext");
		string newFilePath = Script("rename-same-new.ext");

		var tracker = new DocumentTracker();
		tracker.Synchronize(oldFilePath, "return 1", leases: DocumentLeaseKinds.Open);
		StoreCachedPayloads(tracker, oldFilePath, "return 1");

		Assert.AreEqual(DocumentRenameOutcome.Renamed, tracker.Rename(oldFilePath, newFilePath, "return 1").Outcome);

		Assert.HasCount(1, tracker.GetDiagnostics(newFilePath));
		Assert.HasCount(1, tracker.GetSemanticTokens(newFilePath));
		Assert.IsEmpty(tracker.GetDiagnostics(oldFilePath));
		Assert.IsEmpty(tracker.GetSemanticTokens(oldFilePath));
	}

	[TestMethod]
	public void InvalidateServerSynchronization_KeepsTheCachedPayloads()
	{
		string filePath = Script("invalidate-keeps.ext");

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);
		StoreCachedPayloads(tracker, filePath, "return 1");

		Assert.IsTrue(tracker.InvalidateServerSynchronization(filePath));

		// A failed send leaves the last answers of the server readable until a newer payload replaces them.
		Assert.HasCount(1, tracker.GetDiagnostics(filePath));
		Assert.HasCount(1, tracker.GetSemanticTokens(filePath));
	}

	[TestMethod]
	public void PrepareForRestart_KeepsTheCachedPayloads()
	{
		string filePath = Script("restart.ext");

		var tracker = new DocumentTracker();
		tracker.Synchronize(filePath, "return 1", leases: DocumentLeaseKinds.Open);
		StoreCachedPayloads(tracker, filePath, "return 1");

		Assert.HasCount(1, tracker.PrepareForRestart());

		// The last answers of the server stay readable while the restart is in flight.
		Assert.HasCount(1, tracker.GetDiagnostics(filePath));
		Assert.HasCount(1, tracker.GetSemanticTokens(filePath));
	}

	/// <summary>Returns a synthetic absolute path below the tracker's test root; nothing here touches the file system.</summary>
	/// <param name="fileName">The file name to place inside the synthetic <c>Scripts</c> folder.</param>
	/// <returns>An absolute path below the tracker's test root.</returns>
	private static string Script(string fileName) => Path.Combine(s_workspaceRoot, "Scripts", fileName);

	private static void StoreCachedPayloads(DocumentTracker tracker, string filePath, string content)
	{
		DocumentSnapshot? document = tracker.GetDocumentSnapshot(filePath);
		Assert.IsNotNull(document);

		Assert.IsTrue(
			tracker.TryStoreDiagnostics(
				new ParsedDiagnostics(filePath, [CreateDiagnosticEntry("Cached")], version: document.Version),
				expectedDocumentVersion: document.Version,
				sourceContent: content
			)
		);

		Assert.IsTrue(tracker.TryStoreSemanticTokens(filePath, document.Version, [new SemanticToken(new TextRange(0, 6), "variable")]));
	}

	private static DiagnosticEntry CreateDiagnosticEntry(string message) =>
		new(new Diagnostic(severity: DiagnosticSeverity.Warning, message, range: TextRange.FromBounds(0, 1)), EmptyDiagnosticPayload());

	private static Lsp.DiagnosticPayload EmptyDiagnosticPayload() => new(Range: null, Severity: null, Message: null, Source: null, Code: null);
}
