using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	[TestMethod]
	[DataRow(false, DisplayName = "Fresh client before startup")]
	[DataRow(true, DisplayName = "Active connection session before the handshake")]
	public void ClientWithoutNegotiatedCapabilities_ExposesNoServerCapabilities(bool hasActiveSessionBeforeHandshake)
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		long expectedGeneration = 0;

		if (hasActiveSessionBeforeHandshake)
		{
			ConnectionSession session = CreateConnectionSession(
				client,
				generation: 3,
				process: null,
				serverOutputStream: Stream.Null,
				serverInputStream: Stream.Null
			);

			SetActiveSession(client, session);
			expectedGeneration = 3;
		}

		Assert.IsFalse(client.IsReady);
		Assert.AreEqual(expectedGeneration, client.ConnectionGeneration);
		Assert.AreEqual(TextDocumentSyncKind.None, client.NegotiatedCapabilities.TextDocumentSyncKind);
		Assert.IsEmpty(client.NegotiatedCapabilities.SemanticTokenTypes);
		Assert.IsEmpty(client.NegotiatedCapabilities.SemanticTokenModifiers);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCompletionResolve);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsDocumentSymbols);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCodeActions);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsReferences);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsRename);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsFormatting);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsHover);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsDefinition);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsSignatureHelp);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsSemanticTokensFull);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsPullDiagnostics);
	}

	[TestMethod]
	public void CaptureServerCapabilities_WhenPositionEncodingIsUtf16CaseInsensitive_IsAccepted()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "positionEncoding": "UTF-16",
				    "textDocumentSync": {
				      "change": 1
				    }
				  }
				}
				"""
			)
		);

		Assert.AreEqual(TextDocumentSyncKind.Full, client.NegotiatedCapabilities.TextDocumentSyncKind);
	}

	[TestMethod]
	public void CaptureServerCapabilities_WhenPositionEncodingIsNotUtf16_ThrowsNotSupportedException()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		InitializeResponse response = DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "positionEncoding": "utf-8",
			    "textDocumentSync": {
			      "change": 1
			    }
			  }
			}
			"""
		);

		NotSupportedException exception = Assert.ThrowsExactly<NotSupportedException>(() => CaptureServerCapabilities(client, response));

		Assert.Contains("utf-8", exception.Message);
	}

	[TestMethod]
	public void CaptureServerCapabilities_WithCompletionResolveProvider_EnablesCompletionResolve()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "completionProvider": {
				      "resolveProvider": true,
				      "triggerCharacters": ["."]
				    }
				  }
				}
				"""
			)
		);

		Assert.IsTrue(client.NegotiatedCapabilities.SupportsCompletionResolve);
	}

	[TestMethod]
	public void CaptureServerCapabilities_UsesFullTextSyncWhenServerAdvertisesFullSync()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    }
				  }
				}
				"""
			)
		);

		Assert.AreEqual(TextDocumentSyncKind.Full, client.NegotiatedCapabilities.TextDocumentSyncKind);
	}

	[TestMethod]
	public void CaptureServerCapabilities_RecognizesHoverDefinitionAndSignatureHelpProviders()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "hoverProvider": true,
				    "definitionProvider": {},
				    "signatureHelpProvider": {}
				  }
				}
				"""
			)
		);

		Assert.IsTrue(client.NegotiatedCapabilities.SupportsHover);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsDefinition);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsSignatureHelp);
	}

	[TestMethod]
	public void CaptureServerCapabilities_WithExplicitlyDisabledProviders_KeepsThemDisabled()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "hoverProvider": false,
				    "definitionProvider": false,
				    "signatureHelpProvider": false
				  }
				}
				"""
			)
		);

		Assert.IsFalse(client.NegotiatedCapabilities.SupportsHover);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsDefinition);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsSignatureHelp);
	}

	[TestMethod]
	public void CaptureServerCapabilities_RecognizesReferenceRenameAndFormattingProviders()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "referencesProvider": {},
				    "renameProvider": { "prepareProvider": true },
				    "documentFormattingProvider": true
				  }
				}
				"""
			)
		);

		Assert.IsTrue(client.NegotiatedCapabilities.SupportsReferences);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsRename);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsFormatting);
	}

	[TestMethod]
	public void CaptureServerCapabilities_RecognizesTheDocumentSymbolProvider()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		Assert.IsFalse(client.NegotiatedCapabilities.SupportsDocumentSymbols);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "documentSymbolProvider": { "label": "Lua" }
				  }
				}
				"""
			)
		);

		// The object form of documentSymbolProvider carries options; its presence is what matters.
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsDocumentSymbols);
	}

	[TestMethod]
	public void CaptureServerCapabilities_DocumentSymbolBooleanForm_IsRecognized()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "documentSymbolProvider": true
				  }
				}
				"""
			)
		);

		Assert.IsTrue(client.NegotiatedCapabilities.SupportsDocumentSymbols);
	}

	[TestMethod]
	public void CaptureServerCapabilities_DocumentSymbolAbsent_StaysUnsupported()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    }
				  }
				}
				"""
			)
		);

		Assert.IsFalse(client.NegotiatedCapabilities.SupportsDocumentSymbols);
	}

	[TestMethod]
	public void CaptureServerCapabilities_RecognizesTheCodeActionProvider()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCodeActions);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "codeActionProvider": { "codeActionKinds": ["quickfix"] }
				  }
				}
				"""
			)
		);

		// The object form of codeActionProvider carries options; its presence is what matters.
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsCodeActions);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCodeActionResolve);
	}

	[TestMethod]
	public void CaptureServerCapabilities_CodeActionResolveProvider_IsExposed()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCodeActionResolve);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "codeActionProvider": { "resolveProvider": true }
				  }
				}
				"""
			)
		);

		Assert.IsTrue(client.NegotiatedCapabilities.SupportsCodeActions);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsCodeActionResolve);
	}

	[TestMethod]
	public void CaptureServerCapabilities_CodeActionBooleanForm_IsRecognized()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "codeActionProvider": true
				  }
				}
				"""
			)
		);

		// The boolean form carries no resolve flag; resolve stays unnegotiated.
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsCodeActions);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCodeActionResolve);
	}

	[TestMethod]
	public void CaptureServerCapabilities_CodeActionAbsent_StaysUnsupported()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    }
				  }
				}
				"""
			)
		);

		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCodeActions);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCodeActionResolve);
	}

	[TestMethod]
	public void CaptureServerCapabilities_RecognizesSemanticTokensLegendAndFullSupport()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "semanticTokensProvider": {
				      "full": {
				        "delta": true
				      },
				      "legend": {
				        "tokenTypes": ["function", "variable"],
				        "tokenModifiers": ["declaration"]
				      }
				    }
				  }
				}
				"""
			)
		);

		Assert.IsTrue(client.NegotiatedCapabilities.SupportsSemanticTokensFull);

		Assert.AreSequenceEqual(["function", "variable"], [.. client.NegotiatedCapabilities.SemanticTokenTypes]);
		Assert.AreSequenceEqual(["declaration"], [.. client.NegotiatedCapabilities.SemanticTokenModifiers]);
	}

	[TestMethod]
	public void CaptureServerCapabilities_TracksWhenFullSemanticTokensAreUnsupported()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "semanticTokensProvider": {
				      "full": false,
				      "legend": {
				        "tokenTypes": ["function"],
				        "tokenModifiers": []
				      }
				    }
				  }
				}
				"""
			)
		);

		Assert.IsFalse(client.NegotiatedCapabilities.SupportsSemanticTokensFull);

		Assert.AreSequenceEqual(["function"], [.. client.NegotiatedCapabilities.SemanticTokenTypes]);
	}

	[TestMethod]
	[DataRow("""{}""", DisplayName = "No capabilities payload")]
	[DataRow("""{ "capabilities": { "textDocumentSync": {} } }""", DisplayName = "Missing document change support")]
	[DataRow("""{ "capabilities": { "referencesProvider": true, "renameProvider": true } }""", DisplayName = "Missing text-document sync capability")]
	public void CaptureServerCapabilities_RejectsResponsesWithoutUsableDocumentSync(string responseJson)
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		Assert.ThrowsExactly<NotSupportedException>(() => CaptureServerCapabilities(client, DeserializeInitializeResponse(responseJson)));
	}

	[TestMethod]
	public void SemanticTokenCapabilityLists_AreReadOnly()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "semanticTokensProvider": {
				      "legend": {
				        "tokenTypes": ["function"],
				        "tokenModifiers": ["declaration"]
				      }
				    }
				  }
				}
				"""
			)
		);

		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)client.NegotiatedCapabilities.SemanticTokenTypes)[0] = "class");
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)client.NegotiatedCapabilities.SemanticTokenModifiers)[0] = "readonly");

		Assert.AreSequenceEqual(["function"], [.. client.NegotiatedCapabilities.SemanticTokenTypes]);
		Assert.AreSequenceEqual(["declaration"], [.. client.NegotiatedCapabilities.SemanticTokenModifiers]);
	}
}
