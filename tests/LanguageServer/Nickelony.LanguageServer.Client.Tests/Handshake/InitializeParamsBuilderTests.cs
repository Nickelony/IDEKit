using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.Infrastructure;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class InitializeParamsBuilderTests
{
	private static InitializeParams Build(string[] workspaceRootDirectoryPaths, LanguageServerClientOptions? options = null, ILogger? logger = null)
	{
		LanguageServerClientOptions effectiveOptions = options ?? LanguageServerClientOptions.Default;

		var builder = new InitializeParamsBuilder(
			roots: WorkspaceRoots.Create(workspaceRootDirectoryPaths),
			initializationOptionsProvider: effectiveOptions.InitializationOptionsProvider,
			clientCapabilitiesProvider: effectiveOptions.ClientCapabilitiesProvider,
			hooks: new LanguageServerClientHooks(),
			logger: logger ?? NullLogger.Instance
		);

		return builder.Build();
	}

	private static JsonElement ToElement(JsonNode? node) => JsonSerializer.SerializeToElement(node);

	[TestMethod]
	public void Build_UsesInjectedInitializationOptions()
	{
		InitializeParams initializeParams = Build(
			[TestPaths.Root],

			LanguageServerClientOptions.Default with
			{
				ClientCapabilitiesProvider = static _ => new { },

				InitializationOptionsProvider = static workspaceRoots => new
				{
					workspace = workspaceRoots[0],
					rootCount = workspaceRoots.Count,
					customFlag = true,
				},
			}
		);

		JsonElement initializationOptions = ToElement(initializeParams.InitializationOptions);

		Assert.AreEqual(TestPaths.Root, initializationOptions.GetProperty("workspace").GetString());
		Assert.AreEqual(1, initializationOptions.GetProperty("rootCount").GetInt32());
		Assert.IsTrue(initializationOptions.GetProperty("customFlag").GetBoolean());
	}

	[TestMethod]
	public void Build_UsesInjectedClientCapabilities()
	{
		InitializeParams initializeParams = Build(
			[TestPaths.Root],

			LanguageServerClientOptions.Default with
			{
				ClientCapabilitiesProvider = static workspaceRoots => new
				{
					workspace = new
					{
						workspaceFolders = true,
						configuration = true,
						root = workspaceRoots[0],
					},
				},
			}
		);

		JsonElement capabilities = ToElement(initializeParams.Capabilities);

		Assert.IsTrue(capabilities.GetProperty("workspace").GetProperty("workspaceFolders").GetBoolean());
		Assert.IsTrue(capabilities.GetProperty("workspace").GetProperty("configuration").GetBoolean());
		Assert.AreEqual(TestPaths.Root, capabilities.GetProperty("workspace").GetProperty("root").GetString());
	}

	[TestMethod]
	public void Build_ForcesUnsupportedDynamicRegistrationFlagsToFalse()
	{
		InitializeParams initializeParams = Build(
			[TestPaths.Root],

			LanguageServerClientOptions.Default with
			{
				ClientCapabilitiesProvider = static _ => new
				{
					workspace = new
					{
						didChangeWatchedFiles = new { dynamicRegistration = true },
						fileOperations = new { didCreate = new { dynamicRegistration = true }, didRename = new { dynamicRegistration = true } },
					},

					textDocument = new
					{
						rename = new { dynamicRegistration = true, prepareSupport = true },
						references = new { dynamicRegistration = true },
						formatting = new { dynamicRegistration = true },
						completion = new { dynamicRegistration = true },
					},
				},
			}
		);

		JsonElement capabilities = ToElement(initializeParams.Capabilities);

		Assert.IsFalse(capabilities.GetProperty("workspace").GetProperty("didChangeWatchedFiles").GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsFalse(capabilities.GetProperty("textDocument").GetProperty("rename").GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsFalse(capabilities.GetProperty("textDocument").GetProperty("references").GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsFalse(capabilities.GetProperty("textDocument").GetProperty("formatting").GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsFalse(capabilities.GetProperty("textDocument").GetProperty("completion").GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsTrue(capabilities.GetProperty("textDocument").GetProperty("rename").GetProperty("prepareSupport").GetBoolean());

		// The payload declares dynamicRegistration on the nested workspace.fileOperations.didCreate and didRename objects,
		// which a shallow rewrite would miss. fileOperations declares no member and must not gain one.
		Assert.IsFalse(
			capabilities
				.GetProperty("workspace")
				.GetProperty("fileOperations")
				.GetProperty("didCreate")
				.GetProperty("dynamicRegistration")
				.GetBoolean()
		);

		Assert.IsFalse(
			capabilities
				.GetProperty("workspace")
				.GetProperty("fileOperations")
				.GetProperty("didRename")
				.GetProperty("dynamicRegistration")
				.GetBoolean()
		);

		Assert.IsFalse(capabilities.GetProperty("workspace").GetProperty("fileOperations").TryGetProperty("dynamicRegistration", out _));
	}

	[TestMethod]
	public void Build_NormalizesWorkspaceRootAndFolderName()
	{
		string rawWorkspaceRoot = TestPaths.WithAlternateSeparators(TestPaths.Root);

		InitializeParams initializeParams = Build(
			[rawWorkspaceRoot],

			LanguageServerClientOptions.Default with
			{
				ClientCapabilitiesProvider = static _ => new { },
				InitializationOptionsProvider = static workspaceRoots => new { workspace = workspaceRoots[0], rootCount = workspaceRoots.Count },
			}
		);

		IReadOnlyList<WorkspaceFolder> workspaceFolders = initializeParams.WorkspaceFolders!;
		JsonElement initializationOptions = ToElement(initializeParams.InitializationOptions);

		string expectedWorkspaceRoot = LocalPathRules.Normalize(rawWorkspaceRoot);

		Assert.HasCount(1, workspaceFolders);
		Assert.AreEqual(expectedWorkspaceRoot, initializationOptions.GetProperty("workspace").GetString());
		Assert.AreEqual(1, initializationOptions.GetProperty("rootCount").GetInt32());
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(expectedWorkspaceRoot), initializeParams.RootUri);
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(expectedWorkspaceRoot), workspaceFolders[0].Uri);
		Assert.AreEqual("Workspace", workspaceFolders[0].Name);
	}

	[TestMethod]
	public void Build_MultipleRoots_SendsPrimaryRootUriAndAllFolderEntries()
	{
		InitializeParams initializeParams = Build([TestPaths.Root, TestPaths.SecondaryRoot]);

		IReadOnlyList<WorkspaceFolder> workspaceFolders = initializeParams.WorkspaceFolders!;

		string expectedPrimaryRoot = LocalPathRules.Normalize(TestPaths.Root);
		string expectedSecondaryRoot = LocalPathRules.Normalize(TestPaths.SecondaryRoot);

		Assert.HasCount(2, workspaceFolders);
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(expectedPrimaryRoot), initializeParams.RootUri);
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(expectedPrimaryRoot), workspaceFolders[0].Uri);
		Assert.AreEqual("Workspace", workspaceFolders[0].Name);
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(expectedSecondaryRoot), workspaceFolders[1].Uri);
		Assert.AreEqual("Secondary", workspaceFolders[1].Name);
	}

	[TestMethod]
	public void Build_MultipleRoots_PassesNormalizedRootsToProvidersInCallerOrder()
	{
		string[] capabilityRoots = [];
		string[] initializationRoots = [];

		string rawPrimary = TestPaths.WithAlternateSeparators(TestPaths.Root);
		string rawSecondary = TestPaths.WithAlternateSeparators(TestPaths.SecondaryRoot);

		_ = Build(
			[rawPrimary, rawSecondary],

			LanguageServerClientOptions.Default with
			{
				ClientCapabilitiesProvider = workspaceRoots =>
				{
					capabilityRoots = [.. workspaceRoots];
					return new { };
				},

				InitializationOptionsProvider = workspaceRoots =>
				{
					initializationRoots = [.. workspaceRoots];
					return new { };
				},
			}
		);

		Assert.AreSequenceEqual([LocalPathRules.Normalize(rawPrimary), LocalPathRules.Normalize(rawSecondary)], capabilityRoots);

		Assert.AreSequenceEqual([LocalPathRules.Normalize(rawPrimary), LocalPathRules.Normalize(rawSecondary)], initializationRoots);
	}

	[TestMethod]
	public void Build_WithEmptyWorkspaceRoots_ModelsAFolderlessSession()
	{
		InitializeParams initializeParams = Build([]);

		Assert.IsNull(initializeParams.RootUri);
		Assert.IsNull(initializeParams.WorkspaceFolders);
	}

	[TestMethod]
	public void Build_WithNestedWorkspaceRoots_AcceptsTheConfiguration()
	{
		InitializeParams initializeParams = Build([TestPaths.Root, Path.Combine(TestPaths.Root, "Nested")]);
		Assert.HasCount(2, initializeParams.WorkspaceFolders!);
	}

	[TestMethod]
	public void Build_WhenCapabilitiesProviderReturnsNonObject_SendsEmptyCapabilities()
	{
		LanguageServerClientOptions options = LanguageServerClientOptions.Default with { ClientCapabilitiesProvider = static _ => new[] { 1 } };

		JsonElement capabilities = ToElement(Build([TestPaths.Root], options).Capabilities);

		// A non-object payload cannot receive the enforced overrides and must not reach the server as a scalar
		// or array; the handshake stays protocol-valid with empty capabilities instead.
		Assert.AreEqual(JsonValueKind.Object, capabilities.ValueKind);
		Assert.IsEmpty(capabilities.EnumerateObject());
	}

	[TestMethod]
	public void Build_WhenCapabilitiesProviderReturnsNull_SendsEmptyCapabilities()
	{
		LanguageServerClientOptions options = LanguageServerClientOptions.Default with { ClientCapabilitiesProvider = static _ => null };

		JsonElement capabilities = ToElement(Build([TestPaths.Root], options).Capabilities);

		// A null factory result is treated as an empty capabilities object; the only member present is the
		// enforced UTF-16 position-encoding pin the client always adds.
		Assert.AreEqual(JsonValueKind.Object, capabilities.ValueKind);
		Assert.HasCount(1, capabilities.EnumerateObject());
	}

	[TestMethod]
	public void Build_WithoutWorkspaceRoots_OmitsRootsAndPinsUtf16()
	{
		InitializeParams initializeParams = Build([]);

		Assert.IsNull(initializeParams.RootUri);
		Assert.IsNull(initializeParams.WorkspaceFolders);
		Assert.AreEqual("utf-16", ToElement(initializeParams.Capabilities).GetProperty("general").GetProperty("positionEncodings")[0].GetString());
	}

	[TestMethod]
	public void Build_ConsumerAdvertisesUtf8_OverridesEncodingAndWorkDoneProgress()
	{
		var options = new LanguageServerClientOptions
		{
			ClientCapabilitiesProvider = _ => new
			{
				General = new { PositionEncodings = new[] { "utf-8" } },
				Window = new { WorkDoneProgress = true },
			},
		};

		JsonElement capabilities = ToElement(Build([TestPaths.Root], options).Capabilities);

		Assert.AreEqual("utf-16", capabilities.GetProperty("general").GetProperty("positionEncodings")[0].GetString());
		Assert.IsFalse(capabilities.GetProperty("window").GetProperty("workDoneProgress").GetBoolean());
	}

	[TestMethod]
	public void Build_ConsumerAdvertisesUnservicedCapabilities_ForcesThemOff()
	{
		// The client does not service workspace/applyEdit, and its semantic-token decoder cannot represent
		// multi-line or overlapping tokens, so an advertised capability must be rewritten off rather than
		// letting a server rely on a feature this client cannot honor.
		var options = new LanguageServerClientOptions
		{
			ClientCapabilitiesProvider = _ => new
			{
				workspace = new { applyEdit = true },
				textDocument = new { semanticTokens = new { multilineTokenSupport = true, overlappingTokenSupport = true } },
			},
		};

		JsonElement capabilities = ToElement(Build([TestPaths.Root], options).Capabilities);

		Assert.IsFalse(capabilities.GetProperty("workspace").GetProperty("applyEdit").GetBoolean());

		JsonElement semanticTokens = capabilities.GetProperty("textDocument").GetProperty("semanticTokens");

		Assert.IsFalse(semanticTokens.GetProperty("multilineTokenSupport").GetBoolean());
		Assert.IsFalse(semanticTokens.GetProperty("overlappingTokenSupport").GetBoolean());
	}

	[TestMethod]
	public void Build_WhenCapabilitiesProviderReturnsNonObject_WarnsThatTheOverridesCannotBeEnforced()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		LanguageServerClientOptions options = LanguageServerClientOptions.Default with { ClientCapabilitiesProvider = static _ => 42 };

		_ = Build([TestPaths.Root], options, logger);

		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Build_WhenInitializationOptionsProviderReturnsNull_SendsNullInitializationOptions()
	{
		LanguageServerClientOptions options = LanguageServerClientOptions.Default with { InitializationOptionsProvider = static _ => null };
		Assert.IsNull(Build([TestPaths.Root], options).InitializationOptions);
	}

	[TestMethod]
	public void Build_SerializesInitializationOptionsWithLowerCamelMemberNames()
	{
		LanguageServerClientOptions options = LanguageServerClientOptions.Default with
		{
			InitializationOptionsProvider = static _ => new { ExampleSetting = true },
		};

		JsonElement initializationOptions = ToElement(Build([TestPaths.Root], options).InitializationOptions);

		Assert.IsTrue(initializationOptions.GetProperty("exampleSetting").GetBoolean());
		Assert.IsFalse(initializationOptions.TryGetProperty("ExampleSetting", out _));
	}

	[TestMethod]
	public void Build_ReportsTheProcessIdOfTheCurrentProcess() => Assert.AreEqual(Environment.ProcessId, Build([TestPaths.Root]).ProcessId);

	[TestMethod]
	public void Serialization_WritesTheProtocolMemberNamesInDeclarationOrder()
	{
		JsonElement wire = JsonSerializer.SerializeToElement(Build([TestPaths.Root]));

		Assert.AreSequenceEqual(
			["processId", "initializationOptions", "rootUri", "workspaceFolders", "capabilities"],
			[.. wire.EnumerateObject().Select(property => property.Name)]
		);

		Assert.AreEqual(JsonValueKind.Array, wire.GetProperty("workspaceFolders").ValueKind);
		Assert.AreEqual("Workspace", wire.GetProperty("workspaceFolders")[0].GetProperty("name").GetString());
	}

	[TestMethod]
	public void Serialization_OfAFolderlessSession_WritesNullForTheRootMembers()
	{
		JsonElement wire = JsonSerializer.SerializeToElement(Build([]));

		Assert.AreEqual(JsonValueKind.Null, wire.GetProperty("rootUri").ValueKind);
		Assert.AreEqual(JsonValueKind.Null, wire.GetProperty("workspaceFolders").ValueKind);
	}
}
