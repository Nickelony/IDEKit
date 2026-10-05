using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class WorkspaceEditTests
{
	[TestMethod]
	public void Constructor_NullElement_ThrowsArgumentException() => Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceEdit([null!]));

	[TestMethod]
	public void HasChanges_EmptyDocumentList_IsFalse()
	{
		var workspaceEdit = new WorkspaceEdit([]);
		Assert.IsFalse(workspaceEdit.HasChanges);
	}

	[TestMethod]
	public void HasChanges_DocumentsWithoutEdits_IsFalse()
	{
		var workspaceEdit = new WorkspaceEdit([new TextDocumentEdit("a.lua", []), new TextDocumentEdit("b.lua", [])]);
		Assert.IsFalse(workspaceEdit.HasChanges);
	}

	[TestMethod]
	public void HasChanges_AnyDocumentWithEdits_IsTrue()
	{
		var workspaceEdit = new WorkspaceEdit([
			new TextDocumentEdit("a.lua", []),
			new TextDocumentEdit("b.lua", [new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "replacement")]),
		]);

		Assert.IsTrue(workspaceEdit.HasChanges);
	}

	[TestMethod]
	public void Constructor_StoresOwnedDocumentAndTextEditSnapshots()
	{
		var textEdits = new List<TextEdit> { new(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "replacement") };

		var documentEdits = new List<TextDocumentEdit> { new("C:\\Workspace\\test.lua", textEdits) };

		var workspaceEdit = new WorkspaceEdit(documentEdits);

		textEdits.Clear();
		documentEdits.Clear();

		Assert.HasCount(1, workspaceEdit.DocumentEdits);
		Assert.HasCount(1, workspaceEdit.DocumentEdits[0].TextEdits);
		Assert.AreEqual("replacement", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void Equals_ValueEqualSnapshots_AreEqual()
	{
		var documentEdits = new List<TextDocumentEdit> { new("a.lua", [new TextEdit(default, "replacement")]) };

		var first = new WorkspaceEdit(documentEdits);
		var second = new WorkspaceEdit(documentEdits);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_IndependentlyConstructedValueEqualEdits_AreEqual()
	{
		var first = new WorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, "replacement")])]);
		var second = new WorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, "replacement")])]);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DifferentEdits_AreNotEqual()
	{
		var first = new WorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, "replacement")])]);
		var second = new WorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, "other")])]);

		Assert.AreNotEqual(first, second);
	}

	[TestMethod]
	public void HasChanges_OnlyNoOpEdits_IsFalse()
	{
		var workspaceEdit = new WorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, string.Empty)])]);
		Assert.IsFalse(workspaceEdit.HasChanges);
	}

	[TestMethod]
	public void HasChanges_DeletionEdit_IsTrue()
	{
		var workspaceEdit = new WorkspaceEdit([
			new TextDocumentEdit("a.lua", [new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 3)), string.Empty)]),
		]);

		Assert.IsTrue(workspaceEdit.HasChanges);
	}
}
