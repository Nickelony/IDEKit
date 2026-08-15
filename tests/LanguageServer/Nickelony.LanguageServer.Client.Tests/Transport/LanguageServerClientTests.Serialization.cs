using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	public void CreateProtocolSerializerOptions_UsesLowerCamelMemberNames()
	{
		JsonSerializerOptions options = LanguageServerClient.CreateProtocolSerializerOptions();

		Assert.AreEqual(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
		Assert.AreEqual("{\"triggerCharacter\":\"x\"}", JsonSerializer.Serialize(new { TriggerCharacter = "x" }, options));
	}

	[TestMethod]
	public void CreateProtocolSerializerOptions_ReturnsANewInstancePerCall()
	{
		Assert.AreNotSame(
			LanguageServerClient.CreateProtocolSerializerOptions(),
			LanguageServerClient.CreateProtocolSerializerOptions());
	}
}
