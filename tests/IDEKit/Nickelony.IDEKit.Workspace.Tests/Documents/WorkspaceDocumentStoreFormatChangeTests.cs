using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Covers how a host changes the line terminators and the encoding of a document: a replacement with another format, and a reload with an encoding.
/// </summary>
[TestClass]
public sealed class WorkspaceDocumentStoreFormatChangeTests
{
	private static readonly WorkspaceDocumentOpenOptions s_options = new(
		[TextEncoding.Utf8],
		new TextFileFormat(TextEncoding.Utf8, false, LineTerminatorStyle.Lf)
	);

	public TestContext? TestContext { get; set; }

	private static WorkspaceDocumentReplaceRequest ReplaceRequest(WorkspaceDocumentSnapshot snapshot, string content, TextFileFormat format) =>
		new(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), content, format);

	private static WorkspaceDocumentCommitRequest CommitRequest(WorkspaceDocumentSnapshot snapshot) =>
		new(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp);

	private static WorkspaceDocumentDiscardRequest DiscardRequest(WorkspaceDocumentSnapshot snapshot) =>
		new(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version));

	private async Task<WorkspaceDocumentSnapshot> OpenAsync(
		WorkspaceDocumentStore store,
		string path,
		WorkspaceDocumentOpenOptions? options = null
	) => (await store.OpenAsync(path, options ?? s_options, TestContext!.CancellationToken)).Snapshot!;

	[TestMethod]
	public async Task Commit_AfterAReplacementWithNewTerminatorsAndTheirStyle_WritesTheNewTerminators()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("lines.txt");

		File.WriteAllBytes(path, "one\r\ntwo\r\nthree\r\n"u8.ToArray());

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = await OpenAsync(store, path);

		Assert.AreEqual(LineTerminatorStyle.CrLf, opened.FileFormat.LineTerminatorStyle);

		WorkspaceDocumentSnapshot converted = store
			.Replace(ReplaceRequest(opened, "one\ntwo\nthree\n", opened.FileFormat with { LineTerminatorStyle = LineTerminatorStyle.Lf }))
			.Snapshot!;

		Assert.IsTrue(converted.IsDirty);

		WorkspaceDocumentCommitResult committed = await store.CommitAsync(CommitRequest(converted), TestContext!.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);
		Assert.AreSequenceEqual("one\ntwo\nthree\n"u8.ToArray(), File.ReadAllBytes(path));
		Assert.IsFalse(committed.Snapshot!.IsDirty);
		Assert.AreEqual(LineTerminatorStyle.Lf, committed.Snapshot.FileFormat.LineTerminatorStyle);
	}

	[TestMethod]
	public async Task Discard_AfterAReplacementWithNewTerminators_RestoresTheTerminatorsAndTheirStyle()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("lines.txt");

		File.WriteAllBytes(path, "one\r\ntwo\r\n"u8.ToArray());

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = await OpenAsync(store, path);

		WorkspaceDocumentSnapshot converted = store
			.Replace(ReplaceRequest(opened, "one\ntwo\n", opened.FileFormat with { LineTerminatorStyle = LineTerminatorStyle.Lf }))
			.Snapshot!;

		WorkspaceDocumentSnapshot discarded = store.Discard(DiscardRequest(converted)).Snapshot!;

		Assert.AreEqual("one\r\ntwo\r\n", discarded.Content);
		Assert.AreEqual(LineTerminatorStyle.CrLf, discarded.FileFormat.LineTerminatorStyle);
		Assert.IsFalse(discarded.IsDirty);
	}

	[TestMethod]
	public async Task Replace_OfTheStyleAlone_ForATextWithoutATerminator_MarksTheDocumentDirtyAndSavesTheSameBytes()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("single.txt");

		File.WriteAllBytes(path, "one line"u8.ToArray());

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = await OpenAsync(store, path);

		Assert.AreEqual(LineTerminatorStyle.None, opened.FileFormat.LineTerminatorStyle);

		WorkspaceDocumentMutationResult chosen = store.Replace(
			ReplaceRequest(opened, opened.Content, opened.FileFormat with { LineTerminatorStyle = LineTerminatorStyle.CrLf })
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, chosen.Outcome);
		Assert.IsTrue(chosen.Snapshot!.IsDirty);

		WorkspaceDocumentCommitResult committed = await store.CommitAsync(CommitRequest(chosen.Snapshot), TestContext!.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);
		Assert.AreSequenceEqual("one line"u8.ToArray(), File.ReadAllBytes(path));
	}

	[TestMethod]
	public async Task Replace_OfTheSameTerminatorsAndStyle_ChangesNothing()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("lines.txt");

		File.WriteAllBytes(path, "one\ntwo\n"u8.ToArray());

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = await OpenAsync(store, path);
		WorkspaceDocumentMutationResult same = store.Replace(ReplaceRequest(opened, opened.Content, opened.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.NoChange, same.Outcome);
		Assert.IsFalse(same.Snapshot!.IsDirty);
		Assert.AreEqual(opened.Version, same.Snapshot.Version);
	}

	[TestMethod]
	public async Task Commit_AfterAReplacementWithOnlyAnotherEncoding_WritesTheSameTextInThatEncoding()
	{
		(TextFileFormat Format, byte[] Expected, string Label)[] cases =
		[
			(new TextFileFormat(TextEncoding.Utf8, true, LineTerminatorStyle.Lf), [0xEF, 0xBB, 0xBF, 0x63, 0x61, 0x66, 0xC3, 0xA9], "UTF-8 with BOM"),

			(
				new TextFileFormat(TextEncoding.Utf16LittleEndian, true, LineTerminatorStyle.Lf),
				[0xFF, 0xFE, 0x63, 0x00, 0x61, 0x00, 0x66, 0x00, 0xE9, 0x00],
				"UTF-16 LE"
			),

			(
				new TextFileFormat(TextEncoding.Utf16BigEndian, true, LineTerminatorStyle.Lf),
				[0xFE, 0xFF, 0x00, 0x63, 0x00, 0x61, 0x00, 0x66, 0x00, 0xE9],
				"UTF-16 BE"
			),

			(new TextFileFormat(TextEncoding.Windows1252, false, LineTerminatorStyle.Lf), [0x63, 0x61, 0x66, 0xE9], "Windows-1252"),
		];

		foreach ((TextFileFormat format, byte[] expected, string label) in cases)
		{
			using var temp = new TemporaryDirectory();
			string path = temp.GetFilePath("text.txt");

			File.WriteAllBytes(path, "café"u8.ToArray());

			await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

			WorkspaceDocumentSnapshot opened = await OpenAsync(store, path);
			WorkspaceDocumentSnapshot reencoded = store.Replace(ReplaceRequest(opened, opened.Content, format)).Snapshot!;

			Assert.IsTrue(reencoded.IsDirty, label);

			WorkspaceDocumentCommitResult committed = await store.CommitAsync(CommitRequest(reencoded), TestContext!.CancellationToken);

			Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome, label);
			Assert.AreSequenceEqual(expected, File.ReadAllBytes(path), label);
			Assert.AreEqual(label, committed.Snapshot!.FileFormat.EncodingDisplayName);
			Assert.AreEqual("café", committed.Snapshot.Content, label);
		}
	}

	[TestMethod]
	public async Task Commit_AfterAReplacementWithAnEncodingThatCannotHoldTheText_FailsAndKeepsTheFile_AndDiscardRestoresTheEncoding()
	{
		using var temp = new TemporaryDirectory();

		string path = temp.GetFilePath("text.txt");
		byte[] original = "日本"u8.ToArray();

		File.WriteAllBytes(path, original);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = await OpenAsync(store, path);

		WorkspaceDocumentSnapshot reencoded = store
			.Replace(ReplaceRequest(opened, opened.Content, opened.FileFormat with { Encoding = TextEncoding.Windows1252 }))
			.Snapshot!;

		WorkspaceDocumentCommitResult committed = await store.CommitAsync(CommitRequest(reencoded), TestContext!.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.WriteFailed, committed.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, committed.Failure!.Code);
		Assert.AreSequenceEqual(original, File.ReadAllBytes(path));
		Assert.IsTrue(committed.Snapshot!.IsDirty);

		WorkspaceDocumentSnapshot discarded = store.Discard(DiscardRequest(committed.Snapshot)).Snapshot!;

		Assert.AreEqual(TextEncoding.Utf8, discarded.FileFormat.Encoding);
		Assert.IsFalse(discarded.IsDirty);
	}

	[TestMethod]
	public async Task Reload_WithAnEncoding_ForADocumentWithUnsavedChanges_ReportsUnsavedChanges_AndSucceedsOnceTheChangesAreDiscarded()
	{
		using var temp = new TemporaryDirectory();
		string path = temp.GetFilePath("text.txt");

		// The Czech letters U+0159 U+011B U+0161 in Windows-1250.
		File.WriteAllBytes(path, [0xF8, 0xEC, 0x9A]);

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentOpenOptions options = s_options with { NoBomEncodings = [TextEncoding.Utf8, TextEncoding.Windows1252] };
		WorkspaceDocumentSnapshot opened = await OpenAsync(store, path, options);

		Assert.AreEqual(TextEncoding.Windows1252, opened.FileFormat.Encoding);

		WorkspaceDocumentSnapshot edited = store.Replace(ReplaceRequest(opened, opened.Content + "!", opened.FileFormat)).Snapshot!;
		var windows1250 = TextEncoding.FromCodePage(1250);

		WorkspaceDocumentReloadResult refused = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(edited.DocumentKey, edited.DocumentId, edited.Version), windows1250),
			TestContext!.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.UnsavedChanges, refused.Outcome);

		WorkspaceDocumentSnapshot discarded = store.Discard(DiscardRequest(refused.Snapshot!)).Snapshot!;

		WorkspaceDocumentReloadResult reopened = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(discarded.DocumentKey, discarded.DocumentId, discarded.Version), windows1250),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, reopened.Outcome);
		Assert.AreEqual("řěš", reopened.Snapshot!.Content);
		Assert.AreEqual(windows1250, reopened.Snapshot.FileFormat.Encoding);
	}
}
