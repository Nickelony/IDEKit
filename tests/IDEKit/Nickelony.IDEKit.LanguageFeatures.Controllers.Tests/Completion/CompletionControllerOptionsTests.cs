using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Pins the documented defaults and validation of the completion controller options.</summary>
[TestClass]
public sealed class CompletionControllerOptionsTests
{
	[TestMethod]
	public void Default_PinsTheDocumentedValues()
	{
		// The defaults are pinned so the option values and their XML docs cannot drift apart.
		CompletionControllerOptions options = CompletionControllerOptions.Default;

		Assert.AreEqual(TimeSpan.FromMilliseconds(120.0), options.RequestDebounceDelay);
		Assert.AreSame(CompletionTriggerPolicy.Default, options.Triggers);
	}

	[TestMethod]
	public void Triggers_Null_Throws()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			CompletionControllerOptions.Default with
			{
				Triggers = null!,
			}
		);

		Assert.AreEqual("Triggers", exception.ParamName);
	}

	[TestMethod]
	public void RequestDebounceDelay_Negative_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			CompletionControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(-1.0),
			}
		);

		Assert.AreEqual("RequestDebounceDelay", exception.ParamName);
	}

	[TestMethod]
	public void RequestDebounceDelay_Zero_IsAccepted()
	{
		CompletionControllerOptions options = CompletionControllerOptions.Default with { RequestDebounceDelay = TimeSpan.Zero };
		Assert.AreEqual(TimeSpan.Zero, options.RequestDebounceDelay);
	}
}
