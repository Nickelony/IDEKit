using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Editing;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class CodeActionTests
{
	[TestMethod]
	public void Constructor_StoresTitleKindPreferredAndEdit()
	{
		var edit = new WorkspaceEdit([
			new TextDocumentEdit("a.lua", [new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)), "x")]),
		]);

		var action = new CodeAction(title: "Fix it", edit, kind: "quickfix", isPreferred: true);

		Assert.AreEqual("Fix it", action.Title);
		Assert.AreEqual("quickfix", action.Kind);
		Assert.IsTrue(action.IsPreferred);
		Assert.AreSame(edit, action.Edit);
	}

	[TestMethod]
	public void Constructor_OptionalMetadataDefaults_AreNullAndFalse()
	{
		var action = new CodeAction("Fix it", new WorkspaceEdit([]));

		Assert.IsNull(action.Kind);
		Assert.IsFalse(action.IsPreferred);
	}

	[TestMethod]
	public void Constructor_NotPreferred_IsStored()
	{
		var action = new CodeAction(title: "Fix it", edit: new WorkspaceEdit([]), kind: "quickfix", isPreferred: false);
		Assert.IsFalse(action.IsPreferred);
	}

	[TestMethod]
	public void Constructor_NullKind_IsStoredAsNull()
	{
		var action = new CodeAction("Fix it", new WorkspaceEdit([]), null);
		Assert.IsNull(action.Kind);
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty kind becomes null")]
	[DataRow("   ", DisplayName = "Blank kind becomes null")]
	public void Constructor_BlankKind_IsStoredAsNull(string kind)
	{
		var action = new CodeAction("Fix it", new WorkspaceEdit([]), kind);
		Assert.IsNull(action.Kind);
	}

	[TestMethod]
	public void Constructor_PaddedKind_IsTrimmed()
	{
		var action = new CodeAction("Fix it", new WorkspaceEdit([]), " quickfix ");
		Assert.AreEqual("quickfix", action.Kind);
	}

	[TestMethod]
	public void Constructor_NullTitle_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new CodeAction(null!, new WorkspaceEdit([]), "quickfix"));

	[TestMethod]
	public void Constructor_EditAndDataAreOptional()
	{
		var action = new CodeAction("Fix it");

		Assert.IsNull(action.Edit);
		Assert.IsNull(action.Data);
		Assert.IsNull(action.Kind);
		Assert.IsFalse(action.IsPreferred);
	}

	[TestMethod]
	public void Constructor_StoresTheData()
	{
		object data = new();

		var action = new CodeAction(title: "Fix it", kind: "quickfix", isPreferred: true, data: data);

		Assert.AreEqual("quickfix", action.Kind);
		Assert.IsTrue(action.IsPreferred);
		Assert.AreSame(data, action.Data);
		Assert.IsNull(action.Edit);
	}

	[TestMethod]
	public void Equals_ComparesTheDataByEquality()
	{
		object data = new();

		var action = new CodeAction(title: "Fix it", data: data);

		Assert.AreEqual(action, new CodeAction(title: "Fix it", data: data));
		Assert.AreNotEqual(action, new CodeAction(title: "Fix it", data: new object()));
		Assert.AreNotEqual(action, new CodeAction(title: "Fix it"));
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty title is rejected")]
	[DataRow("   ", DisplayName = "Blank title is rejected")]
	public void Constructor_BlankTitle_ThrowsArgumentException(string title) =>
		Assert.ThrowsExactly<ArgumentException>(() => new CodeAction(title, new WorkspaceEdit([])));

	[TestMethod]
	public void Equals_SameEditReference_AreEqual()
	{
		var edit = new WorkspaceEdit([]);
		var first = new CodeAction(title: "Fix it", edit, kind: "quickfix", isPreferred: true);
		var second = new CodeAction(title: "Fix it", edit, kind: "quickfix", isPreferred: true);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_ValueEqualDistinctInstances_AreEqual()
	{
		var first = new CodeAction(
			title: "Fix it",
			edit: new WorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, "replacement")])]),
			kind: "quickfix",
			isPreferred: true
		);

		var second = new CodeAction(
			title: "Fix it",
			edit: new WorkspaceEdit([new TextDocumentEdit("a.lua", [new TextEdit(default, "replacement")])]),
			kind: "quickfix",
			isPreferred: true
		);

		Assert.AreEqual(first, second);
		Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
	}

	[TestMethod]
	public void Equals_DifferentPreferredFlag_AreNotEqual()
	{
		var edit = new WorkspaceEdit([]);
		var preferred = new CodeAction(title: "Fix it", edit, kind: "quickfix", isPreferred: true);
		var regular = new CodeAction(title: "Fix it", edit, kind: "quickfix", isPreferred: false);

		Assert.AreNotEqual(preferred, regular);
	}

	[TestMethod]
	public void Constructor_CommandIsOptional_AndStoredWhenGiven()
	{
		var command = new CodeActionCommand("Remove spaces", "lua.removeSpace", ["file:///a.lua"]);

		Assert.IsNull(new CodeAction("Fix it").Command);
		Assert.AreSame(command, new CodeAction("Fix it", command: command).Command);
	}

	[TestMethod]
	public void Equals_ComparesTheCommandByValue()
	{
		var first = new CodeAction("Fix it", command: new CodeActionCommand("Remove spaces", "lua.removeSpace", ["file:///a.lua"]));
		var second = new CodeAction("Fix it", command: new CodeActionCommand("Remove spaces", "lua.removeSpace", ["file:///a.lua"]));

		Assert.AreEqual(first, second);
		Assert.AreNotEqual(first, new CodeAction("Fix it", command: new CodeActionCommand("Remove spaces", "lua.removeSpace", ["file:///b.lua"])));
		Assert.AreNotEqual(first, new CodeAction("Fix it"));
	}
}
