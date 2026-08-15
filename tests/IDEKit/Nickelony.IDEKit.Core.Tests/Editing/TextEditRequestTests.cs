namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Verifies the range validation and caret default of the edit-request record.
/// </summary>
[TestClass]
public sealed class TextEditRequestTests
{
	[TestMethod]
	public void Constructor_StoresRangeAndText()
	{
		var request = new TextEditRequest(2, 3, "x");

		Assert.AreEqual(2, request.StartOffset);
		Assert.AreEqual(3, request.Length);
		Assert.AreEqual("x", request.NewText);
		Assert.IsNull(request.CaretOffsetAfterEdit);
	}

	[TestMethod]
	public void CaretOffsetAfterEdit_AcceptsAValueThroughAnInitializer()
	{
		var request = new TextEditRequest(0, 0, "x") { CaretOffsetAfterEdit = 1 };

		Assert.AreEqual(1, request.CaretOffsetAfterEdit);
	}

	[TestMethod]
	public void Constructor_NullText_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextEditRequest(0, 0, null!));
	}

	[TestMethod]
	public void Constructor_NegativeStartOffsetOrLength_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextEditRequest(-1, 0, "x"));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextEditRequest(0, -1, "x"));
	}

	[TestMethod]
	public void Constructor_RangeEndOverflow_ThrowsForStartOffset()
	{
		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextEditRequest(int.MaxValue, 1, "x"));

		Assert.AreEqual("startOffset", exception.ParamName);
	}
}
