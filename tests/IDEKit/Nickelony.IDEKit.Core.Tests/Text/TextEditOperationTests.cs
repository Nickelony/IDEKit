namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextEditOperationTests
{
	[TestMethod]
	public void Constructor_NullReplacementText_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextEditOperation(0, 1, null!, 0));
	}

	[TestMethod]
	public void Constructor_NegativeStartOffset_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextEditOperation(-1, 2, "x", 0));
	}

	[TestMethod]
	public void Constructor_EndOffsetBeforeStartOffset_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextEditOperation(5, 2, "x", 0));
	}

	[TestMethod]
	public void Deconstruct_ReturnsConstructorValues()
	{
		var operation = new TextEditOperation(2, 5, "new", 7);

		(int startOffset, int endOffset, string newText, int editIndex) = operation;

		Assert.AreEqual(2, startOffset);
		Assert.AreEqual(5, endOffset);
		Assert.AreEqual("new", newText);
		Assert.AreEqual(7, editIndex);
		Assert.AreEqual(3, operation.Length);
	}

	[TestMethod]
	public void IsNoOp_EmptyRangeWithEmptyText_IsTrue()
	{
		Assert.IsTrue(new TextEditOperation(2, 2, string.Empty, 0).IsNoOp);
	}

	[TestMethod]
	public void IsNoOp_DeletionOrInsertionOrReplacement_IsFalse()
	{
		Assert.IsFalse(new TextEditOperation(2, 4, string.Empty, 0).IsNoOp);   // deletion
		Assert.IsFalse(new TextEditOperation(2, 2, "x", 0).IsNoOp);           // insertion
		Assert.IsFalse(new TextEditOperation(2, 4, "xy", 0).IsNoOp);          // same-length replacement
	}
}
