using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Pins the documented defaults and validation of the code-action controller options.</summary>
[TestClass]
public sealed class CodeActionControllerOptionsTests
{
	[TestMethod]
	public void Default_PinsTheTwoHundredFiftyMillisecondRequestDebounce() =>
		Assert.AreEqual(TimeSpan.FromMilliseconds(250.0), CodeActionControllerOptions.Default.RequestDebounceDelay);

	[TestMethod]
	public void RequestDebounceDelay_Negative_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			CodeActionControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(-1.0),
			}
		);
	}

	[TestMethod]
	public void RequestDebounceDelay_Zero_IsAccepted()
	{
		CodeActionControllerOptions options = CodeActionControllerOptions.Default with { RequestDebounceDelay = TimeSpan.Zero };
		Assert.AreEqual(TimeSpan.Zero, options.RequestDebounceDelay);
	}
}
