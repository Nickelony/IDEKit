using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class DeferredTextSnapshotTests
{
	[TestMethod]
	public void CharacterQueries_AnswerWithoutLineAccess()
	{
		var snapshot = new DeferredTextSnapshot("one\ntwo", "file.txt");

		// Character and range queries answer from the captured string; the deferred line table is
		// not needed for them, and the answers must match the captured content exactly.
		Assert.AreEqual("file.txt", snapshot.FileName);
		Assert.AreEqual(7, snapshot.TextLength);
		Assert.AreEqual('o', snapshot.GetCharAt(0));
		Assert.AreEqual("two", snapshot.GetText(4, 3));

		// A later line query still observes the same captured content.
		Assert.AreEqual(2, snapshot.LineCount);
		Assert.AreEqual(2, snapshot.GetLineByNumber(2).LineNumber);
	}

	[TestMethod]
	public void LineQueries_RepeatedAccess_IsConsistent()
	{
		var snapshot = new DeferredTextSnapshot("one\ntwo", "file.txt");

		// The line table is built on first use and every later access must observe the same table.
		Assert.AreEqual(2, snapshot.LineCount);
		Assert.AreEqual(2, snapshot.Lines.Count);
		Assert.AreEqual(2, snapshot.GetLineByNumber(2).LineNumber);

		Assert.AreEqual(2, snapshot.LineCount);
		Assert.AreEqual(2, snapshot.Lines.Count);
		Assert.AreEqual(4, snapshot.Lines[1].Offset);
		Assert.AreEqual(3, snapshot.Lines[1].Length);
		Assert.AreEqual("two", snapshot.GetText(snapshot.Lines[1].Offset, snapshot.Lines[1].Length));
	}

	[TestMethod]
	public void OutOfRangeQueries_AreArgumentErrors()
	{
		var snapshot = new DeferredTextSnapshot("one\ntwo", "file.txt");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetCharAt(-1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetCharAt(7));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(-1, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(0, 8));
	}

	[TestMethod]
	public void WorkspaceDocumentSnapshot_Content_FallsBackToSnapshotText()
	{
		// A snapshot whose text source is not the store's deferred snapshot materializes the
		// complete content from the text source on every access.
		WorkspaceDocumentSnapshot snapshot = TestSnapshots.Create("script.lua", "one\ntwo");

		Assert.AreEqual("one\ntwo", snapshot.Content);
	}
}
