namespace Nickelony.LanguageServer.Provider.Abstractions.Tests;

[TestClass]
public sealed class LanguageServerFeatureProviderCapabilitiesTests
{
	private static readonly LanguageServerFeatureProviderCapabilities s_everything = new()
	{
		SupportsHover = true,
		SupportsDefinition = true,
		SupportsSignatureHelp = true,
		SupportsReferences = true,
		SupportsRename = true,
	};

	[TestMethod]
	public void None_AdvertisesNoFeature()
	{
		LanguageServerFeatureProviderCapabilities none = LanguageServerFeatureProviderCapabilities.None;

		Assert.IsFalse(none.SupportsHover);
		Assert.IsFalse(none.SupportsDefinition);
		Assert.IsFalse(none.SupportsSignatureHelp);
		Assert.IsFalse(none.SupportsReferences);
		Assert.IsFalse(none.SupportsRename);
	}

	[TestMethod]
	public void None_IsOneSharedInstance()
	{
		LanguageServerFeatureProviderCapabilities first = LanguageServerFeatureProviderCapabilities.None;
		LanguageServerFeatureProviderCapabilities second = LanguageServerFeatureProviderCapabilities.None;

		Assert.AreSame(first, second);
	}

	[TestMethod]
	public void Equality_ComparesEveryFlag()
	{
		Assert.AreEqual(s_everything, s_everything with { });
		Assert.AreEqual(s_everything.GetHashCode(), (s_everything with { }).GetHashCode());

		Assert.AreNotEqual(s_everything, s_everything with { SupportsHover = false });
		Assert.AreNotEqual(s_everything, s_everything with { SupportsDefinition = false });
		Assert.AreNotEqual(s_everything, s_everything with { SupportsSignatureHelp = false });
		Assert.AreNotEqual(s_everything, s_everything with { SupportsReferences = false });
		Assert.AreNotEqual(s_everything, s_everything with { SupportsRename = false });
		Assert.AreNotEqual(s_everything, LanguageServerFeatureProviderCapabilities.None);
	}
}
