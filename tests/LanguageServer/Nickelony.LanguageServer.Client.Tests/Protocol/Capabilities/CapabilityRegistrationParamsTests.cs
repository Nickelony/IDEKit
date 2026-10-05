using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class CapabilityRegistrationParamsTests
{
	[TestMethod]
	public void Deserialize_ReadsTheRegistrationsProperty()
	{
		CapabilityRegistrationParams parameters = JsonSerializer.Deserialize<CapabilityRegistrationParams>(
			"""
			{
			  "registrations": [
			    { "id": "1", "method": "textDocument/rename" }
			  ]
			}
			"""
		);

		Assert.IsNotNull(parameters.Registrations);
		Assert.HasCount(1, parameters.Registrations);
		Assert.AreEqual("1", parameters.Registrations[0].Id);
		Assert.AreEqual("textDocument/rename", parameters.Registrations[0].Method);
	}
}
