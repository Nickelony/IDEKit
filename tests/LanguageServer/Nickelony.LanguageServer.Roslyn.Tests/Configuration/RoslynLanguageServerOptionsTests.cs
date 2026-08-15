namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>
/// Covers the Roslyn session option normalization.
/// </summary>
[TestClass]
public sealed class RoslynLanguageServerOptionsTests
{
	[TestMethod]
	public void Default_UsesTheServerDefaults()
	{
		RoslynLanguageServerOptions options = RoslynLanguageServerOptions.Default;

		Assert.IsNull(options.AutoLoadProjectsMaximum);
		Assert.IsTrue(options.ShowCompletionItemsFromUnimportedNamespaces);
	}

	[TestMethod]
	public void AutoLoadProjectsMaximum_RejectsANegativeValue()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = -1 });
	}

	[TestMethod]
	public void AutoLoadProjectsMaximum_AcceptsZeroAndPositiveValues()
	{
		Assert.AreEqual(0, new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 0 }.AutoLoadProjectsMaximum);
		Assert.AreEqual(500, new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 500 }.AutoLoadProjectsMaximum);
	}

	[TestMethod]
	public void Options_CompareByValue()
	{
		Assert.AreEqual(
			new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 10 },
			new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 10 });
		Assert.AreNotEqual(
			new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 10 },
			new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 11 });
	}
}
