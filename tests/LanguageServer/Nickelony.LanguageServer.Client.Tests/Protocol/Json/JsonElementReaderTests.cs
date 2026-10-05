using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Pins the tolerant lookup contract of <see cref="JsonElementReader.TryGetProperty"/>: an exact name match wins over a differently-cased sibling,
/// a differently-cased name resolves through the fallback, and an absent property reports failure with a default value.
/// </summary>
[TestClass]
public sealed class JsonElementReaderTests
{
	[TestMethod]
	public void TryGetProperty_ExactNameMatch_WinsOverACaseInsensitiveSibling()
	{
		using var document = JsonDocument.Parse("""{ "name": "exact", "NAME": "upper" }""");

		Assert.IsTrue(JsonElementReader.TryGetProperty(document.RootElement, "name", out JsonElement value));
		Assert.AreEqual("exact", value.GetString());
	}

	[TestMethod]
	public void TryGetProperty_DifferentCasing_ResolvesThroughTheFallback()
	{
		using var document = JsonDocument.Parse("""{ "Name": "value" }""");

		Assert.IsTrue(JsonElementReader.TryGetProperty(document.RootElement, "name", out JsonElement value));
		Assert.AreEqual("value", value.GetString());
	}

	[TestMethod]
	public void TryGetProperty_AbsentProperty_ReportsFailure()
	{
		using var document = JsonDocument.Parse("""{ "other": 1 }""");

		Assert.IsFalse(JsonElementReader.TryGetProperty(document.RootElement, "name", out JsonElement value));
		Assert.AreEqual(JsonValueKind.Undefined, value.ValueKind);
	}

	[TestMethod]
	public void TryGetProperty_NonObjectElement_ReportsFailureWithoutThrowing()
	{
		using var arrayDocument = JsonDocument.Parse("[1, 2, 3]");
		using var stringDocument = JsonDocument.Parse("\"text\"");

		// A non-object element has no properties; the Try* contract promises a false result instead of an
		// InvalidOperationException from JsonElement.TryGetProperty or EnumerateObject.
		Assert.IsFalse(JsonElementReader.TryGetProperty(arrayDocument.RootElement, "name", out JsonElement arrayValue));
		Assert.AreEqual(JsonValueKind.Undefined, arrayValue.ValueKind);
		Assert.IsFalse(JsonElementReader.TryGetProperty(stringDocument.RootElement, "name", out JsonElement stringValue));
		Assert.AreEqual(JsonValueKind.Undefined, stringValue.ValueKind);
	}
}
