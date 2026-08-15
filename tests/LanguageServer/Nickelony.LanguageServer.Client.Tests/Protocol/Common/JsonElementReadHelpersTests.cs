using Nickelony.LanguageServer.Protocol;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Pins the tolerant lookup contract of <see cref="JsonElementReadHelpers.TryGetProperty"/>: an exact name
/// match wins over a differently-cased sibling, a differently-cased name resolves through the fallback, and
/// an absent property reports failure with a default value.
/// </summary>
[TestClass]
public sealed class JsonElementReadHelpersTests
{
	[TestMethod]
	public void TryGetProperty_ExactNameMatch_WinsOverACaseInsensitiveSibling()
	{
		using JsonDocument document = JsonDocument.Parse("""{ "name": "exact", "NAME": "upper" }""");

		Assert.IsTrue(JsonElementReadHelpers.TryGetProperty(document.RootElement, "name", out JsonElement value));
		Assert.AreEqual("exact", value.GetString());
	}

	[TestMethod]
	public void TryGetProperty_DifferentCasing_ResolvesThroughTheFallback()
	{
		using JsonDocument document = JsonDocument.Parse("""{ "Name": "value" }""");

		Assert.IsTrue(JsonElementReadHelpers.TryGetProperty(document.RootElement, "name", out JsonElement value));
		Assert.AreEqual("value", value.GetString());
	}

	[TestMethod]
	public void TryGetProperty_AbsentProperty_ReportsFailure()
	{
		using JsonDocument document = JsonDocument.Parse("""{ "other": 1 }""");

		Assert.IsFalse(JsonElementReadHelpers.TryGetProperty(document.RootElement, "name", out JsonElement value));
		Assert.AreEqual(JsonValueKind.Undefined, value.ValueKind);
	}

	[TestMethod]
	public void TryGetProperty_NonObjectElement_ReportsFailureWithoutThrowing()
	{
		using JsonDocument arrayDocument = JsonDocument.Parse("[1, 2, 3]");
		using JsonDocument stringDocument = JsonDocument.Parse("\"text\"");

		// A non-object element has no properties; the Try* contract promises a false result instead of an
		// InvalidOperationException from JsonElement.TryGetProperty or EnumerateObject.
		Assert.IsFalse(JsonElementReadHelpers.TryGetProperty(arrayDocument.RootElement, "name", out JsonElement arrayValue));
		Assert.AreEqual(JsonValueKind.Undefined, arrayValue.ValueKind);
		Assert.IsFalse(JsonElementReadHelpers.TryGetProperty(stringDocument.RootElement, "name", out JsonElement stringValue));
		Assert.AreEqual(JsonValueKind.Undefined, stringValue.ValueKind);
	}
}
