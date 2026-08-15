using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	[DataRow(false, DisplayName = "Fresh client before startup")]
	[DataRow(true, DisplayName = "Active transport session before the handshake")]
	public void ClientWithoutNegotiatedCapabilities_ExposesNoServerCapabilities(bool hasActiveSessionBeforeHandshake)
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		long expectedGeneration = 0;

		if (hasActiveSessionBeforeHandshake)
		{
			TransportSession session = CreateTransportSession(client, 3, process: null, Stream.Null, Stream.Null);
			SetActiveSession(client, session);
			expectedGeneration = 3;
		}

		Assert.IsFalse(client.IsReady);
		Assert.AreEqual(expectedGeneration, client.TransportGeneration);
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
		Assert.AreEqual(0, client.SemanticTokenTypes.Count);
		Assert.AreEqual(0, client.SemanticTokenModifiers.Count);
		Assert.IsFalse(client.SupportsCompletionResolve);
		Assert.IsFalse(client.SupportsDocumentSymbols);
		Assert.IsFalse(client.SupportsCodeActions);
		Assert.IsFalse(client.SupportsReferences);
		Assert.IsFalse(client.SupportsRename);
		Assert.IsFalse(client.SupportsFormatting);
		Assert.IsFalse(client.SupportsHover);
		Assert.IsFalse(client.SupportsDefinition);
		Assert.IsFalse(client.SupportsSignatureHelp);
		Assert.IsFalse(client.SupportsSemanticTokensFull);
		Assert.IsFalse(client.SupportsPullDiagnostics);
	}

	[TestMethod]
	public void CaptureServerCapabilities_WhenPositionEncodingIsUtf16CaseInsensitive_IsAccepted()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "positionEncoding": "UTF-16",
			    "textDocumentSync": {
			      "change": 1
			    }
			  }
			}
			"""));

		Assert.AreEqual(TextDocumentSyncKind.Full, client.TextDocumentSyncKind);
	}

	[TestMethod]
	public void CaptureServerCapabilities_WhenPositionEncodingIsNotUtf16_ThrowsNotSupportedException()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

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
			""");

		NotSupportedException exception = Assert.ThrowsExactly<NotSupportedException>(() => CaptureServerCapabilities(client, response));

		StringAssert.Contains(exception.Message, "utf-8");
	}

	[TestMethod]
	public void CaptureServerCapabilities_WithCompletionResolveProvider_EnablesCompletionResolve()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
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
			"""));

		Assert.IsTrue(client.SupportsCompletionResolve);
	}

	[TestMethod]
	public void CaptureServerCapabilities_UsesFullTextSyncWhenServerAdvertisesFullSync()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    }
			  }
			}
			"""));

		Assert.AreEqual(TextDocumentSyncKind.Full, client.TextDocumentSyncKind);
	}

	[TestMethod]
	public void CaptureServerCapabilities_RecognizesHoverDefinitionAndSignatureHelpProviders()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
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
			"""));

		Assert.IsTrue(client.SupportsHover);
		Assert.IsTrue(client.SupportsDefinition);
		Assert.IsTrue(client.SupportsSignatureHelp);
	}

	[TestMethod]
	public void CaptureServerCapabilities_WithExplicitlyDisabledProviders_KeepsThemDisabled()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
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
			"""));

		Assert.IsFalse(client.SupportsHover);
		Assert.IsFalse(client.SupportsDefinition);
		Assert.IsFalse(client.SupportsSignatureHelp);
	}

	[TestMethod]
	public void CaptureServerCapabilities_RecognizesReferenceRenameAndFormattingProviders()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
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
			"""));

		Assert.IsTrue(client.SupportsReferences);
		Assert.IsTrue(client.SupportsRename);
		Assert.IsTrue(client.SupportsFormatting);
	}

	[TestMethod]
	public void CaptureServerCapabilities_RecognizesTheDocumentSymbolProvider()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		Assert.IsFalse(client.SupportsDocumentSymbols);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    },
			    "documentSymbolProvider": { "label": "Lua" }
			  }
			}
			"""));

		// The object form of documentSymbolProvider carries options; its presence is what matters.
		Assert.IsTrue(client.SupportsDocumentSymbols);
	}

	[TestMethod]
	public void CaptureServerCapabilities_DocumentSymbolBooleanForm_IsRecognized()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    },
			    "documentSymbolProvider": true
			  }
			}
			"""));

		Assert.IsTrue(client.SupportsDocumentSymbols);
	}

	[TestMethod]
	public void CaptureServerCapabilities_DocumentSymbolAbsent_StaysUnsupported()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    }
			  }
			}
			"""));

		Assert.IsFalse(client.SupportsDocumentSymbols);
	}

	[TestMethod]
	public void CaptureServerCapabilities_RecognizesTheCodeActionProvider()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		Assert.IsFalse(client.SupportsCodeActions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    },
			    "codeActionProvider": { "codeActionKinds": ["quickfix"] }
			  }
			}
			"""));

		// The object form of codeActionProvider carries options; its presence is what matters.
		Assert.IsTrue(client.SupportsCodeActions);
		Assert.IsFalse(client.SupportsCodeActionResolve);
	}

	[TestMethod]
	public void CaptureServerCapabilities_CodeActionResolveProvider_IsExposed()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		Assert.IsFalse(client.SupportsCodeActionResolve);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    },
			    "codeActionProvider": { "resolveProvider": true }
			  }
			}
			"""));

		Assert.IsTrue(client.SupportsCodeActions);
		Assert.IsTrue(client.SupportsCodeActionResolve);
	}

	[TestMethod]
	public void CaptureServerCapabilities_CodeActionBooleanForm_IsRecognized()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    },
			    "codeActionProvider": true
			  }
			}
			"""));

		// The boolean form carries no resolve flag; resolve stays unnegotiated.
		Assert.IsTrue(client.SupportsCodeActions);
		Assert.IsFalse(client.SupportsCodeActionResolve);
	}

	[TestMethod]
	public void CaptureServerCapabilities_CodeActionAbsent_StaysUnsupported()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    }
			  }
			}
			"""));

		Assert.IsFalse(client.SupportsCodeActions);
		Assert.IsFalse(client.SupportsCodeActionResolve);
	}

	[TestMethod]
	public void DeserializeInitializeResponse_TreatsExplicitNullCapabilitiesAsUnsupported()
	{
		InitializeResponse response = DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": { "change": 1 },
			    "referencesProvider": null,
			    "renameProvider": null,
			    "documentFormattingProvider": null,
			    "documentSymbolProvider": null,
			    "codeActionProvider": null,
			    "completionProvider": null,
			    "semanticTokensProvider": null
			  }
			}
			""");

		Assert.IsNotNull(response.Capabilities);
		Assert.IsNull(response.Capabilities.ReferencesProvider);
		Assert.IsNull(response.Capabilities.RenameProvider);
		Assert.IsNull(response.Capabilities.DocumentFormattingProvider);
		Assert.IsNull(response.Capabilities.DocumentSymbolProvider);
		Assert.IsNull(response.Capabilities.CodeActionProvider);
		Assert.IsNull(response.Capabilities.CompletionProvider);
		Assert.IsNull(response.Capabilities.SemanticTokensProvider);

		// Direct struct deserialization reaches the converter's Null branch even though Nullable<T> properties
		// are handled by the framework before the converter is invoked.
		Assert.IsFalse(JsonSerializer.Deserialize<SupportedCapability>("null").IsSupported);
	}

	[TestMethod]
	public void CaptureServerCapabilities_RecognizesSemanticTokensLegendAndFullSupport()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
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
			"""));

		Assert.IsTrue(client.SupportsSemanticTokensFull);

		CollectionAssert.AreEqual(new[] { "function", "variable" }, client.SemanticTokenTypes.ToArray());
		CollectionAssert.AreEqual(new[] { "declaration" }, client.SemanticTokenModifiers.ToArray());
	}

	[TestMethod]
	public void CaptureServerCapabilities_TracksWhenFullSemanticTokensAreUnsupported()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
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
			"""));

		Assert.IsFalse(client.SupportsSemanticTokensFull);

		CollectionAssert.AreEqual(new[] { "function" }, client.SemanticTokenTypes.ToArray());
	}

	[TestMethod]
	[DataRow("""{}""", DisplayName = "No capabilities payload")]
	[DataRow("""{ "capabilities": { "textDocumentSync": {} } }""", DisplayName = "Missing document change support")]
	[DataRow("""{ "capabilities": { "referencesProvider": true, "renameProvider": true } }""", DisplayName = "Missing text-document sync capability")]
	public void CaptureServerCapabilities_RejectsResponsesWithoutUsableDocumentSync(string responseJson)
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		Assert.ThrowsExactly<NotSupportedException>(() =>
			CaptureServerCapabilities(client, DeserializeInitializeResponse(responseJson)));
	}

	[TestMethod]
	public void DeserializeInitializeResponse_TreatsUnsupportedCapabilityShapesAsUnsupported()
	{
		InitializeResponse response = DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": "invalid"
			    },
			    "referencesProvider": "unexpected",
			    "renameProvider": [true],
			    "documentFormattingProvider": 123,
			    "semanticTokensProvider": {
			      "full": [],
			      "legend": {
			        "tokenTypes": ["function"],
			        "tokenModifiers": ["declaration"]
			      }
			    }
			  }
			}
			""");

		Assert.IsNotNull(response.Capabilities);
		Assert.AreEqual(TextDocumentSyncKind.None, response.Capabilities.TextDocumentSync?.Kind);
		Assert.IsFalse(response.Capabilities.ReferencesProvider?.IsSupported ?? true);
		Assert.IsFalse(response.Capabilities.RenameProvider?.IsSupported ?? true);
		Assert.IsFalse(response.Capabilities.DocumentFormattingProvider?.IsSupported ?? true);
		Assert.IsFalse(response.Capabilities.SemanticTokensProvider?.Full?.IsSupported ?? true);

		CollectionAssert.AreEqual(new[] { "function" }, response.Capabilities.SemanticTokensProvider?.Legend?.TokenTypes);
		CollectionAssert.AreEqual(new[] { "declaration" }, response.Capabilities.SemanticTokensProvider?.Legend?.TokenModifiers);
	}

	[TestMethod]
	public void DeserializeInitializeResponse_TreatsBooleanTextDocumentSyncAsUnsupported()
	{
		InitializeResponse response = DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": true
			  }
			}
			""");

		Assert.IsNotNull(response.Capabilities);
		Assert.AreEqual(TextDocumentSyncKind.None, response.Capabilities.TextDocumentSync?.Kind);
	}

	[TestMethod]
	public void SemanticTokenCapabilityLists_AreReadOnly()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
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
			"""));

		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)client.SemanticTokenTypes)[0] = "class");
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)client.SemanticTokenModifiers)[0] = "readonly");

		CollectionAssert.AreEqual(new[] { "function" }, client.SemanticTokenTypes.ToArray());
		CollectionAssert.AreEqual(new[] { "declaration" }, client.SemanticTokenModifiers.ToArray());
	}

	[TestMethod]
	public void SerializeInitializeResponse_RoundTripsNormalizedCapabilityShapes()
	{
		InitializeResponse initializeResponse = DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": 2,
			    "referencesProvider": { "dynamicRegistration": true },
			    "semanticTokensProvider": {
			      "full": { "delta": true },
			      "legend": {
			        "tokenTypes": ["function"],
			        "tokenModifiers": ["declaration"]
			      }
			    }
			  }
			}
			""");

		string json = JsonSerializer.Serialize(initializeResponse);

		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement capabilities = document.RootElement.GetProperty("capabilities");

		Assert.AreEqual(2, capabilities.GetProperty("textDocumentSync").GetInt32());
		Assert.IsTrue(capabilities.GetProperty("referencesProvider").GetBoolean());
		Assert.IsTrue(capabilities.GetProperty("semanticTokensProvider").GetProperty("full").GetBoolean());
	}
}
