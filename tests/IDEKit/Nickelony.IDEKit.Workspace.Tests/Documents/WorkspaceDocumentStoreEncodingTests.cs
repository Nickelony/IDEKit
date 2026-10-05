using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class WorkspaceDocumentStoreEncodingTests
{
	private static readonly TextFileFormat s_newFileFormat = new(TextEncoding.Utf8, false, LineTerminatorStyle.Lf);

	// The Czech letters U+0159 U+011B U+0161 in Windows-1250, whose first byte is invalid UTF-8, and the Japanese word U+65E5 U+672C U+8A9E in Shift-JIS.
	private static readonly byte[] s_windows1250Bytes = [0xF8, 0xEC, 0x9A];
	private static readonly byte[] s_shiftJisBytes = [0x93, 0xFA, 0x96, 0x7B, 0x8C, 0xEA];

	private static TextEncoding Windows1250 => TextEncoding.FromCodePage(1250);

	private static TextEncoding ShiftJis => TextEncoding.FromCodePage(932);

	// No provider serves this code page, so the process cannot resolve it whichever providers the test assembly registered.
	private static TextEncoding Unresolvable => new(4242);

	public TestContext? TestContext { get; set; }

	[TestMethod]
	public async Task Open_FileInWindows1250_DecodesThroughTheCandidateListAndRecordsTheEncoding()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, s_windows1250Bytes);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentOpenResult result = await store.OpenAsync(path, OptionsOf(TextEncoding.Utf8, Windows1250), TestContext!.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, result.Outcome);
		Assert.AreEqual("\u0159\u011B\u0161", result.Snapshot!.Content);
		Assert.AreEqual(new TextFileFormat(Windows1250, false, LineTerminatorStyle.None), result.Snapshot.FileFormat);
		Assert.IsFalse(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task Open_FileInShiftJis_DecodesThroughTheCandidateListAndRecordsTheEncoding()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, s_shiftJisBytes);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentOpenResult result = await store.OpenAsync(path, OptionsOf(TextEncoding.Utf8, ShiftJis), TestContext!.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, result.Outcome);
		Assert.AreEqual("\u65E5\u672C\u8A9E", result.Snapshot!.Content);
		Assert.AreEqual(ShiftJis, result.Snapshot.FileFormat.Encoding);
	}

	[TestMethod]
	public async Task Open_FileThatTheFirstCandidateDecodes_RecordsThatCandidate()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, [0x63, 0x61, 0x66, 0xC3, 0xA9]);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (
			await store.OpenAsync(path, OptionsOf(TextEncoding.Utf8, Windows1250), TestContext!.CancellationToken)
		).Snapshot!;

		Assert.AreEqual("caf\u00E9", opened.Content);
		Assert.AreEqual(TextEncoding.Utf8, opened.FileFormat.Encoding);
	}

	[TestMethod]
	public async Task Open_FileWithAByteOrderMark_IgnoresTheCandidates()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, 0x63, 0x61, 0x66, 0xC3, 0xA9]);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, OptionsOf(ShiftJis, Windows1250), TestContext!.CancellationToken)).Snapshot!;

		Assert.AreEqual("caf\u00E9", opened.Content);
		Assert.AreEqual(new TextFileFormat(TextEncoding.Utf8, true, LineTerminatorStyle.None), opened.FileFormat);
	}

	[TestMethod]
	public async Task Open_BytesThatNoCandidateDecodes_FailsWithInvalidEncodingAndTracksNothing()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		// Invalid UTF-8, and an odd length that UTF-16 rejects too.
		File.WriteAllBytes(path, [0xC3, 0x28, 0x41]);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentOpenResult result = await store.OpenAsync(
			path,
			OptionsOf(TextEncoding.Utf8, TextEncoding.Utf16LittleEndian),
			TestContext!.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.LoadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.IsNull(result.Snapshot);
		Assert.IsFalse(store.TryGetSnapshot(path, out _));
	}

	[TestMethod]
	public async Task Commit_WritesTheEncodingTheDocumentRecordedByteForByte()
	{
		(TextEncoding Encoding, byte[] Initial, string Replacement, byte[] Expected)[] cases =
		[
			(Windows1250, s_windows1250Bytes, "\u011B\u0161", [0xEC, 0x9A]),
			(ShiftJis, s_shiftJisBytes, "\u672C", [0x96, 0x7B]),
			(TextEncoding.Windows1252, [0x6E, 0x61, 0x6D, 0x65], "caf\u00E9", [0x63, 0x61, 0x66, 0xE9]),
			(TextEncoding.Utf16LittleEndian, [0x61, 0x00, 0x62, 0x00], "cd", [0x63, 0x00, 0x64, 0x00]),
			(TextEncoding.Utf16BigEndian, [0x00, 0x61, 0x00, 0x62], "cd", [0x00, 0x63, 0x00, 0x64]),
			(TextEncoding.Utf8, [0x61, 0x62], "\u00E9", [0xC3, 0xA9]),
		];

		foreach ((TextEncoding encoding, byte[] initial, string replacement, byte[] expected) in cases)
		{
			using var temp = new TemporaryDirectory();
			string path = temp.GetFilePath("strings.txt");

			File.WriteAllBytes(path, initial);

			await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

			WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, OptionsOf(encoding), TestContext!.CancellationToken)).Snapshot!;
			WorkspaceDocumentSnapshot edited = store.Replace(ReplaceRequest(opened, replacement)).Snapshot!;

			WorkspaceDocumentCommitResult committed = await store.CommitAsync(CommitRequest(edited), TestContext.CancellationToken);

			Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome, encoding.DisplayName);
			Assert.AreEqual(encoding, committed.Snapshot!.FileFormat.Encoding, encoding.DisplayName);
			Assert.AreSequenceEqual(expected, File.ReadAllBytes(path));
		}
	}

	[TestMethod]
	public async Task Commit_CharacterThatTheRecordedEncodingCannotRepresent_FailsTheSaveAndLeavesTheFile()
	{
		(TextEncoding Encoding, byte[] Initial, string Unencodable)[] cases =
		[
			(Windows1250, s_windows1250Bytes, "\u0439"),
			(ShiftJis, s_shiftJisBytes, "\u00E9"),
		];

		foreach ((TextEncoding encoding, byte[] initial, string unencodable) in cases)
		{
			using var temp = new TemporaryDirectory();
			string path = temp.GetFilePath("strings.txt");

			File.WriteAllBytes(path, initial);

			await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

			WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, OptionsOf(encoding), TestContext!.CancellationToken)).Snapshot!;
			WorkspaceDocumentSnapshot edited = store.Replace(ReplaceRequest(opened, unencodable)).Snapshot!;

			WorkspaceDocumentCommitResult committed = await store.CommitAsync(CommitRequest(edited), TestContext.CancellationToken);

			Assert.AreEqual(WorkspaceDocumentCommitOutcome.WriteFailed, committed.Outcome, encoding.DisplayName);
			Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, committed.Failure!.Code, encoding.DisplayName);
			Assert.IsTrue(committed.Snapshot!.IsDirty, encoding.DisplayName);
			Assert.AreSequenceEqual(initial, File.ReadAllBytes(path));
		}
	}

	[TestMethod]
	public async Task Reload_WithAnEncoding_DecodesTheSameBytesAgainAndRecordsIt()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, s_windows1250Bytes);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (
			await store.OpenAsync(path, OptionsOf(TextEncoding.Windows1252), TestContext!.CancellationToken)
		).Snapshot!;

		Assert.AreEqual("\u00F8\u00EC\u0161", opened.Content);

		WorkspaceDocumentReloadResult reloaded = await store.ReloadAsync(ReloadRequest(opened, Windows1250), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, reloaded.Outcome);
		Assert.AreEqual("\u0159\u011B\u0161", reloaded.Snapshot!.Content);
		Assert.AreEqual(Windows1250, reloaded.Snapshot.FileFormat.Encoding);
		Assert.AreEqual(opened.Version + 1, reloaded.Snapshot.Version);
		Assert.AreEqual(opened.OnDiskStamp, reloaded.Snapshot.OnDiskStamp);
		Assert.IsFalse(reloaded.Snapshot.IsDirty);
		Assert.AreSequenceEqual(s_windows1250Bytes, File.ReadAllBytes(path));

		// The same encoding again decodes to what the document holds, so nothing changes.
		WorkspaceDocumentReloadResult again = await store.ReloadAsync(ReloadRequest(reloaded.Snapshot, Windows1250), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, again.Outcome);
		Assert.AreEqual(reloaded.Snapshot.Version, again.Snapshot!.Version);
	}

	[TestMethod]
	public async Task Reload_WithAnEncoding_StaysTheDocumentsEncodingForLaterReloads()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, s_windows1250Bytes);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (
			await store.OpenAsync(path, OptionsOf(TextEncoding.Windows1252), TestContext!.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentSnapshot reopened = (await store.ReloadAsync(ReloadRequest(opened, Windows1250), TestContext.CancellationToken)).Snapshot!;

		// The file changes on disk and a plain reload reads it, as a file watcher does: the chosen encoding still decodes it.
		File.WriteAllBytes(path, [0xF8, 0xEC, 0x9A, 0x41]);

		WorkspaceDocumentReloadResult changed = await store.ReloadAsync(ReloadRequest(reopened), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, changed.Outcome);
		Assert.AreEqual("\u0159\u011B\u0161A", changed.Snapshot!.Content);
		Assert.AreEqual(Windows1250, changed.Snapshot.FileFormat.Encoding);
	}

	[TestMethod]
	public async Task Reload_UnchangedWithAnEncoding_StillBecomesTheDocumentsEncoding()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, s_windows1250Bytes);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		// The first candidate fails on the bytes, so the document already holds Windows-1250 and the reopen decodes to the same text.
		WorkspaceDocumentSnapshot opened = (
			await store.OpenAsync(path, OptionsOf(TextEncoding.Utf8, Windows1250), TestContext!.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentReloadResult same = await store.ReloadAsync(ReloadRequest(opened, Windows1250), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, same.Outcome);

		// Bytes that the open candidates would read as UTF-8: the encoding that the reopen chose reads them as Windows-1250 instead.
		File.WriteAllBytes(path, [0xC3, 0xA9]);

		WorkspaceDocumentReloadResult changed = await store.ReloadAsync(ReloadRequest(same.Snapshot!), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, changed.Outcome);
		Assert.AreEqual("\u0102\u00A9", changed.Snapshot!.Content);
		Assert.AreEqual(Windows1250, changed.Snapshot.FileFormat.Encoding);
	}

	[TestMethod]
	public async Task Reload_AfterAReopen_FailsInsteadOfFallingBackToTheOpenCandidates()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, [0x61, 0x62, 0x63]);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (
			await store.OpenAsync(path, OptionsOf(TextEncoding.Windows1252), TestContext!.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentSnapshot reopened = (
			await store.ReloadAsync(ReloadRequest(opened, TextEncoding.Utf8), TestContext.CancellationToken)
		).Snapshot!;

		Assert.AreEqual(TextEncoding.Utf8, reopened.FileFormat.Encoding);

		// A lone 0xE9 is not UTF-8. The open candidate Windows-1252 would read it as a letter, but the user chose UTF-8 for this file.
		File.WriteAllBytes(path, [0xE9]);

		WorkspaceDocumentReloadResult failed = await store.ReloadAsync(ReloadRequest(reopened), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, failed.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, failed.Failure!.Code);
		Assert.AreEqual("abc", failed.Snapshot!.Content);
		Assert.AreEqual(TextEncoding.Utf8, failed.Snapshot.FileFormat.Encoding);

		// Another reopen replaces the choice.
		WorkspaceDocumentReloadResult again = await store.ReloadAsync(
			ReloadRequest(failed.Snapshot, TextEncoding.Windows1252),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, again.Outcome);
		Assert.AreEqual("\u00E9", again.Snapshot!.Content);
	}

	[TestMethod]
	public async Task Reload_WithAnEncoding_AByteOrderMarkStillDecides()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, 0x63, 0x61, 0x66, 0xC3, 0xA9]);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, OptionsOf(TextEncoding.Utf8), TestContext!.CancellationToken)).Snapshot!;

		// Decoded as Windows-1250 the bytes would read differently and the reload would report a change.
		WorkspaceDocumentReloadResult result = await store.ReloadAsync(ReloadRequest(opened, Windows1250), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, result.Outcome);
		Assert.AreEqual("caf\u00E9", result.Snapshot!.Content);
		Assert.AreEqual(new TextFileFormat(TextEncoding.Utf8, true, LineTerminatorStyle.None), result.Snapshot.FileFormat);
	}

	[TestMethod]
	public async Task Reload_WithAnEncodingThatCannotDecode_FailsAndLeavesTheDocument()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, s_windows1250Bytes);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, OptionsOf(Windows1250), TestContext!.CancellationToken)).Snapshot!;

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(ReloadRequest(opened, TextEncoding.Utf8), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.AreEqual(opened.Content, result.Snapshot!.Content);
		Assert.AreEqual(opened.FileFormat, result.Snapshot.FileFormat);
		Assert.AreEqual(opened.Version, result.Snapshot.Version);
	}

	[TestMethod]
	public async Task Reload_DocumentWithUnsavedChanges_ReportsUnsavedChangesForAnEncodingWhateverTheDiskHolds()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, s_windows1250Bytes);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (
			await store.OpenAsync(path, OptionsOf(TextEncoding.Windows1252), TestContext!.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentSnapshot dirty = store.Replace(ReplaceRequest(opened, "unsaved")).Snapshot!;

		// The file is unchanged: a plain reload says so, but a requested encoding is not a no-op, so it must not say Unchanged.
		WorkspaceDocumentReloadResult plain = await store.ReloadAsync(ReloadRequest(dirty), TestContext.CancellationToken);
		WorkspaceDocumentReloadResult requested = await store.ReloadAsync(ReloadRequest(dirty, Windows1250), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, plain.Outcome);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.UnsavedChanges, requested.Outcome);
		Assert.IsNull(requested.ObservedOnDiskStamp);
		Assert.AreEqual("unsaved", requested.Snapshot!.Content);
		Assert.AreEqual(TextEncoding.Windows1252, requested.Snapshot.FileFormat.Encoding);
		Assert.AreEqual(dirty.Version, requested.Snapshot.Version);

		// The file changed: a plain reload reports the conflict, and the requested encoding still reports the unsaved changes.
		File.WriteAllBytes(path, [0xF8, 0xEC, 0x9A, 0x41]);

		WorkspaceDocumentReloadResult conflict = await store.ReloadAsync(ReloadRequest(dirty), TestContext.CancellationToken);
		WorkspaceDocumentReloadResult requestedAgain = await store.ReloadAsync(ReloadRequest(dirty, Windows1250), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, conflict.Outcome);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.UnsavedChanges, requestedAgain.Outcome);
		Assert.AreEqual("unsaved", requestedAgain.Snapshot!.Content);
	}

	[TestMethod]
	public async Task Reload_RefusedForUnsavedChanges_LeavesTheDocumentsEncodingAlone()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, s_windows1250Bytes);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (
			await store.OpenAsync(path, OptionsOf(TextEncoding.Windows1252), TestContext!.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentSnapshot dirty = store.Replace(ReplaceRequest(opened, "unsaved")).Snapshot!;

		await store.ReloadAsync(ReloadRequest(dirty, Windows1250), TestContext.CancellationToken);

		// The refused request changed nothing: after the edits are discarded, a changed file decodes with the candidates of the open.
		WorkspaceDocumentSnapshot discarded = store
			.Discard(new WorkspaceDocumentDiscardRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)))
			.Snapshot!;

		File.WriteAllBytes(path, [0xF8, 0xEC, 0x9A, 0x41]);

		WorkspaceDocumentReloadResult reloaded = await store.ReloadAsync(ReloadRequest(discarded), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, reloaded.Outcome);
		Assert.AreEqual(TextEncoding.Windows1252, reloaded.Snapshot!.FileFormat.Encoding);
	}

	[TestMethod]
	public async Task Reload_WithAnEncodingThatAByteOrderMarkOverrode_LeavesTheCandidatesAsTheyWere()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, 0x63, 0xC3, 0xA9]);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, OptionsOf(TextEncoding.Utf8), TestContext!.CancellationToken)).Snapshot!;
		WorkspaceDocumentReloadResult reopened = await store.ReloadAsync(ReloadRequest(opened, Windows1250), TestContext.CancellationToken);

		// The mark decided, so nothing was read as Windows-1250 and the request is not the user's choice for this file.
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, reopened.Outcome);

		File.WriteAllBytes(path, [0x63, 0xC3, 0xA9]);

		WorkspaceDocumentReloadResult changed = await store.ReloadAsync(ReloadRequest(reopened.Snapshot!), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, changed.Outcome);
		Assert.AreEqual("c\u00E9", changed.Snapshot!.Content);
		Assert.AreEqual(TextEncoding.Utf8, changed.Snapshot.FileFormat.Encoding);
	}

	[TestMethod]
	public async Task Commit_OfAnotherEncodingThanTheOneDecoded_MakesItTheEncodingTheFileReadsBackWith()
	{
		TextEncoding[][] candidateLists =
		[
			[TextEncoding.Utf8],
			[TextEncoding.Utf8, TextEncoding.Windows1252],
		];

		foreach (TextEncoding[] candidates in candidateLists)
		{
			using var temp = new TemporaryDirectory();
			string path = temp.GetFilePath("strings.txt");

			File.WriteAllBytes(path, "abc"u8.ToArray());

			await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

			WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, OptionsOf(candidates), TestContext!.CancellationToken)).Snapshot!;

			WorkspaceDocumentSnapshot edited = store
				.Replace(
					new(
						new(opened.DocumentKey, opened.DocumentId, opened.Version),
						"\u65E5\u672C",
						new TextFileFormat(ShiftJis, false, LineTerminatorStyle.None)
					)
				)
				.Snapshot!;

			WorkspaceDocumentCommitResult committed = await store.CommitAsync(CommitRequest(edited), TestContext.CancellationToken);

			Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);
			Assert.AreSequenceEqual(new byte[] { 0x93, 0xFA, 0x96, 0x7B }, File.ReadAllBytes(path));

			// A watcher reads the host's own save, and then an external tool rewrites the file in the same encoding.
			WorkspaceDocumentReloadResult own = await store.ReloadAsync(ReloadRequest(committed.Snapshot!), TestContext.CancellationToken);

			Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, own.Outcome);

			File.WriteAllBytes(path, s_shiftJisBytes);

			WorkspaceDocumentReloadResult changed = await store.ReloadAsync(ReloadRequest(own.Snapshot!), TestContext.CancellationToken);

			Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, changed.Outcome, candidates.Length.ToString());
			Assert.AreEqual("\u65E5\u672C\u8A9E", changed.Snapshot!.Content, candidates.Length.ToString());
			Assert.AreEqual(ShiftJis, changed.Snapshot.FileFormat.Encoding, candidates.Length.ToString());
		}
	}

	[TestMethod]
	public async Task SaveAs_OfAnotherEncodingThanTheOneDecoded_MakesItTheEncodingTheFileReadsBackWith()
	{
		using var temp = new TemporaryDirectory();

		string path = temp.GetFilePath("strings.txt");
		string destination = temp.GetFilePath("saved.txt");

		File.WriteAllBytes(path, "abc"u8.ToArray());

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, OptionsOf(TextEncoding.Utf8), TestContext!.CancellationToken)).Snapshot!;

		WorkspaceDocumentSnapshot edited = store
			.Replace(
				new(
					new(opened.DocumentKey, opened.DocumentId, opened.Version),
					"\u65E5\u672C",
					new TextFileFormat(ShiftJis, false, LineTerminatorStyle.None)
				)
			)
			.Snapshot!;

		WorkspaceDocumentSaveAsResult saved = await store.SaveAsAsync(
			new(new(edited.DocumentKey, edited.DocumentId, edited.Version), edited.OnDiskStamp, destination),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, saved.Outcome);

		File.WriteAllBytes(destination, s_shiftJisBytes);

		WorkspaceDocumentReloadResult changed = await store.ReloadAsync(ReloadRequest(saved.Snapshot!), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, changed.Outcome);
		Assert.AreEqual("\u65E5\u672C\u8A9E", changed.Snapshot!.Content);
		Assert.AreEqual(ShiftJis, changed.Snapshot.FileFormat.Encoding);
	}

	[TestMethod]
	public async Task Commit_OfTheEncodingTheDocumentDecodedWith_KeepsTheCandidates()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		// A lone 0xE9 is not UTF-8, so the second candidate decodes it.
		File.WriteAllBytes(path, [0xE9]);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (
			await store.OpenAsync(path, OptionsOf(TextEncoding.Utf8, TextEncoding.Windows1252), TestContext!.CancellationToken)
		).Snapshot!;

		Assert.AreEqual(TextEncoding.Windows1252, opened.FileFormat.Encoding);

		WorkspaceDocumentSnapshot edited = store.Replace(ReplaceRequest(opened, "\u00E9\u00E9")).Snapshot!;
		WorkspaceDocumentCommitResult committed = await store.CommitAsync(CommitRequest(edited), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);

		// The save did not change the encoding, so the detection the host configured still applies to what an external tool writes next.
		File.WriteAllBytes(path, [0xC3, 0xA9]);

		WorkspaceDocumentReloadResult changed = await store.ReloadAsync(ReloadRequest(committed.Snapshot!), TestContext.CancellationToken);

		Assert.AreEqual("\u00E9", changed.Snapshot!.Content);
		Assert.AreEqual(TextEncoding.Utf8, changed.Snapshot.FileFormat.Encoding);
	}

	[TestMethod]
	public async Task Reload_OfAFileWhoseStampIsTracked_ReportsUnchangedWithoutDecoding()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, "abc"u8.ToArray());

		var fileSystem = new TransformingFileSystem(new LocalWorkspaceFileSystem());
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, OptionsOf(TextEncoding.Utf8), TestContext!.CancellationToken)).Snapshot!;

		// The same stamp, with bytes that the candidates cannot decode: nothing would be adopted, so nothing needs reading.
		fileSystem.Transform = read => WorkspaceFileReadResult.FromBytes(read.OnDiskStamp, new byte[] { 0xE9 });

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(ReloadRequest(opened), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, result.Outcome);
		Assert.AreEqual("abc", result.Snapshot!.Content);
	}

	[TestMethod]
	public async Task Reload_WithAnEncodingWhenTheReadReturnsDecodedContent_FailsAndWritesNothingSticky()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, s_windows1250Bytes);

		var fileSystem = new TransformingFileSystem(new LocalWorkspaceFileSystem());
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot opened = (
			await store.OpenAsync(path, OptionsOf(TextEncoding.Windows1252), TestContext!.CancellationToken)
		).Snapshot!;

		// A host file system that decodes inside its read: the reopen has no bytes to decode with the encoding it asked for.
		fileSystem.Transform = read => WorkspaceFileReadResult.FromContent("decoded", opened.FileFormat, read.OnDiskStamp);

		WorkspaceDocumentReloadResult refused = await store.ReloadAsync(ReloadRequest(opened, Windows1250), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, refused.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.EncodingNotApplicable, refused.Failure!.Code);
		Assert.AreEqual(opened.Content, refused.Snapshot!.Content);
		Assert.AreEqual(opened.Version, refused.Snapshot.Version);

		// The refusal chose nothing: a later change reads with the candidates of the open.
		fileSystem.Transform = null;
		File.WriteAllBytes(path, [0xF8, 0xEC, 0x9A, 0x41]);

		WorkspaceDocumentReloadResult later = await store.ReloadAsync(ReloadRequest(refused.Snapshot), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, later.Outcome);
		Assert.AreEqual(TextEncoding.Windows1252, later.Snapshot!.FileFormat.Encoding);
	}

	[TestMethod]
	public async Task Open_WithACandidateTheProcessCannotResolve_FailsWithEncodingUnavailable()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, "abc"u8.ToArray());

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentOpenResult result = await store.OpenAsync(path, OptionsOf(Unresolvable), TestContext!.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.LoadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.EncodingUnavailable, result.Failure!.Code);
		Assert.IsNull(result.Snapshot);
		Assert.IsFalse(store.TryGetSnapshot(path, out _));
	}

	[TestMethod]
	public async Task Reload_WithAnEncodingTheProcessCannotResolve_FailsWithEncodingUnavailableAndLeavesTheDocument()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("strings.txt");

		File.WriteAllBytes(path, "abc"u8.ToArray());

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, OptionsOf(TextEncoding.Utf8), TestContext!.CancellationToken)).Snapshot!;

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(ReloadRequest(opened, Unresolvable), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.EncodingUnavailable, result.Failure!.Code);
		Assert.AreEqual(opened.FileFormat, result.Snapshot!.FileFormat);
		Assert.AreEqual(opened.Version, result.Snapshot.Version);
	}

	[TestMethod]
	public async Task Commit_InAnEncodingTheProcessCannotResolve_FailsWithEncodingUnavailableAndLeavesTheFile()
	{
		using var temp = new TemporaryDirectory();

		string path = temp.GetFilePath("strings.txt");
		string destination = temp.GetFilePath("saved.txt");

		File.WriteAllBytes(path, "abc"u8.ToArray());

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, OptionsOf(TextEncoding.Utf8), TestContext!.CancellationToken)).Snapshot!;

		WorkspaceDocumentSnapshot edited = store
			.Replace(
				new(
					new(opened.DocumentKey, opened.DocumentId, opened.Version),
					"xyz",
					new TextFileFormat(Unresolvable, false, LineTerminatorStyle.None)
				)
			)
			.Snapshot!;

		WorkspaceDocumentCommitResult committed = await store.CommitAsync(CommitRequest(edited), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.WriteFailed, committed.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.EncodingUnavailable, committed.Failure!.Code);
		Assert.IsTrue(committed.Snapshot!.IsDirty);

		WorkspaceDocumentSaveAsResult saved = await store.SaveAsAsync(
			new(new(edited.DocumentKey, edited.DocumentId, edited.Version), edited.OnDiskStamp, destination),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.WriteFailed, saved.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.EncodingUnavailable, saved.Failure!.Code);
		Assert.AreSequenceEqual("abc"u8.ToArray(), File.ReadAllBytes(path));
		Assert.IsFalse(File.Exists(destination));
	}

	private static WorkspaceDocumentOpenOptions OptionsOf(params TextEncoding[] candidates) => new(candidates, s_newFileFormat);

	private static WorkspaceDocumentReplaceRequest ReplaceRequest(WorkspaceDocumentSnapshot snapshot, string content) =>
		new(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), content, snapshot.FileFormat);

	private static WorkspaceDocumentCommitRequest CommitRequest(WorkspaceDocumentSnapshot snapshot) =>
		new(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp);

	private static WorkspaceDocumentReloadRequest ReloadRequest(WorkspaceDocumentSnapshot snapshot, TextEncoding? encoding = null) =>
		new(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), encoding);

	// Rewrites what a read returns, so a test can give the store bytes or content that its own file would not produce.
	private sealed class TransformingFileSystem(IWorkspaceFileSystem inner) : WorkspaceFileSystemDecorator(inner)
	{
		public Func<WorkspaceFileReadResult, WorkspaceFileReadResult>? Transform { get; set; }

		public override async Task<WorkspaceFileReadResult> ReadAsync(string path, CancellationToken cancellationToken = default)
		{
			WorkspaceFileReadResult read = await base.ReadAsync(path, cancellationToken).ConfigureAwait(false);
			return Transform is null ? read : Transform(read);
		}
	}
}
