namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Verifies the <see cref="TextAutoClosingAction"/> factories.
/// </summary>
[TestClass]
public sealed class TextAutoClosingActionTests
{
	[TestMethod]
	public void ActionFactories_GuardArgumentsAndCarryKindAndClosingText()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextAutoClosingAction.CreateInsert(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => TextAutoClosingAction.CreateSkip(null!));

		TextAutoClosingAction insert = TextAutoClosingAction.CreateInsert(")");
		TextAutoClosingAction skip = TextAutoClosingAction.CreateSkip(",");

		Assert.AreEqual(TextAutoClosingActionKind.InsertClosingText, insert.Kind);
		Assert.AreEqual(")", insert.ClosingText);
		Assert.AreEqual(TextAutoClosingActionKind.SkipExistingClosingText, skip.Kind);
		Assert.AreEqual(",", skip.ClosingText);
	}
}
