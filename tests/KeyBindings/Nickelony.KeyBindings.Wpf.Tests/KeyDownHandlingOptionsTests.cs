namespace Nickelony.KeyBindings.Wpf.Tests;

/// <summary>Pins the <see cref="KeyDownHandlingOptions"/> defaults: the auto-repeat and AltGr strokes are both filtered.</summary>
[TestClass]
public sealed class KeyDownHandlingOptionsTests
{
	[TestMethod]
	public void Default_IgnoresAutoRepeatAndAltGr()
	{
		Assert.IsTrue(KeyDownHandlingOptions.Default.IgnoreAutoRepeat);
		Assert.IsTrue(KeyDownHandlingOptions.Default.IgnoreAltGr);
	}
}
