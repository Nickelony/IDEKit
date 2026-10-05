using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class JsonElementJsonConverterTests
{
	[TestMethod]
	public void Serialize_UndefinedElement_WithConverter_WritesJsonNullInsteadOfThrowing()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new JsonElementJsonConverter());

		JsonElement element = JsonSerializer.SerializeToElement(new { data = default(JsonElement) }, options);

		Assert.AreEqual(JsonValueKind.Null, element.GetProperty("data").ValueKind);
	}

	[TestMethod]
	public void Serialize_CodeActionWithUndefinedData_WritesJsonNull()
	{
		// The member-level converter keeps a host-assigned default(JsonElement) from faulting the serializer even
		// when the host serializes with its own options.
		var action = new CodeActionPayload { Title = "fix", Data = default(JsonElement) };

		JsonElement element = JsonSerializer.SerializeToElement(action);

		Assert.AreEqual(JsonValueKind.Null, element.GetProperty("data").ValueKind);
	}

	[TestMethod]
	public void Serialize_DefinedElement_WritesTheContent()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new JsonElementJsonConverter());

		JsonElement element = JsonSerializer.SerializeToElement(new { data = JsonSerializer.SerializeToElement(new { a = 1 }) }, options);

		Assert.AreEqual(1, element.GetProperty("data").GetProperty("a").GetInt32());
	}
}
