using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DocumentRequestDescriptorTests
{
	[TestMethod]
	public void Constructor_WithoutAnOffsetOrCapabilityGate_DescribesADocumentScopedRequestThatIsAlwaysAttempted()
	{
		var descriptor = new DocumentRequestDescriptor<string, int>(
			method: "textDocument/example",
			buildParameters: static (_, _) => new object(),
			parseResponse: static (response, _) => response.Length,
			fallbackValue: -1
		);

		Assert.AreEqual("textDocument/example", descriptor.Method);
		Assert.AreEqual(-1, descriptor.FallbackValue);
		Assert.IsNull(descriptor.Offset);
		Assert.IsNull(descriptor.SupportsRequest);
	}

	[TestMethod]
	public void Constructor_KeepsTheDelegatesAndTheCaretOffsetItWasGiven()
	{
		var buildParameters = new Func<TextDocumentIdentifier, PositionPayload, object>(static (document, position) => (document, position));
		var parseResponse = new Func<string, RequestedDocument, int>(static (response, _) => response.Length);
		var supportsRequest = new Func<ILanguageServerClient, bool>(static client => client.NegotiatedCapabilities.SupportsHover);

		var descriptor = new DocumentRequestDescriptor<string, int>(
			method: "textDocument/example",
			buildParameters,
			parseResponse,
			fallbackValue: -1,
			supportsRequest,
			offset: 37
		);

		Assert.AreSame(buildParameters, descriptor.BuildParameters);
		Assert.AreSame(parseResponse, descriptor.ParseResponse);
		Assert.AreSame(supportsRequest, descriptor.SupportsRequest);
		Assert.AreEqual(37, descriptor.Offset);
	}
}
