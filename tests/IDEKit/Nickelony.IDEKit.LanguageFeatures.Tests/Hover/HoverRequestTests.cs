using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class HoverRequestTests
{
	private const string DocumentId = "doc.lua";

	[TestMethod]
	public void Request_StoresSnapshotAndOffset()
	{
		var request = new HoverRequest(DocumentId, "local value", 6);

		Assert.AreEqual("local value", request.DocumentText);
		Assert.AreEqual(6, request.HoveredOffset);
	}

	[TestMethod]
	public void Request_OffsetAtTextLength_IsAccepted()
	{
		var request = new HoverRequest(DocumentId, "abc", 3);
		Assert.AreEqual(3, request.HoveredOffset);
	}

	[TestMethod]
	public void Request_NegativeOffset_Throws() => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HoverRequest(DocumentId, "abc", -1));

	[TestMethod]
	public void Request_OffsetBeyondTextLength_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new HoverRequest(DocumentId, "abc", 4));

	[TestMethod]
	public void Request_NullDocumentText_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new HoverRequest(DocumentId, null!, 0));

	[TestMethod]
	public void Request_Equality_ComparesAllComponents()
	{
		var request = new HoverRequest(DocumentId, "local value", 6);

		Assert.AreEqual(request, new HoverRequest(DocumentId, "local value", 6));
		Assert.AreNotEqual(request, new HoverRequest(DocumentId, "local value", 5));
		Assert.AreNotEqual(request, new HoverRequest(DocumentId, "other value", 6));
	}
}
