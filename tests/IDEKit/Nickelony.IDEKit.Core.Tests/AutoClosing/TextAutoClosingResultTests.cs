namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the streaming contract of <see cref="TextAutoClosingResult"/>.
/// </summary>
[TestClass]
public sealed class TextAutoClosingResultTests
{
	[TestMethod]
	public void None_ReportsNoAction()
	{
		TextAutoClosingResult none = TextAutoClosingResult.None;

		Assert.AreEqual(TextAutoClosingActionKind.None, none.Action.Kind);
		Assert.IsNull(none.Action.ClosingText);
		Assert.IsFalse(none.DidWrapSelection);
	}
}
