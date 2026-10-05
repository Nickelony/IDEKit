using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using Nickelony.IDEKit.LanguageFeatures.Rename;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the rename request through the framework pipeline.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderRenameTests
{
	[TestMethod]
	public async Task RenameSymbolAsync_WhenUnsupported_ReturnsNullAndSendsNoRename()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsRename = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		WorkspaceEdit? workspaceEdit = await provider.GetRenameEditsAsync(
			new RenameRequest(documentId: filePath, documentText: "local value = 1", symbolOffset: 6, newName: "renamed"),
			TestContext.CancellationToken
		);

		// The capability gate runs before document synchronization, so an unsupported request
		// produces no traffic at all.
		Assert.IsNull(workspaceEdit);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task RenameSymbolAsync_WhitespaceNewName_ReturnsNullWithoutSendingAnything()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		WorkspaceEdit? workspaceEdit = await provider.GetRenameEditsAsync(
			new RenameRequest(documentId: filePath, documentText: "local value = 1", symbolOffset: 6, newName: "   "),
			TestContext.CancellationToken
		);

		Assert.IsNull(workspaceEdit);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task RenameSymbolAsync_UnnormalizablePath_ReturnsNullWithoutSendingAnything()
	{
		string workspaceRoot = TestPaths.Root;

		// An embedded NUL is not a valid path character on any platform.
		string filePath = TestPaths.InvalidFile;

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		WorkspaceEdit? workspaceEdit = await provider.GetRenameEditsAsync(
			new RenameRequest(documentId: filePath, documentText: "local value = 1", symbolOffset: 6, newName: "renamed"),
			TestContext.CancellationToken
		);

		Assert.IsNull(workspaceEdit);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task RenameSymbolAsync_ReturnsWorkspaceEditFromTypedResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = target\r\nprint(target)";
		string secondPath = TestPaths.Script("other.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsRename = true,

			RenameResponse = JsonSerializer.SerializeToElement(
				new
				{
					changes = new Dictionary<string, object[]>
					{
						[new Uri(filePath).AbsoluteUri] =
						[
							new
							{
								range = new { start = new { line = 0, character = 14 }, end = new { line = 0, character = 20 } },
								newText = "renamed",
							},
						],
					},

					documentChanges = new object[]
					{
						new
						{
							textDocument = new { uri = new Uri(secondPath).AbsoluteUri },

							edits = new object[]
							{
								new
								{
									range = new { start = new { line = 1, character = 6 }, end = new { line = 1, character = 12 } },
									newText = "renamed",
								},
							},
						},
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		WorkspaceEdit? workspaceEdit = await provider.GetRenameEditsAsync(
			new RenameRequest(
				documentId: filePath,
				documentText: content,
				symbolOffset: content.OffsetAt(new TextPosition(0, 14)),
				newName: "renamed"
			),

			TestContext.CancellationToken
		);

		// The fake response populates both alternative edit representations; the structured
		// documentChanges list wins so a real server's response cannot be applied twice.
		Assert.IsNotNull(workspaceEdit);
		Assert.HasCount(1, workspaceEdit.DocumentEdits);
		Assert.AreEqual(secondPath, workspaceEdit.DocumentEdits[0].DocumentId);
		Assert.AreEqual("renamed", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);

		JsonElement parameters = client.GetLastRequestParameters("textDocument/rename");

		Assert.AreEqual(new Uri(filePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.AreEqual("renamed", parameters.GetProperty("newName").GetString());

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/rename"], client.GetSentMethodNames());
	}

	public TestContext? TestContext { get; set; }
}
