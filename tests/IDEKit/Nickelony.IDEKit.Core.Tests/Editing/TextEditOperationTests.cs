namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextEditOperationTests
{
	[TestMethod]
	public void Constructor_NullReplacementText_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextEditOperation(range: TextRange.FromBounds(0, 1), newText: null!, editIndex: 0));

	[TestMethod]
	public void Deconstruct_ReturnsConstructorValues()
	{
		var operation = new TextEditOperation(range: TextRange.FromBounds(2, 5), newText: "new", editIndex: 7);

		(TextRange range, string newText, int editIndex) = operation;

		Assert.AreEqual(new TextRange(2, 3), range);
		Assert.AreEqual("new", newText);
		Assert.AreEqual(7, editIndex);
	}

	[TestMethod]
	public void IsNoOp_EmptyRangeWithEmptyText_IsTrue() =>
		Assert.IsTrue(new TextEditOperation(range: TextRange.FromBounds(2, 2), newText: string.Empty, editIndex: 0).IsNoOp);

	[TestMethod]
	public void IsNoOp_DeletionOrInsertionOrReplacement_IsFalse()
	{
		Assert.IsFalse(new TextEditOperation(range: TextRange.FromBounds(2, 4), newText: string.Empty, editIndex: 0).IsNoOp); // deletion
		Assert.IsFalse(new TextEditOperation(range: TextRange.FromBounds(2, 2), newText: "x", editIndex: 0).IsNoOp); // insertion
		Assert.IsFalse(new TextEditOperation(range: TextRange.FromBounds(2, 4), newText: "xy", editIndex: 0).IsNoOp); // same-length replacement
	}
}
