using System.Text.Json.Nodes;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class LanguageServerProfileHooksTests
{
	[TestMethod]
	public void Default_DescribesALanguageWithoutConfigurationFilesAndWithEmptyPayloads()
	{
		LanguageServerProfileHooks hooks = LanguageServerProfileHooks.Default;

		Assert.IsFalse(hooks.IsConfigurationPath("any.file"));
		Assert.IsEmpty(hooks.BuildSettings().AsObject());
		Assert.IsNotNull(hooks.BuildInitializationOptions());
		Assert.IsNull(hooks.BuildSessionStartNotifications);
	}

	[TestMethod]
	public void RequiredHooks_RejectNull()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new LanguageServerProfileHooks
			{
				IsConfigurationPath = null!,
				BuildSettings = static () => new JsonObject(),
				BuildInitializationOptions = static () => null,
			}
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new LanguageServerProfileHooks
			{
				IsConfigurationPath = static _ => false,
				BuildSettings = null!,
				BuildInitializationOptions = static () => null,
			}
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new LanguageServerProfileHooks
			{
				IsConfigurationPath = static _ => false,
				BuildSettings = static () => new JsonObject(),
				BuildInitializationOptions = null!,
			}
		);
	}

	[TestMethod]
	public void Settings_BuildAFreshDocumentOnEveryCall()
	{
		LanguageServerProfileHooks hooks = LanguageServerProfileHooks.Default;

		JsonNode first = hooks.BuildSettings();
		JsonNode second = hooks.BuildSettings();

		Assert.AreNotSame(first, second);
	}
}
