using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class TextDocumentEditTests
{
	[TestMethod]
	public void Constructor_NullElement_ThrowsArgumentException() =>
		Assert.ThrowsExactly<ArgumentException>(() => new TextDocumentEdit("a.lua", [null!]));

	[TestMethod]
	public void Constructor_StoresOwnedEditSnapshot()
	{
		var textEdits = new List<TextEdit> { new(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "replacement") };

		var documentEdit = new TextDocumentEdit("doc.lua", textEdits);

		textEdits.Clear();

		Assert.AreEqual("doc.lua", documentEdit.DocumentId);
		Assert.HasCount(1, documentEdit.TextEdits);
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
