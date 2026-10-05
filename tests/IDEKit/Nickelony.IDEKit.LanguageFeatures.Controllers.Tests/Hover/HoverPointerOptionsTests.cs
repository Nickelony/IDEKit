using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Pins the documented default and validation of the hover pointer options.</summary>
[TestClass]
public sealed class HoverPointerOptionsTests
{
	[TestMethod]
	public void Default_PinsTheFourHundredMillisecondRestDelay()
	{
		// The default is pinned so the option value and its XML doc cannot drift apart.
		Assert.AreEqual(TimeSpan.FromMilliseconds(400), HoverPointerOptions.Default.RestDelay);
	}

	[TestMethod]
	public void RestDelay_Null_IsKept() => Assert.IsNull((HoverPointerOptions.Default with { RestDelay = null }).RestDelay);

	[TestMethod]
	public void RestDelay_Zero_IsKept() => Assert.AreEqual(TimeSpan.Zero, (HoverPointerOptions.Default with { RestDelay = TimeSpan.Zero }).RestDelay);

	[TestMethod]
	public void RestDelay_Negative_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HoverPointerOptions.Default with { RestDelay = TimeSpan.FromMilliseconds(-1) });
}
