namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextEditInputTests
{
	[TestMethod]
	public void IsNoOp_EmptyRangeWithEmptyText_IsTrue()
	{
		var input = new TextEditInput(new TextRange(4, 0), string.Empty);

		Assert.IsTrue(input.IsNoOp);
	}

	[TestMethod]
	public void IsNoOp_WithReplacementTextOrReplacedRange_IsFalse()
	{
		Assert.IsFalse(new TextEditInput(new TextRange(4, 0), "x").IsNoOp);
		Assert.IsFalse(new TextEditInput(new TextRange(4, 2), string.Empty).IsNoOp);
	}

	[TestMethod]
	public void IsNoOp_NullReplacementText_IsFalse()
	{
		// A null replacement text is invalid rather than a no-op, so the kernel reports it instead of
		// silently ignoring the input.
		var input = new TextEditInput(new TextRange(4, 0), null!);

		Assert.IsFalse(input.IsNoOp);
	}
}
