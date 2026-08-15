using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class TextWorkspaceEditTests
{
	[TestMethod]
	public void HasChanges_EmptyDocumentList_IsFalse()
	{
		var workspaceEdit = new TextWorkspaceEdit([]);

		Assert.IsFalse(workspaceEdit.HasChanges);
	}

	[TestMethod]
	public void HasChanges_DocumentsWithoutEdits_IsFalse()
	{
		var workspaceEdit = new TextWorkspaceEdit(
		[
			new TextDocumentEdit("a.lua", []),
			new TextDocumentEdit("b.lua", [])
		]);

		Assert.IsFalse(workspaceEdit.HasChanges);
	}

	[TestMethod]
	public void HasChanges_AnyDocumentWithEdits_IsTrue()
	{
		var workspaceEdit = new TextWorkspaceEdit(
		[
			new TextDocumentEdit("a.lua", []),
			new TextDocumentEdit("b.lua", [new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "replacement")])
		]);

		Assert.IsTrue(workspaceEdit.HasChanges);
	}

	[TestMethod]
	public void Constructor_StoresOwnedDocumentAndTextEditSnapshots()
	{
		var textEdits = new List<TextEdit>
		{
			new(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "replacement")
		};

		var documentEdits = new List<TextDocumentEdit>
		{
			new("C:\\Workspace\\test.lua", textEdits)
		};

		var workspaceEdit = new TextWorkspaceEdit(documentEdits);

		textEdits.Clear();
		documentEdits.Clear();

		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits[0].TextEdits.Count);
		Assert.AreEqual("replacement", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void Equals_ValueEqualSnapshots_AreEqual()
	{
		var documentEdits = new List<TextDocumentEdit>
		{
			new("a.lua", [new TextEdit(default, "replacement")])
		};

		var first = new TextWorkspaceEdit(documentEdits);
		var second = new TextWorkspaceEdit(documentEdits);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_IndependentlyConstructedValueEqualEdits_AreEqual()
	{
		var first = new TextWorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, "replacement")])]);
		var second = new TextWorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, "replacement")])]);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DifferentEdits_AreNotEqual()
	{
		var first = new TextWorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, "replacement")])]);
		var second = new TextWorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, "other")])]);

		Assert.AreNotEqual(first, second);
	}

	[TestMethod]
	public void HasChanges_OnlyNoOpEdits_IsFalse()
	{
		var workspaceEdit = new TextWorkspaceEdit(
		[
			new TextDocumentEdit("a.lua", [new TextEdit(default, string.Empty)])
		]);

		Assert.IsFalse(workspaceEdit.HasChanges);
	}

	[TestMethod]
	public void HasChanges_DeletionEdit_IsTrue()
	{
		var workspaceEdit = new TextWorkspaceEdit(
		[
			new TextDocumentEdit("a.lua",
			[
				new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 3)), string.Empty)
			])
		]);

		Assert.IsTrue(workspaceEdit.HasChanges);
	}
}
