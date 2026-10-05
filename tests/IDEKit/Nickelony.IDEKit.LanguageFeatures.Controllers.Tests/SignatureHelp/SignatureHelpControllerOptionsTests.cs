using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Pins the documented defaults and validation of the signature help controller options.</summary>
[TestClass]
public sealed class SignatureHelpControllerOptionsTests
{
	[TestMethod]
	public void Default_PinsTheFiftyMillisecondRequestDebounce()
	{
		// The default is pinned so the option value and its XML doc cannot drift apart.
		Assert.AreEqual(TimeSpan.FromMilliseconds(50.0), SignatureHelpControllerOptions.Default.RequestDebounceDelay);
	}

	[TestMethod]
	public void RequestDebounceDelay_Negative_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(-1.0),
			}
		);
	}

	[TestMethod]
	public void Default_PinsCyclicOverloadNavigation() => Assert.IsTrue(SignatureHelpControllerOptions.Default.Cycle);
}
