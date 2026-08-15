using Nickelony.IDEKit.IntelliSense;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers the interop seam between the client's <c>HoverResponse</c> payload and the provider's
/// <see cref="MarkupContentReader"/>: the hover contents member is read through the same reader the
/// provider uses for its own markup payloads. The client payload's own deserialization is covered by the
/// client suite, so only the reader interop lives here.
/// </summary>
[TestClass]
public sealed class HoverResponseMarkupContentInteropTests
{
	[TestMethod]
	public void Deserialize_MarkupObjectContents_IsReadableThroughMarkupContentReader()
	{
		HoverResponse? response = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{
			  "contents": { "kind": "markdown", "value": "**bold**" }
			}
			""");

		Assert.IsNotNull(response);

		ProtocolMarkupContent content = MarkupContentReader.ExtractContent(response.Contents!.Value);

		Assert.AreEqual(TextMarkupKind.Markdown, content.Kind);
		Assert.AreEqual("**bold**", content.Text);
	}

	[TestMethod]
	public void Deserialize_MarkupArrayContents_CombinesEntries()
	{
		HoverResponse? response = JsonSerializer.Deserialize<HoverResponse>(
			"""
			{
			  "contents": [
			    "Summary",
			    { "kind": "markdown", "value": "Details" }
			  ]
			}
			""");

		Assert.IsNotNull(response);

		ProtocolMarkupContent content = MarkupContentReader.ExtractContent(response.Contents!.Value);

		Assert.AreEqual("Summary\n\nDetails", content.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
	}
}
