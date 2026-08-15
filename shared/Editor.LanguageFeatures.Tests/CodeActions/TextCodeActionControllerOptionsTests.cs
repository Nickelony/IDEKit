#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Pins the documented defaults and validation of the code-action controller options.
/// </summary>
[TestClass]
public sealed class TextCodeActionControllerOptionsTests
{
	[TestMethod]
	public void Default_PinsTheTwoHundredFiftyMillisecondRequestDebounce()
		=> Assert.AreEqual(TimeSpan.FromMilliseconds(250.0), TextCodeActionControllerOptions.Default.RequestDebounceDelay);

	[TestMethod]
	public void RequestDebounceDelay_Negative_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCodeActionControllerOptions.Default with { RequestDebounceDelay = TimeSpan.FromMilliseconds(-1.0) });

	[TestMethod]
	public void RequestDebounceDelay_Zero_IsAccepted()
	{
		TextCodeActionControllerOptions options = TextCodeActionControllerOptions.Default with
		{
			RequestDebounceDelay = TimeSpan.Zero
		};

		Assert.AreEqual(TimeSpan.Zero, options.RequestDebounceDelay);
	}
}
