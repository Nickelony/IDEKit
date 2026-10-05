namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Verifies the storage and caret default of the edit-request record.</summary>
[TestClass]
public sealed class TextEditRequestTests
{
	[TestMethod]
	public void Constructor_StoresRangeAndText()
	{
		var request = new TextEditRequest(new TextRange(2, 3), "x");

		Assert.AreEqual(new TextRange(2, 3), request.Range);
		Assert.AreEqual("x", request.NewText);
		Assert.IsNull(request.CaretOffsetAfterEdit);
	}

	[TestMethod]
	public void CaretOffsetAfterEdit_AcceptsAValueThroughAnInitializer()
	{
		var request = new TextEditRequest(new TextRange(0, 0), "x") { CaretOffsetAfterEdit = 1 };
		Assert.AreEqual(1, request.CaretOffsetAfterEdit);
	}

	[TestMethod]
	public void Constructor_NullText_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new TextEditRequest(new TextRange(0, 0), null!));
}
