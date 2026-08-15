using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	public void BuildConfigurationResponse_ReturnsRequestedSections()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions
		{
			SettingsProvider = static () => new
			{
				Example = new
				{
					runtime = new
					{
						version = "4.0"
					}
				}
			}
		});

		object?[] response = client.ProtocolForwarder.BuildConfigurationResponse(
			new WorkspaceConfigurationParams(
			[
				new WorkspaceConfigurationItem("Example"),
				new WorkspaceConfigurationItem("Example.runtime")
			]));

		Assert.AreEqual(2, response.Length);
		Assert.AreEqual("4.0", JsonSerializer.SerializeToElement(response[1]).GetProperty("version").GetString());
	}

	[TestMethod]
	public void BuildConfigurationResponse_ReturnsRequestedNestedSections()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions
		{
			SettingsProvider = static () => new
			{
				Editor = new
				{
					theme = "Dark",
					fonts = new
					{
						size = 14
					}
				}
			}
		});

		object?[] response = client.ProtocolForwarder.BuildConfigurationResponse(
			new WorkspaceConfigurationParams(
			[
				new WorkspaceConfigurationItem("Editor"),
				new WorkspaceConfigurationItem("Editor.fonts")
			]));

		Assert.AreEqual(2, response.Length);
		Assert.AreEqual("Dark", JsonSerializer.SerializeToElement(response[0]).GetProperty("theme").GetString());
		Assert.AreEqual(14, JsonSerializer.SerializeToElement(response[1]).GetProperty("size").GetInt32());
	}

	[TestMethod]
	public void BuildConfigurationResponse_ReturnsNullWhenSectionIsMissing()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions
		{
			SettingsProvider = static () => new
			{
				Editor = new
				{
					theme = "Dark"
				}
			}
		});

		object?[] response = client.ProtocolForwarder.BuildConfigurationResponse(
			new WorkspaceConfigurationParams(
			[
				new WorkspaceConfigurationItem("Example"),
				new WorkspaceConfigurationItem("Example.runtime")
			]));

		Assert.AreEqual(2, response.Length);
		Assert.IsNull(response[0]);
		Assert.IsNull(response[1]);
	}

	[TestMethod]
	public void BuildConfigurationResponse_ReusesCachedSettingsSnapshotAcrossRepeatedRequests()
	{
		int settingsProviderCallCount = 0;

		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions
		{
			SettingsProvider = () =>
		{
			settingsProviderCallCount++;

			return new
			{
				Example = new
				{
					Runtime = new
					{
						Version = "4.0"
					}
				}
			};
		}
		});

		object?[] firstResponse = client.ProtocolForwarder.BuildConfigurationResponse(
			new WorkspaceConfigurationParams(
			[
				new WorkspaceConfigurationItem("Example.runtime")
			]));

		object?[] secondResponse = client.ProtocolForwarder.BuildConfigurationResponse(
			new WorkspaceConfigurationParams(
			[
				new WorkspaceConfigurationItem("Example.runtime")
			]));

		Assert.AreEqual(1, settingsProviderCallCount);
		Assert.AreEqual("4.0", JsonSerializer.SerializeToElement(firstResponse[0]).GetProperty("version").GetString());
		Assert.AreEqual("4.0", JsonSerializer.SerializeToElement(secondResponse[0]).GetProperty("version").GetString());
	}

	[TestMethod]
	public async Task SendNotificationAsync_WhenDidChangeConfigurationIsAlreadyCanceled_PreservesCachedSettingsSnapshot()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions
		{
			SettingsProvider = static () => new
			{
				Example = new
				{
					Runtime = new
					{
						Version = "4.0"
					}
				}
			}
		});

		using var cancellationSource = new CancellationTokenSource();
		TransportSession session = CreateTransportSession(client, 11, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);
		cancellationSource.Cancel();

		object?[] initialResponse = client.ProtocolForwarder.BuildConfigurationResponse(
			new WorkspaceConfigurationParams(
			[
				new WorkspaceConfigurationItem("Example.runtime")
			]));

		Assert.AreEqual("4.0", JsonSerializer.SerializeToElement(initialResponse[0]).GetProperty("version").GetString());

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
			await client.SendNotificationAsync(
				"workspace/didChangeConfiguration",
				new DidChangeConfigurationParams(new
				{
					Example = new
					{
						Runtime = new
						{
							Version = "4.1"
						}
					}
				}),
				cancellationSource.Token).ConfigureAwait(false)).ConfigureAwait(false);

		object?[] responseAfterFailure = client.ProtocolForwarder.BuildConfigurationResponse(
			new WorkspaceConfigurationParams(
			[
				new WorkspaceConfigurationItem("Example.runtime")
			]));

		Assert.AreEqual("4.0", JsonSerializer.SerializeToElement(responseAfterFailure[0]).GetProperty("version").GetString());
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(11L, client.TransportGeneration);
	}

	[TestMethod]
	public void BuildConfigurationResponse_TypedSettingsObject_MatchesNestedSectionCaseInsensitively()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions
		{
			SettingsProvider = static () => new TestConfigurationRoot
			{
				Section = new TestSectionConfiguration
				{
					Runtime = new TestNestedSectionConfiguration
					{
						Version = "5.4"
					}
				}
			}
		});

		object?[] response = client.ProtocolForwarder.BuildConfigurationResponse(
			new WorkspaceConfigurationParams(
			[
				new WorkspaceConfigurationItem("Section.runtime")
			]));

		Assert.AreEqual(1, response.Length);
		Assert.IsNotNull(response[0]);
		Assert.AreEqual("5.4", JsonSerializer.SerializeToElement(response[0]).GetProperty("version").GetString());
	}

	[TestMethod]
	public void WorkspaceConfiguration_WhenSettingsSerializationFails_ReturnsNullValuesAndLogsWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		var cyclicSettings = new Dictionary<string, object?>();
		cyclicSettings["self"] = cyclicSettings;

		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions { SettingsProvider = () => cyclicSettings }, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));

		object?[] response = rpcTarget.WorkspaceConfiguration(
			new WorkspaceConfigurationParams(
			[
				new WorkspaceConfigurationItem("Example"),
				new WorkspaceConfigurationItem("Example.runtime")
			]));

		Assert.AreEqual(2, response.Length);
		Assert.IsNull(response[0]);
		Assert.IsNull(response[1]);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("workspace/configuration", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("returning null values", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void WorkspaceConfiguration_MalformedEntry_KeepsPositionAndAnswersNull()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions
		{
			SettingsProvider = () => new
			{
				Section = new TestSectionConfiguration
				{
					Runtime = new TestNestedSectionConfiguration
					{
						Version = "5.4"
					}
				}
			}
		});
		TransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);

		object?[] response = client.ProtocolForwarder.BuildConfigurationResponse(
			new WorkspaceConfigurationParams(
			[
				new WorkspaceConfigurationItem("Section"),
				new WorkspaceConfigurationItem(Section: null, IsMalformed: true),
				new WorkspaceConfigurationItem("Section.runtime")
			]));

		// The response must stay aligned one-for-one with the request: the malformed placeholder keeps its
		// slot and is answered with null instead of shifting every later section.
		Assert.AreEqual(3, response.Length);
		Assert.IsNotNull(response[0]);
		Assert.IsNull(response[1]);
		Assert.IsNotNull(response[2]);
		Assert.AreEqual("5.4", JsonSerializer.SerializeToElement(response[2]).GetProperty("version").GetString());
	}

	[TestMethod]
	public void BuildInitializeParams_UsesInjectedInitializationOptions()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", LanguageServerClientOptions.Default with
		{
			ClientCapabilitiesProvider = static _ => new { },
			InitializationOptionsProvider = static workspaceRoots => new
			{
				workspace = workspaceRoots[0],
				rootCount = workspaceRoots.Count,
				customFlag = true
			}
		});

		JsonElement initializeParams = JsonSerializer.SerializeToElement(client.BuildInitializeParams());
		JsonElement initializationOptions = initializeParams.GetProperty("initializationOptions");

		Assert.AreEqual(@"C:\Workspace", initializationOptions.GetProperty("workspace").GetString());
		Assert.AreEqual(1, initializationOptions.GetProperty("rootCount").GetInt32());
		Assert.IsTrue(initializationOptions.GetProperty("customFlag").GetBoolean());
	}

	[TestMethod]
	public void BuildInitializeParams_UsesInjectedClientCapabilities()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", LanguageServerClientOptions.Default with
		{
			ClientCapabilitiesProvider = static workspaceRoots => new
			{
				workspace = new
				{
					workspaceFolders = true,
					configuration = true,
					root = workspaceRoots[0]
				}
			}
		});

		JsonElement initializeParams = JsonSerializer.SerializeToElement(client.BuildInitializeParams());
		JsonElement capabilities = initializeParams.GetProperty("capabilities");

		Assert.IsTrue(capabilities.GetProperty("workspace").GetProperty("workspaceFolders").GetBoolean());
		Assert.IsTrue(capabilities.GetProperty("workspace").GetProperty("configuration").GetBoolean());
		Assert.AreEqual(@"C:\Workspace", capabilities.GetProperty("workspace").GetProperty("root").GetString());
	}

	[TestMethod]
	public void BuildInitializeParams_ForcesUnsupportedDynamicRegistrationFlagsToFalse()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", LanguageServerClientOptions.Default with
		{
			ClientCapabilitiesProvider = static _ => new
			{
				workspace = new
				{
					didChangeWatchedFiles = new { dynamicRegistration = true },
					fileOperations = new
					{
						didCreate = new { dynamicRegistration = true },
						didRename = new { dynamicRegistration = true }
					}
				},
				textDocument = new
				{
					rename = new { dynamicRegistration = true, prepareSupport = true },
					references = new { dynamicRegistration = true },
					formatting = new { dynamicRegistration = true },
					completion = new { dynamicRegistration = true }
				}
			}
		});

		JsonElement initializeParams = JsonSerializer.SerializeToElement(client.BuildInitializeParams());
		JsonElement capabilities = initializeParams.GetProperty("capabilities");

		Assert.IsFalse(capabilities.GetProperty("workspace").GetProperty("didChangeWatchedFiles").GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsFalse(capabilities.GetProperty("textDocument").GetProperty("rename").GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsFalse(capabilities.GetProperty("textDocument").GetProperty("references").GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsFalse(capabilities.GetProperty("textDocument").GetProperty("formatting").GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsFalse(capabilities.GetProperty("textDocument").GetProperty("completion").GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsTrue(capabilities.GetProperty("textDocument").GetProperty("rename").GetProperty("prepareSupport").GetBoolean());

		// Nested capability objects (workspace.fileOperations.didCreate/didRename) carry their own member,
		// and objects that never declared it must not gain one.
		Assert.IsFalse(capabilities.GetProperty("workspace").GetProperty("fileOperations").GetProperty("didCreate").GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsFalse(capabilities.GetProperty("workspace").GetProperty("fileOperations").GetProperty("didRename").GetProperty("dynamicRegistration").GetBoolean());
		Assert.IsFalse(capabilities.GetProperty("workspace").GetProperty("fileOperations").TryGetProperty("dynamicRegistration", out _));
	}

	[TestMethod]
	public void BuildInitializeParams_NormalizesWorkspaceRootAndFolderName()
	{
		using var client = new LanguageServerClient([@"C:/Workspace/"], "example-language-server.exe", LanguageServerClientOptions.Default with
		{
			ClientCapabilitiesProvider = static _ => new { },
			InitializationOptionsProvider = static workspaceRoots => new
			{
				workspace = workspaceRoots[0],
				rootCount = workspaceRoots.Count
			}
		});

		JsonElement initializeParams = JsonSerializer.SerializeToElement(client.BuildInitializeParams());
		JsonElement workspaceFolders = initializeParams.GetProperty("workspaceFolders");
		JsonElement workspaceFolder = workspaceFolders[0];
		string expectedWorkspaceRoot = LanguageServerPaths.NormalizeLocalPath(@"C:/Workspace/");

		Assert.AreEqual(1, workspaceFolders.GetArrayLength());
		Assert.AreEqual(expectedWorkspaceRoot, initializeParams.GetProperty("initializationOptions").GetProperty("workspace").GetString());
		Assert.AreEqual(1, initializeParams.GetProperty("initializationOptions").GetProperty("rootCount").GetInt32());
		Assert.AreEqual(LanguageServerPaths.CreateFileUri(expectedWorkspaceRoot), initializeParams.GetProperty("rootUri").GetString());
		Assert.AreEqual(LanguageServerPaths.CreateFileUri(expectedWorkspaceRoot), workspaceFolder.GetProperty("uri").GetString());
		Assert.AreEqual("Workspace", workspaceFolder.GetProperty("name").GetString());
	}

	[TestMethod]
	public void LanguageServerClientOptions_ServerArgumentsAndEnvironmentVariables_AreCopiedAndNormalized()
	{
		var defaults = LanguageServerClientOptions.Default;

		Assert.IsEmpty(defaults.ServerArguments);
		Assert.IsEmpty(defaults.EnvironmentVariables);

		var arguments = new List<string> { "--log=debug" };
		var variables = new Dictionary<string, string> { ["VAR"] = "value" };

		var configured = LanguageServerClientOptions.Default with
		{
			ServerArguments = arguments,
			EnvironmentVariables = variables
		};

		arguments.Add("--appended-after-assignment");
		variables["VAR"] = "changed-after-assignment";

		Assert.AreEqual(1, configured.ServerArguments.Count);
		Assert.AreEqual("--log=debug", configured.ServerArguments[0]);
		Assert.AreEqual("value", configured.EnvironmentVariables["VAR"]);

		var nulled = LanguageServerClientOptions.Default with
		{
			// The options normalize null collections to empty instances.
			ServerArguments = null!,
			EnvironmentVariables = null!
		};

		Assert.IsEmpty(nulled.ServerArguments);
		Assert.IsEmpty(nulled.EnvironmentVariables);
	}

	[TestMethod]
	public void LanguageServerClientOptions_ServerArgumentsWithNullEntry_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => LanguageServerClientOptions.Default with
		{
			ServerArguments = ["--log=debug", null!]
		});
	}

	[TestMethod]
	public void LanguageServerClientOptions_WithNullSettingsProvider_ThrowsArgumentNullException()
	{
		// A settings provider is always required: the default pushes an empty payload, so an explicit null is a
		// contract violation the options reject at assignment instead of failing the later handshake.
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => LanguageServerClientOptions.Default with
		{
			SettingsProvider = null!
		});

		Assert.AreEqual("SettingsProvider", exception.ParamName);
	}

	[TestMethod]
	public void LanguageServerClientOptions_EnvironmentVariablesWithCasingOnlyDuplicates_UseTheConfiguredComparer()
	{
		var variables = new Dictionary<string, string> { ["VAR"] = "first", ["var"] = "second" };

		// The default ordinal comparer is host-neutral, so the two spellings stay distinct variables.
		var caseSensitive = LanguageServerClientOptions.Default with
		{
			EnvironmentVariables = variables
		};

		Assert.AreEqual(2, caseSensitive.EnvironmentVariables.Count);

		// A host on a case-insensitive platform opts in explicitly, which rejects sibling entries that differ only
		// in casing instead of letting one silently overwrite the other in the child process environment.
		Assert.ThrowsExactly<ArgumentException>(() => LanguageServerClientOptions.Default with
		{
			EnvironmentVariableNameComparer = StringComparer.OrdinalIgnoreCase,
			EnvironmentVariables = variables
		});
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_ReportsServerArgumentsAndEnvironmentVariablesToTheTransportAndTheProcess()
	{
		LanguageServerTransportContext? capturedContext = null;
		string[]? capturedArguments = null;
		string? capturedVariableValue = null;
		string? capturedWorkingDirectory = null;

		var options = LanguageServerClientOptions.Default with
		{
			ServerArguments = ["/c", "ping 127.0.0.1 -n 1 -w 200 > nul"],
			EnvironmentVariables = new Dictionary<string, string> { ["LS_TEST_VARIABLE"] = "ls-test-value" },
			Transport = new TestLanguageServerTransport(async (context, cancellationToken) =>
			{
				capturedContext = context;
				// The stdio transport is the production mapping from the context onto a child process, so the
				// captured process configuration proves both halves of the contract in one place.
				ILanguageServerConnection connection = await StdioLanguageServerTransport.Default
					.ConnectAsync(context, cancellationToken)
					.ConfigureAwait(false);

				Process process = connection.Process
					?? throw new AssertFailedException("The stdio transport must expose the spawned process.");

				capturedArguments = [.. process.StartInfo.ArgumentList];
				capturedWorkingDirectory = process.StartInfo.WorkingDirectory;
				capturedVariableValue = process.StartInfo.Environment.TryGetValue("LS_TEST_VARIABLE", out string? variableValue)
					? variableValue
					: null;

				// The transport owns the connection until it returns it, so it must release what it created here.
				await connection.DisposeAsync().ConfigureAwait(false);

				throw new InvalidOperationException("Simulated startup failure after capturing the process configuration.");
			})
		};

		using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			options,
			logger: null);

		bool started = await client.StartAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.IsFalse(started);
		Assert.IsNotNull(capturedContext);

		CollectionAssert.AreEqual(new[] { "/c", "ping 127.0.0.1 -n 1 -w 200 > nul" }, capturedContext.ServerArguments.ToArray());
		Assert.AreEqual("ls-test-value", capturedContext.EnvironmentVariables["LS_TEST_VARIABLE"]);
		Assert.AreEqual(Path.GetDirectoryName(Path.Combine(Environment.SystemDirectory, "cmd.exe")), capturedContext.ServerWorkingDirectory);

		Assert.IsNotNull(capturedArguments);
		CollectionAssert.AreEqual(new[] { "/c", "ping 127.0.0.1 -n 1 -w 200 > nul" }, capturedArguments);
		Assert.AreEqual("ls-test-value", capturedVariableValue);
		Assert.AreEqual(Environment.SystemDirectory, capturedWorkingDirectory);
	}

	[TestMethod]
	public async Task SendNotificationAsync_WithTypedPascalCaseSettings_PushesAndServesTheSameCasing()
	{
		using var serverInputStream = new RecordingStream();
		using var serverOutputStream = new DeferredPersistentJsonRpcResponseStream();
		await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 1, process: null, serverOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		await client.SendNotificationAsync(
			"workspace/didChangeConfiguration",
			new DidChangeConfigurationParams(new PascalCaseSettings
			{
				Example = new PascalCaseSettingsSection { MaxPreload = 4 }
			}),
			CancellationToken.None).ConfigureAwait(false);

		// The pushed notification and the workspace/configuration callback must use the same member casing.
		string writtenText = await WaitForWrittenTextAsync(serverInputStream, "\"maxPreload\":4").ConfigureAwait(false);

		Assert.IsFalse(writtenText.Contains("\"MaxPreload\"", StringComparison.Ordinal), writtenText);

		object?[] response = client.ProtocolForwarder.BuildConfigurationResponse(
			new WorkspaceConfigurationParams(
			[
				new WorkspaceConfigurationItem("example"),
				new WorkspaceConfigurationItem("example.maxPreload")
			]));

		Assert.AreEqual(2, response.Length);
		Assert.AreEqual(4, JsonSerializer.SerializeToElement(response[1]).GetInt32());
		Assert.AreEqual(4, JsonSerializer.SerializeToElement(response[0]).GetProperty("maxPreload").GetInt32());
	}

	private sealed class PascalCaseSettings
	{
		public PascalCaseSettingsSection? Example { get; init; }
	}

	private sealed class PascalCaseSettingsSection
	{
		public int MaxPreload { get; init; }
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void WorkspaceFolders_ReturnsDriveRootPathWhenWorkspaceRootIsDriveRoot()
	{
		using var client = new LanguageServerClient([@"C:\"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));

		WorkspaceFolder[] workspaceFolders = rpcTarget.WorkspaceFolders();

		Assert.AreEqual(1, workspaceFolders.Length);
		Assert.AreEqual(LanguageServerPaths.CreateFileUri(@"C:\"), workspaceFolders[0].Uri);

		// A drive root has no file name; the folder name is the root path itself, not the misleading "C:".
		Assert.AreEqual(@"C:\", workspaceFolders[0].Name);
	}

	[TestMethod]
	public void WorkspaceConfiguration_StaleTransportGeneration_ReturnsNullValues()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", new LanguageServerClientOptions
		{
			SettingsProvider = static () => new
			{
				Example = new
				{
					Runtime = new
					{
						Version = "5.4"
					}
				}
			}
		});

		TransportSession staleSession = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);
		TransportSession activeSession = CreateTransportSession(client, 2, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, activeSession);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(staleSession));
		object?[] response = rpcTarget.WorkspaceConfiguration(
			new WorkspaceConfigurationParams(
			[
				new WorkspaceConfigurationItem("Example"),
				new WorkspaceConfigurationItem("Example.runtime")
			]));

		Assert.AreEqual(2, response.Length);
		Assert.IsNull(response[0]);
		Assert.IsNull(response[1]);
	}

	[TestMethod]
	public void WorkspaceFolders_StaleTransportGeneration_ReturnsEmptyArray()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession staleSession = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);
		TransportSession activeSession = CreateTransportSession(client, 2, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, activeSession);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(staleSession));
		WorkspaceFolder[] workspaceFolders = rpcTarget.WorkspaceFolders();

		Assert.AreEqual(0, workspaceFolders.Length);
	}

	[TestMethod]
	public void BuildInitializeParams_MultipleRoots_SendsPrimaryRootUriAndAllFolderEntries()
	{
		using var client = new LanguageServerClient([@"C:/Workspace", @"D:/Secondary"], "example-language-server.exe", s_defaultClientOptions);

		JsonElement initializeParams = JsonSerializer.SerializeToElement(client.BuildInitializeParams());
		JsonElement workspaceFolders = initializeParams.GetProperty("workspaceFolders");
		string expectedPrimaryRoot = LanguageServerPaths.NormalizeLocalPath(@"C:/Workspace");
		string expectedSecondaryRoot = LanguageServerPaths.NormalizeLocalPath(@"D:/Secondary");

		Assert.AreEqual(2, workspaceFolders.GetArrayLength());
		Assert.AreEqual(LanguageServerPaths.CreateFileUri(expectedPrimaryRoot), initializeParams.GetProperty("rootUri").GetString());
		Assert.AreEqual(LanguageServerPaths.CreateFileUri(expectedPrimaryRoot), workspaceFolders[0].GetProperty("uri").GetString());
		Assert.AreEqual("Workspace", workspaceFolders[0].GetProperty("name").GetString());
		Assert.AreEqual(LanguageServerPaths.CreateFileUri(expectedSecondaryRoot), workspaceFolders[1].GetProperty("uri").GetString());
		Assert.AreEqual("Secondary", workspaceFolders[1].GetProperty("name").GetString());
	}

	[TestMethod]
	public void BuildInitializeParams_MultipleRoots_PassesNormalizedRootsToProvidersInCallerOrder()
	{
		string[] capabilityRoots = [];
		string[] initializationRoots = [];

		using var client = new LanguageServerClient([@"C:/Workspace/", @"D:/Secondary/"], "example-language-server.exe", LanguageServerClientOptions.Default with
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
			}
		});

		_ = client.BuildInitializeParams();

		CollectionAssert.AreEqual(
			new[] { LanguageServerPaths.NormalizeLocalPath(@"C:/Workspace/"), LanguageServerPaths.NormalizeLocalPath(@"D:/Secondary/") },
			capabilityRoots);
		CollectionAssert.AreEqual(
			new[] { LanguageServerPaths.NormalizeLocalPath(@"C:/Workspace/"), LanguageServerPaths.NormalizeLocalPath(@"D:/Secondary/") },
			initializationRoots);
	}

	[TestMethod]
	public void WorkspaceFolders_MultipleRoots_ReturnsEveryAdvertisedFolder()
	{
		using var client = new LanguageServerClient([@"C:/Workspace", @"D:/Secondary"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));

		WorkspaceFolder[] workspaceFolders = rpcTarget.WorkspaceFolders();

		Assert.AreEqual(2, workspaceFolders.Length);
		Assert.AreEqual(LanguageServerPaths.CreateFileUri(LanguageServerPaths.NormalizeLocalPath(@"C:/Workspace")), workspaceFolders[0].Uri);
		Assert.AreEqual("Workspace", workspaceFolders[0].Name);
		Assert.AreEqual(LanguageServerPaths.CreateFileUri(LanguageServerPaths.NormalizeLocalPath(@"D:/Secondary")), workspaceFolders[1].Uri);
		Assert.AreEqual("Secondary", workspaceFolders[1].Name);
	}

	[TestMethod]
	public void Constructor_WithNullWorkspaceRootList_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new LanguageServerClient(null!, "example-language-server.exe", s_defaultClientOptions));
	}

	[TestMethod]
	public void Constructor_WithEmptyWorkspaceRootList_ModelsAFolderlessSession()
	{
		using var client = new LanguageServerClient([], "example-language-server.exe", s_defaultClientOptions);

		JsonElement initializeParams = JsonSerializer.SerializeToElement(client.BuildInitializeParams());

		Assert.AreEqual(JsonValueKind.Null, initializeParams.GetProperty("rootUri").ValueKind);
		Assert.AreEqual(JsonValueKind.Null, initializeParams.GetProperty("workspaceFolders").ValueKind);
	}

	[TestMethod]
	public void Constructor_WithWhitespaceWorkspaceRootEntry_ThrowsArgumentException()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new LanguageServerClient(["   "], "example-language-server.exe", s_defaultClientOptions));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Constructor_WithNormalizedDuplicateWorkspaceRoots_ThrowsArgumentException()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			new LanguageServerClient([@"C:\Workspace", @"C:/Workspace/"], "example-language-server.exe", s_defaultClientOptions));
	}

	[TestMethod]
	public void Constructor_WithNestedWorkspaceRoots_AcceptsTheConfiguration()
	{
		using var client = new LanguageServerClient([@"C:/Workspace", @"C:/Workspace/Nested"], "example-language-server.exe", s_defaultClientOptions);

		JsonElement initializeParams = JsonSerializer.SerializeToElement(client.BuildInitializeParams());

		Assert.AreEqual(2, initializeParams.GetProperty("workspaceFolders").GetArrayLength());
	}

	[TestMethod]
	public async Task StartAsync_WithTheDefaultSettingsProvider_PushesAnEmptyConfigurationPayload()
	{
		List<ScriptedStartupAttempt> attempts = [];

		await using var client = CreateScriptedStartupClient(attempts);

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		ScriptedStartupAttempt attempt = await CompleteScriptedStartupAsync(startTask, attempts, 0, ReadyCapabilitiesJson).ConfigureAwait(false);

		// The configuration push is written after the handshake completes, so the recording is waited on rather
		// than sampled: the settings body is the later part of that write, so waiting for it also proves the
		// notification frame was written. Asserting immediately raced the writer under load.
		string writtenText = await WaitForWrittenTextAsync(attempt.Recording, "\"settings\":{}").ConfigureAwait(false);

		// A host that never configures settings must still push a valid (empty) configuration so the server does
		// not have to guess whether the client supports the notification.
		Assert.IsTrue(writtenText.Contains("\"method\":\"workspace/didChangeConfiguration\"", StringComparison.Ordinal), writtenText);
		Assert.IsTrue(writtenText.Contains("\"settings\":{}", StringComparison.Ordinal), writtenText);
	}

	[TestMethod]
	public async Task StartAsync_WithASettingsProviderThatReturnsNull_FailsStartupWithArgumentNullException()
	{
		List<ScriptedStartupAttempt> attempts = [];

		await using var client = CreateScriptedStartupClient(attempts, new LanguageServerClientOptions { SettingsProvider = static () => null! });

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		await TestPolling.UntilAsync(
			() => attempts.Count > 0,
			TestPolling.DefaultTimeout,
			"Timed out waiting for the scripted startup attempt to begin.").ConfigureAwait(false);

		ScriptedStartupAttempt attempt = attempts[0];
		int requestId = await WaitForRequestIdAsync(attempt.Recording).ConfigureAwait(false);
		attempt.Responses.SetPayload(JsonRpcFrameTestHelper.BuildResultResponse(requestId, ReadyCapabilitiesJson));

		// The settings push is part of the handshake, so a provider that violates its non-null contract must fail
		// the startup attempt with the contract exception instead of publishing readiness with a null payload.
		Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		Assert.IsInstanceOfType<ArgumentNullException>(client.LastStartupException);
		Assert.IsFalse(client.IsReady);
	}
}
