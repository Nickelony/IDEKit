using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Views;
#if AVALONIAEDIT
using AvaloniaEdit;

namespace Nickelony.Samples.Avalonia.Tests;

#else
using ICSharpCode.AvalonEdit;

namespace Nickelony.Samples.Wpf.Tests;

#endif

[STATestClass]
public sealed class SampleDocumentViewTests
{
	private static readonly TextFileFormat s_format = new(TextEncoding.Utf8, HasBom: false, LineTerminatorStyle.Lf);

	private static WorkspaceDocumentSnapshot Snapshot(string text, long version = 0, WorkspaceDocumentKey? key = null)
	{
		return new WorkspaceDocumentSnapshot(
			DocumentKey: key ?? new WorkspaceDocumentKey(Guid.NewGuid()),
			DocumentId: "view-test.lua",
			DisplayPath: "view-test.lua",
			version,
			PersistedVersion: version,
			IsDirty: false,
			Text: new StringTextSnapshot(text, "view-test.lua"),
			FileFormat: s_format,
			OnDiskStamp: FileStamp.Missing
		);
	}

	[TestMethod]
	public async Task Open_PutsTheSnapshotTextInTheEditor_AndTypingInTheEditorIsPending()
	{
		await SampleTestHost.RunAsync(() =>
		{
			var editor = new TextEditor();
			var view = new SampleDocumentView("view", editor);

			Assert.AreEqual(WorkspaceDocumentViewOpenOutcome.Opened, view.Open(Snapshot("print(1)")).Outcome);
			Assert.AreEqual("print(1)", editor.Text);
			Assert.IsFalse(view.HasPendingEdits);

			bool dirtyChanged = false;
			view.DirtyChanged += (_, _) => dirtyChanged = true;
			editor.Text = "print(2)";

			Assert.IsTrue(view.HasPendingEdits);
			Assert.IsTrue(dirtyChanged);

			return Task.CompletedTask;
		});
	}

	[TestMethod]
	public async Task Refresh_ReplacesOnlyTheChangedText_SoTheCaretStays()
	{
		await SampleTestHost.RunAsync(() =>
		{
			var editor = new TextEditor();
			var view = new SampleDocumentView("view", editor);

			WorkspaceDocumentSnapshot opened = Snapshot("print(1)\nprint(2)");
			view.Open(opened);
			editor.CaretOffset = 3;

			WorkspaceDocumentViewRefreshResult refreshed = view.Refresh(Snapshot("print(1)\nprint(3)", 1, opened.DocumentKey));

			Assert.AreEqual(WorkspaceDocumentViewRefreshOutcome.Refreshed, refreshed.Outcome);
			Assert.AreEqual("print(1)\nprint(3)", editor.Text);
			Assert.AreEqual(3, editor.CaretOffset);

			return Task.CompletedTask;
		});
	}

	[TestMethod]
	public async Task AcknowledgeApply_OfAPublicationThatAChangeOfAnotherLineOvertook_KeepsTheCaretWhereTheTypingPutIt()
	{
		await SampleTestHost.RunAsync(() =>
		{
			var editor = new TextEditor();
			var view = new SampleDocumentView("view", editor);

			WorkspaceDocumentSnapshot opened = Snapshot("one\ntwo\nthree\n");
			view.Open(opened);
			List<WorkspaceDocumentReplaceRequest> published = [];
			view.ApplyRequested += (_, e) => published.Add(e.Request);

			editor.Document.Replace(8, 5, "THREE!");
			editor.CaretOffset = 14;
			view.PublishPendingEdits();

			WorkspaceDocumentSnapshot other = Snapshot("first\ntwo\nthree\n", 1, opened.DocumentKey);
			view.AcknowledgeApply(new WorkspaceDocumentMutationResult(WorkspaceDocumentMutationOutcome.StaleDocument, published[0].Identity, other));

			// The other change lies before the caret, so the caret moves with the text it follows.
			Assert.AreEqual("first\ntwo\nTHREE!\n", editor.Text);
			Assert.AreEqual(16, editor.CaretOffset);
			Assert.AreEqual("first\ntwo\nTHREE!\n", published[1].Content);

			return Task.CompletedTask;
		});
	}

	[TestMethod]
	public async Task DeleteLease_MakesTheEditorReadOnlyUntilItIsReleased()
	{
		await SampleTestHost.RunAsync(() =>
		{
			var editor = new TextEditor();
			var view = new SampleDocumentView("view", editor);

			view.Open(Snapshot("print(1)"));

			Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Entered, view.EnterDeleteLease().Outcome);
			Assert.IsTrue(editor.IsReadOnly);

			Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Released, view.ReleaseDeleteLease().Outcome);
			Assert.IsFalse(editor.IsReadOnly);

			return Task.CompletedTask;
		});
	}
}
