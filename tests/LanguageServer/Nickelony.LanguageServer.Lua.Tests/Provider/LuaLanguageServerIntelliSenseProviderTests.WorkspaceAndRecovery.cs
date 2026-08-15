using Nickelony.IDEKit.IntelliSense;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using Nickelony.IDEKit.IntelliSense.Signatures;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task DispatchWorkspaceFileChangesAsync_RefreshesConfigurationWhenLuaLsConfigurationChanges()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaConfigRefresh_" + Guid.NewGuid().ToString("N"));
		string configurationFilePath = Path.Combine(workspaceRoot, ".luarc.json");

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(
				workspaceRoot,
				client,
				out _,
				new LuaLanguageServerOptions { AdditionalLibraryDirectories = [Path.Combine(TestPaths.Root, "Libraries", "Extra")] });

			await StartProviderAndCaptureWorkspaceWatcherAsync(provider, client);

			var batch = new FileChangeBatch(
			[
				new WorkspaceFileChange(configurationFilePath, FileChangeKind.Changed)
			]);

			await DispatchWorkspaceFileChangesAsync(provider, batch, CancellationToken.None);

			CollectionAssert.AreEqual(
				new[] { "workspace/didChangeConfiguration", "workspace/didChangeWatchedFiles" },
				client.GetSentMethodNames());

			JsonElement settings = client.GetLastNotificationParameters("workspace/didChangeConfiguration")
				.GetProperty("settings")
				.GetProperty("Lua")
				.GetProperty("workspace")
				.GetProperty("library");

			Assert.AreEqual(1, settings.GetArrayLength());
			Assert.AreEqual(Path.Combine(TestPaths.Root, "Libraries", "Extra"), settings[0].GetString());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task DispatchWorkspaceFileChangesAsync_ConfigurationRefreshUsesProviderOptions()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaConfigRefreshOptions_" + Guid.NewGuid().ToString("N"));
		string configurationFilePath = Path.Combine(workspaceRoot, ".luarc.json");

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(
				workspaceRoot,
				client,
				out _,
				new LuaLanguageServerOptions { RuntimeVersion = "LuaJIT" });

			await StartProviderAndCaptureWorkspaceWatcherAsync(provider, client);

			var batch = new FileChangeBatch(
			[
				new WorkspaceFileChange(configurationFilePath, FileChangeKind.Changed)
			]);

			await DispatchWorkspaceFileChangesAsync(provider, batch, CancellationToken.None);

			JsonElement settings = client.GetLastNotificationParameters("workspace/didChangeConfiguration")
				.GetProperty("settings")
				.GetProperty("Lua");

			Assert.AreEqual("LuaJIT", settings.GetProperty("runtime").GetProperty("version").GetString());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task DispatchWorkspaceFileChangesAsync_ReplaysDeferredChangesAfterStartupRecovery()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaDeferredWorkspaceReplay_" + Guid.NewGuid().ToString("N"));
		string configurationFilePath = Path.Combine(workspaceRoot, ".luarc.json");
		string scriptFilePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeLanguageServerClient
			{
				IsReady = false,
				StartResult = false,
				HoverResponse = JsonSerializer.SerializeToElement(new
				{
					contents = new
					{
						kind = "markdown",
						value = "Hover docs."
					}
				})
			};

			using var provider = CreateProviderWithWatcherCapture(
				workspaceRoot,
				client,
				out _);

			await StartProviderAndCaptureWorkspaceWatcherAsync(provider, client);

			var batch = new FileChangeBatch(
			[
				new WorkspaceFileChange(configurationFilePath, FileChangeKind.Changed)
			]);

			await DispatchWorkspaceFileChangesAsync(provider, batch, CancellationToken.None);

			Assert.AreEqual(0, client.GetSentMethodNames().Length);

			client.StartResult = true;

			TextHoverInfo? hover = await provider.GetHoverAsync(scriptFilePath, "local value = 1", new TextPosition(0, 0));

			Assert.IsNotNull(hover);

			CollectionAssert.AreEqual(
				new[]
				{
					"workspace/didChangeConfiguration",
					"workspace/didChangeWatchedFiles",
					"textDocument/didOpen",
					"textDocument/hover"
				},
				client.GetSentMethodNames());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task DispatchWorkspaceFileChangesAsync_ReplaysBufferedChangesAfterWorkspaceNotificationTransportFailure()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaDeferredWorkspaceTransport_" + Guid.NewGuid().ToString("N"));
		string changedFilePath = Path.Combine(workspaceRoot, "Scripts", "generated.lua");
		string scriptFilePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(changedFilePath) ?? workspaceRoot);

			using var client = new FakeLanguageServerClient
			{
				ThrowIOExceptionOnNextWatchedFilesNotification = true,
				HoverResponse = JsonSerializer.SerializeToElement(new
				{
					contents = new
					{
						kind = "markdown",
						value = "Hover docs."
					}
				})
			};

			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await StartProviderAndCaptureWorkspaceWatcherAsync(provider, client);

			var batch = new FileChangeBatch(
			[
				new WorkspaceFileChange(changedFilePath, FileChangeKind.Changed)
			]);

			await DispatchWorkspaceFileChangesAsync(provider, batch, CancellationToken.None);

			CollectionAssert.AreEqual(
				new[] { "workspace/didChangeWatchedFiles" },
				client.GetSentMethodNames());

			TextHoverInfo? hover = await provider.GetHoverAsync(scriptFilePath, "local value = 1", new TextPosition(0, 0));

			Assert.IsNotNull(hover);

			CollectionAssert.AreEqual(
				new[]
				{
					"workspace/didChangeWatchedFiles",
					"workspace/didChangeWatchedFiles",
					"textDocument/didOpen",
					"textDocument/hover"
				},
				client.GetSentMethodNames());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task GetHoverAsync_ReplaysDeferredWorkspaceChangesAfterReplayCancellation()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaDeferredWorkspaceCancellation_" + Guid.NewGuid().ToString("N"));
		string changedFilePath = Path.Combine(workspaceRoot, "Scripts", "generated.lua");
		string scriptFilePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(changedFilePath) ?? workspaceRoot);

			using var client = new FakeLanguageServerClient
			{
				IsReady = false,
				StartResult = false,
				HoverResponse = JsonSerializer.SerializeToElement(new
				{
					contents = new
					{
						kind = "markdown",
						value = "Hover docs."
					}
				})
			};

			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await StartProviderAndCaptureWorkspaceWatcherAsync(provider, client);

			var batch = new FileChangeBatch(
			[
				new WorkspaceFileChange(changedFilePath, FileChangeKind.Changed)
			]);

			await DispatchWorkspaceFileChangesAsync(provider, batch, CancellationToken.None);

			client.StartResult = true;

			using var cancellationTokenSource = new CancellationTokenSource();
			cancellationTokenSource.Cancel();

			Task<TextHoverInfo?> canceledHoverTask = provider.GetHoverAsync(scriptFilePath, "local value = 1", new TextPosition(0, 0), cancellationTokenSource.Token);
			TextHoverInfo? recoveredHover = await provider.GetHoverAsync(scriptFilePath, "local value = 1", new TextPosition(0, 0));

			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => canceledHoverTask);
			Assert.IsNotNull(recoveredHover);

			CollectionAssert.AreEqual(
				new[]
				{
					"workspace/didChangeWatchedFiles",
					"textDocument/didOpen",
					"textDocument/hover"
				},
				client.GetSentMethodNames());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task DispatchWorkspaceFileChangesAsync_UnexpectedNotificationFailureDoesNotMarkTransportUnhealthyAndDropsChanges()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaDeferredWorkspaceUnexpected_" + Guid.NewGuid().ToString("N"));
		string changedFilePath = Path.Combine(workspaceRoot, "Scripts", "generated.lua");
		string scriptFilePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(changedFilePath) ?? workspaceRoot);

			using var client = new FakeLanguageServerClient
			{
				ThrowInvalidOperationOnNextWatchedFilesNotification = true,
				HoverResponse = JsonSerializer.SerializeToElement(new
				{
					contents = new
					{
						kind = "markdown",
						value = "Hover docs."
					}
				})
			};

			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await StartProviderAndCaptureWorkspaceWatcherAsync(provider, client);

			var batch = new FileChangeBatch(
			[
				new WorkspaceFileChange(changedFilePath, FileChangeKind.Changed)
			]);

			await DispatchWorkspaceFileChangesAsync(provider, batch, CancellationToken.None);

			TextHoverInfo? hover = await provider.GetHoverAsync(scriptFilePath, "local value = 1", new TextPosition(0, 0));

			Assert.IsNotNull(hover);
			Assert.AreEqual(0, client.MarkTransportUnhealthyCallCount);
			Assert.AreEqual(1, client.StartCallCount);

			CollectionAssert.AreEqual(
				new[]
				{
					"workspace/didChangeWatchedFiles",
					"textDocument/didOpen",
					"textDocument/hover"
				},
				client.GetSentMethodNames());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task DispatchWorkspaceFileChangesAsync_StaleWatcherTransportFailureDoesNotInvalidateRestartedTransport()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaDeferredWorkspaceStaleTransport_" + Guid.NewGuid().ToString("N"));
		string changedFilePath = Path.Combine(workspaceRoot, "Scripts", "generated.lua");
		string scriptFilePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(changedFilePath) ?? workspaceRoot);

			using var client = new FakeLanguageServerClient
			{
				HoverResponse = JsonSerializer.SerializeToElement(new
				{
					contents = new
					{
						kind = "markdown",
						value = "Hover docs."
					}
				})
			};

			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await StartProviderAndCaptureWorkspaceWatcherAsync(provider, client);

			client.BlockNextWatchedFilesNotification();
			client.ThrowIOExceptionAfterWatchedFilesNotificationGateRelease = true;

			var batch = new FileChangeBatch(
			[
				new WorkspaceFileChange(changedFilePath, FileChangeKind.Changed)
			]);

			Task dispatchTask = DispatchWorkspaceFileChangesAsync(provider, batch, CancellationToken.None);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

			client.IsReady = false;

			Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(scriptFilePath, "local value = 1", new TextPosition(0, 0));

			await TestPolling.UntilAsync(
				() => client.StartCallCount >= 2,
				TestPolling.DefaultTimeout,
				"The hover must drive the next startup attempt.").ConfigureAwait(false);

			Assert.AreEqual(2, client.StartCallCount);

			client.ReleaseWatchedFilesNotification();

			await dispatchTask.ConfigureAwait(false);
			Assert.IsNotNull(await hoverTask.ConfigureAwait(false));

			TextHoverInfo? followUpHover = await provider.GetHoverAsync(scriptFilePath, "local value = 2", new TextPosition(0, 0)).ConfigureAwait(false);

			Assert.IsNotNull(followUpHover);
			Assert.AreEqual(0, client.MarkTransportUnhealthyCallCount);
			Assert.AreEqual(2, client.StartCallCount);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task DispatchWorkspaceFileChangesAsync_RepeatedTransportFailuresReplayEachBufferedBatchOnce()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaDeferredWorkspaceRepeated_" + Guid.NewGuid().ToString("N"));
		string scriptFilePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(scriptFilePath) ?? workspaceRoot);

			using var client = new FakeLanguageServerClient
			{
				HoverResponse = JsonSerializer.SerializeToElement(new
				{
					contents = new
					{
						kind = "markdown",
						value = "Hover docs."
					}
				})
			};

			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await StartProviderAndCaptureWorkspaceWatcherAsync(provider, client);

			for (int i = 1; i <= 3; i++)
			{
				string changedFilePath = Path.Combine(workspaceRoot, "Scripts", $"generated{i}.lua");

				var batch = new FileChangeBatch(
				[
					new WorkspaceFileChange(changedFilePath, FileChangeKind.Changed)
				]);

				client.ThrowIOExceptionOnNextWatchedFilesNotification = true;

				await DispatchWorkspaceFileChangesAsync(provider, batch, CancellationToken.None);

				TextHoverInfo? hover = await provider.GetHoverAsync(scriptFilePath, $"local value = {i}", new TextPosition(0, 0));

				Assert.IsNotNull(hover);
				Assert.AreEqual(i, client.MarkTransportUnhealthyCallCount);
				Assert.AreEqual(i * 2, CountSentMethods(client, "workspace/didChangeWatchedFiles"));

				JsonElement replayPayload = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");

				Assert.AreEqual(1, replayPayload.GetArrayLength());
				Assert.AreEqual(new Uri(changedFilePath).AbsoluteUri, replayPayload[0].GetProperty("uri").GetString());
			}
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task GetHoverAsync_RaisesTransientAndPermanentStartupFailuresOnceEach()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			IsReady = false,
			StartResult = false
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var failures = new List<LanguageServerStartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));
		await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));
		await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));
		await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

		Assert.AreEqual(2, failures.Count);
		Assert.IsFalse(failures[0].IsPersistent);
		Assert.IsTrue(failures[1].IsPersistent);
		Assert.AreEqual(3, client.StartCallCount);
	}

	[TestMethod]
	public async Task GetHoverAsync_ReturnsParsedHoverInfoFromTypedResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextHoverInfo? hover = await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

		Assert.IsNotNull(hover);
		Assert.AreEqual("Hover docs.", hover.Content);
		Assert.IsTrue(hover.ContentKind == TextMarkupKind.Markdown);

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/hover" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetDefinitionAsync_ReturnsParsedDefinitionLocationFromTypedResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		string targetPath = TestPaths.Script("definitions.lua");

		using var client = new FakeLanguageServerClient
		{
			DefinitionResponse = JsonSerializer.SerializeToElement(new
			{
				uri = new Uri(targetPath).AbsoluteUri,
				range = new
				{
					start = new { line = 4, character = 2 },
					end = new { line = 4, character = 7 }
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextDefinitionLocation? definition = await provider.GetDefinitionAsync(filePath, "value", new TextPosition(0, 0));

		Assert.IsNotNull(definition);
		Assert.AreEqual(targetPath, definition.DocumentId);
		Assert.AreEqual(new TextPositionRange(new TextPosition(4, 2), new TextPosition(4, 7)), definition.TargetRange);
		Assert.IsNull(definition.SelectionRange);

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/definition" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetDefinitionAsync_NullServerResponse_ReturnsTheNullFallback()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		// A configured JSON null models a server that answers the request with null instead of a
		// location list; the provider must send the request and surface its documented null fallback.
		using var client = new FakeLanguageServerClient
		{
			DefinitionResponse = JsonSerializer.SerializeToElement<object?>(null)
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextDefinitionLocation? definition = await provider.GetDefinitionAsync(filePath, "value", new TextPosition(0, 0));

		Assert.IsNull(definition);
		Assert.AreEqual(1, CountSentMethods(client, "textDocument/definition"));
	}

	[TestMethod]
	public async Task GetDefinitionAsync_InvalidFilePath_ReturnsNullWithoutSending()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextDefinitionLocation? definition = await provider.GetDefinitionAsync("   ", "local value = 1", new TextPosition(0, 0));

		Assert.IsNull(definition);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
		Assert.AreEqual(0, client.StartCallCount);
	}

	[TestMethod]
	public async Task GetReferencesAsync_ReturnsParsedReferenceLocationsFromTypedResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		string targetPath = TestPaths.Script("references.lua");

		using var client = new FakeLanguageServerClient
		{
			ReferencesResponse = JsonSerializer.SerializeToElement(new object[]
			{
				new
				{
					uri = new Uri(targetPath).AbsoluteUri,
					range = new
					{
						start = new { line = 2, character = 4 },
						end = new { line = 2, character = 9 }
					}
				},
				new
				{
					uri = "https://example.com/not-a-file.lua",
					range = new
					{
						start = new { line = 0, character = 0 },
						end = new { line = 0, character = 1 }
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextReferenceLocation> references = await provider.GetReferencesAsync(new LanguageServerReferenceRequest(filePath, "value", new TextPosition(0, 0)));

		Assert.AreEqual(1, references.Count);
		Assert.AreEqual(targetPath, references[0].FilePath);
		Assert.AreEqual(new TextPosition(2, 4), references[0].Range.Start);

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/references" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetReferencesAsync_DefaultsIncludeDeclarationToTrue()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			ReferencesResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.GetReferencesAsync(new LanguageServerReferenceRequest(filePath, "value", new TextPosition(0, 0)));

		JsonElement parameters = client.GetLastRequestParameters("textDocument/references");

		Assert.IsTrue(parameters.GetProperty("context").GetProperty("includeDeclaration").GetBoolean());
	}

	[TestMethod]
	public async Task GetReferencesAsync_WhenIncludeDeclarationIsFalse_SendsFalseInPayload()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			ReferencesResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.GetReferencesAsync(new LanguageServerReferenceRequest(filePath, "value", new TextPosition(0, 0), includeDeclaration: false));

		JsonElement parameters = client.GetLastRequestParameters("textDocument/references");

		Assert.IsFalse(parameters.GetProperty("context").GetProperty("includeDeclaration").GetBoolean());
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_ReturnsParsedSignatureHelpFromTypedResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SignatureHelpResponse = JsonSerializer.SerializeToElement(new
			{
				activeSignature = 0,
				activeParameter = 1,
				signatures = new[]
				{
					new
					{
						label = "spawn(room, objectName)",
						documentation = new
						{
							kind = "markdown",
							value = "Spawns an object."
						},
						parameters = new object[]
						{
							new
							{
								label = new[] { 6, 10 },
								documentation = "Room id."
							},
							new
							{
								label = new[] { 12, 22 },
								documentation = "Object name."
							}
						}
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextSignatureHelp? signature = await provider.GetSignatureHelpAsync(new LanguageServerSignatureHelpRequest(filePath, "spawn(", new TextPosition(0, 6)));

		Assert.IsNotNull(signature);
		Assert.AreEqual("spawn(room, objectName)", signature.ActiveSignature.Label);
		Assert.AreEqual("Spawns an object.", signature.ActiveSignature.Documentation);
		Assert.AreEqual(1, signature.ActiveParameterIndex);
		Assert.AreEqual(2, signature.ActiveSignature.Parameters.Count);
		Assert.AreEqual("objectName", signature.ActiveSignature.Parameters[1].Label);
		Assert.AreEqual("Object name.", signature.ActiveSignature.Parameters[1].Documentation);

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/signatureHelp" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task DispatchWorkspaceFileChangesAsync_RefreshesConfigurationFromNonPrimaryRoot()
	{
		string primaryRoot = Path.Combine(Path.GetTempPath(), "LuaMultiRootConfigPrimary_" + Guid.NewGuid().ToString("N"));
		string secondaryRoot = Path.Combine(Path.GetTempPath(), "LuaMultiRootConfigSecondary_" + Guid.NewGuid().ToString("N"));
		string configurationFilePath = Path.Combine(secondaryRoot, ".luarc.json");

		try
		{
			Directory.CreateDirectory(primaryRoot);
			Directory.CreateDirectory(secondaryRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture([primaryRoot, secondaryRoot], client, out _);

			await StartProviderAndCaptureWorkspaceWatcherAsync(provider, client, primaryRoot);

			var batch = new FileChangeBatch(
			[
				new WorkspaceFileChange(configurationFilePath, FileChangeKind.Changed)
			]);

			await DispatchWorkspaceFileChangesAsync(provider, secondaryRoot, batch, CancellationToken.None);

			CollectionAssert.AreEqual(
				new[] { "workspace/didChangeConfiguration", "workspace/didChangeWatchedFiles" },
				client.GetSentMethodNames());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(primaryRoot);
			TemporaryDirectory.DeleteBestEffort(secondaryRoot);
		}
	}
}
