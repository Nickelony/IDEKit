namespace Nickelony.QuickPick.Tests;

[TestClass]
public sealed class QuickPickModeTests
{
	[TestMethod]
	public void Constructor_ProviderAlone_LeavesThePrefixToTheProvider()
	{
		var provider = new ScriptedQuickPickProvider(">");
		var mode = new QuickPickMode(provider);

		Assert.AreSame(provider, mode.Provider);
		Assert.IsNull(mode.Prefix);
	}

	[TestMethod]
	public void Constructor_AnEmptyPrefix_IsKeptAsTheDefaultModePrefix() =>
		Assert.AreEqual(string.Empty, new QuickPickMode(new ScriptedQuickPickProvider(">"), prefix: string.Empty).Prefix);

	[TestMethod]
	public void Constructor_NullProvider_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickMode(null!));
}
