using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class TextDocumentEditTests
{
	[TestMethod]
	public void Constructor_StoresOwnedEditSnapshot()
	{
		var textEdits = new List<TextEdit>
		{
			new(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "replacement")
		};

		var documentEdit = new TextDocumentEdit("doc.lua", textEdits);

		textEdits.Clear();

		Assert.AreEqual("doc.lua", documentEdit.FilePath);
		Assert.AreEqual(1, documentEdit.TextEdits.Count);
		Assert.AreEqual("replacement", documentEdit.TextEdits[0].NewText);
	}

	[TestMethod]
	public void Equals_ValueEqualDistinctSnapshots_AreEqual()
	{
		var first = new TextDocumentEdit("doc.lua", [new TextEdit(default, "replacement")]);
		var second = new TextDocumentEdit("doc.lua", [new TextEdit(default, "replacement")]);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DifferentEdits_AreNotEqual()
	{
		var first = new TextDocumentEdit("doc.lua", [new TextEdit(default, "replacement")]);
		var second = new TextDocumentEdit("doc.lua", [new TextEdit(default, "other")]);

		Assert.AreNotEqual(first, second);
	}
}
