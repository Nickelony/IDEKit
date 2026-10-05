namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Verifies the <see cref="AutoClosingAction"/> factories.</summary>
[TestClass]
public sealed class AutoClosingActionTests
{
	[TestMethod]
	public void ActionFactories_GuardArgumentsAndCarryKindAndClosingText()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => AutoClosingAction.CreateInsert(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => AutoClosingAction.CreateSkip(null!));

		var insert = AutoClosingAction.CreateInsert(")");
		var skip = AutoClosingAction.CreateSkip(",");

		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, insert.Kind);
		Assert.AreEqual(")", insert.ClosingText);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, skip.Kind);
		Assert.AreEqual(",", skip.ClosingText);
	}
}
