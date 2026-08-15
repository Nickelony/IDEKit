using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions.Tests;

[TestClass]
public sealed class TextCodeActionTests
{
	[TestMethod]
	public void Constructor_StoresTitleKindPreferredAndEdit()
	{
		var edit = new TextWorkspaceEdit(
		[
			new TextDocumentEdit("a.lua",
			[
				new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "x")
			])
		]);

		var action = new TextCodeAction("Fix it", edit, "quickfix", isPreferred: true);

		Assert.AreEqual("Fix it", action.Title);
		Assert.AreEqual("quickfix", action.Kind);
		Assert.IsTrue(action.IsPreferred);
		Assert.AreSame(edit, action.Edit);
	}

	[TestMethod]
	public void Constructor_OptionalMetadataDefaults_AreNullAndFalse()
	{
		var action = new TextCodeAction("Fix it", new TextWorkspaceEdit([]));

		Assert.IsNull(action.Kind);
		Assert.IsFalse(action.IsPreferred);
	}

	[TestMethod]
	public void Constructor_NotPreferred_IsStored()
	{
		var action = new TextCodeAction("Fix it", new TextWorkspaceEdit([]), "quickfix", isPreferred: false);

		Assert.IsFalse(action.IsPreferred);
	}

	[TestMethod]
	public void Constructor_NullKind_IsStoredAsNull()
	{
		var action = new TextCodeAction("Fix it", new TextWorkspaceEdit([]), null);

		Assert.IsNull(action.Kind);
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty kind becomes null")]
	[DataRow("   ", DisplayName = "Blank kind becomes null")]
	public void Constructor_BlankKind_IsStoredAsNull(string kind)
	{
		var action = new TextCodeAction("Fix it", new TextWorkspaceEdit([]), kind);

		Assert.IsNull(action.Kind);
	}

	[TestMethod]
	public void Constructor_PaddedKind_IsTrimmed()
	{
		var action = new TextCodeAction("Fix it", new TextWorkspaceEdit([]), " quickfix ");

		Assert.AreEqual("quickfix", action.Kind);
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		var edit = new TextWorkspaceEdit([]);

		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeAction(null!, edit, "quickfix"));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeAction("Fix it", null!, "quickfix"));
	}

	[TestMethod]
	public void Equals_SameEditReference_AreEqual()
	{
		var edit = new TextWorkspaceEdit([]);
		var first = new TextCodeAction("Fix it", edit, "quickfix", isPreferred: true);
		var second = new TextCodeAction("Fix it", edit, "quickfix", isPreferred: true);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_ValueEqualDistinctInstances_AreEqual()
	{
		var first = new TextCodeAction("Fix it",
			new TextWorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, "replacement")])]), "quickfix", isPreferred: true);
		var second = new TextCodeAction("Fix it",
			new TextWorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, "replacement")])]), "quickfix", isPreferred: true);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DifferentPreferredFlag_AreNotEqual()
	{
		var edit = new TextWorkspaceEdit([]);
		var preferred = new TextCodeAction("Fix it", edit, "quickfix", isPreferred: true);
		var regular = new TextCodeAction("Fix it", edit, "quickfix", isPreferred: false);

		Assert.AreNotEqual(preferred, regular);
	}
}
