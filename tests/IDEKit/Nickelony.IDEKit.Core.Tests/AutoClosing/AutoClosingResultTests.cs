namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Pins the no-action value of <see cref="AutoClosingResult"/>.</summary>
[TestClass]
public sealed class AutoClosingResultTests
{
	[TestMethod]
	public void None_ReportsNoAction()
	{
		AutoClosingResult none = AutoClosingResult.None;

		Assert.AreEqual(AutoClosingActionKind.None, none.Action.Kind);
		Assert.IsNull(none.Action.ClosingText);
		Assert.IsFalse(none.DidWrapSelection);
	}
}
