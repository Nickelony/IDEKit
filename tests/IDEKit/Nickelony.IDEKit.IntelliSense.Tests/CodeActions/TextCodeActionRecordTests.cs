using Nickelony.IDEKit.IntelliSense.CodeActions;

namespace Nickelony.IDEKit.IntelliSense.Tests.CodeActions;

/// <summary>
/// Verifies the code-action request and presentation records: storage, equality, and construction
/// guards.
/// </summary>
[TestClass]
public sealed class TextCodeActionRecordTests
{
	[TestMethod]
	public void Item_StoresEveryComponent()
	{
		var payload = new object();
		var item = new TextCodeActionItem("Fix it", "quickfix", isPreferred: true, payload);

		Assert.AreEqual("Fix it", item.Title);
		Assert.AreEqual("quickfix", item.Kind);
		Assert.IsTrue(item.IsPreferred);
		Assert.AreSame(payload, item.Payload);
	}

	[TestMethod]
	public void Item_NullTitle_Throws()
		=> Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionItem(null!, null, false));

	[TestMethod]
	public void Item_BlankTitle_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TextCodeActionItem("   ", null, false));

	[TestMethod]
	public void Item_NullKindAndPayload_AreAllowed()
	{
		var item = new TextCodeActionItem("Fix it", null, false);

		Assert.IsNull(item.Kind);
		Assert.IsNull(item.Payload);
	}

	[TestMethod]
	public void Item_BlankKind_IsNormalizedAndDefaultsAreOptional()
	{
		var item = new TextCodeActionItem("Fix it", "   ");

		Assert.IsNull(item.Kind);
		Assert.IsFalse(item.IsPreferred);
		Assert.IsNull(item.Payload);

		var trimmed = new TextCodeActionItem("Fix it", "  quickfix  ");

		Assert.AreEqual("quickfix", trimmed.Kind);
	}

	[TestMethod]
	public void Item_Equality_ComparesEveryComponent()
	{
		var item = new TextCodeActionItem("Fix it", "quickfix", true);

		Assert.AreEqual(item, new TextCodeActionItem("Fix it", "quickfix", true));
		Assert.AreNotEqual(item, new TextCodeActionItem("Other", "quickfix", true));
		Assert.AreNotEqual(item, new TextCodeActionItem("Fix it", "refactor", true));
		Assert.AreNotEqual(item, new TextCodeActionItem("Fix it", "quickfix", false));
		Assert.AreNotEqual(item, new TextCodeActionItem("Fix it", "quickfix", true, new object()));
	}

	[TestMethod]
	public void Request_StoresDocumentAndRange()
	{
		var request = new TextCodeActionRequest("text", 1, 3);

		Assert.AreEqual("text", request.DocumentText);
		Assert.AreEqual(1, request.StartOffset);
		Assert.AreEqual(3, request.EndOffset);
	}

	[TestMethod]
	public void Request_Equality_ComparesEveryComponent()
	{
		var request = new TextCodeActionRequest("text", 1, 3);

		Assert.AreEqual(request, new TextCodeActionRequest("text", 1, 3));
		Assert.AreNotEqual(request, new TextCodeActionRequest("other", 1, 3));
		Assert.AreNotEqual(request, new TextCodeActionRequest("text", 0, 3));
		Assert.AreNotEqual(request, new TextCodeActionRequest("text", 1, 4));
	}

	[TestMethod]
	public void Request_InvalidRange_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionRequest(null!, 0, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCodeActionRequest("text", 3, 2));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCodeActionRequest("text", 0, 5));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCodeActionRequest("text", -1, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCodeActionRequest("text", 0, -1));
	}

	[TestMethod]
	public void Request_OffsetsAtTheDocumentEnd_AreAllowed()
	{
		// An offset equal to the document length is a valid caret position, and the empty document only
		// admits the zero range.
		var atEnd = new TextCodeActionRequest("text", 4, 4);

		Assert.AreEqual(4, atEnd.StartOffset);
		Assert.AreEqual(4, atEnd.EndOffset);

		var empty = new TextCodeActionRequest(string.Empty, 0, 0);

		Assert.AreEqual(0, empty.EndOffset);
	}
}
