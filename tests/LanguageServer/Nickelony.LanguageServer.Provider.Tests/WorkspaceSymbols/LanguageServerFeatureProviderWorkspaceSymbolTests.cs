using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the workspace-symbol request and the resolution of a listed symbol through the framework.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderWorkspaceSymbolTests
{
	private static string UriOf(string name) => DocumentUriConversion.CreateFileUri(TestPaths.Script(name));

	private static object RangeOf(int line, int start, int end) =>
		new { start = new { line, character = start }, end = new { line, character = end } };

	private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

	private async Task<(FakeStatefulLanguageServerClient Client, LanguageServerFeatureProvider Provider)> StartAsync(
		Action<FakeStatefulLanguageServerClient> configure
	)
	{
		var client = new FakeStatefulLanguageServerClient();

		configure(client);

		LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		Assert.IsTrue(await client.EnsureStartedAsync(TestContext.CancellationToken));

		return (client, provider);
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_SendsTheQueryAndMapsASymbolInformationEntry()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "spawn",
						kind = 12,
						containerName = "world",
						location = new { uri = UriOf("world.test"), range = RangeOf(4, 9, 14) },
					},
				}
			)
		);

		using (client)
		{
			using (provider)
			{
				IReadOnlyList<WorkspaceSymbol> symbols = await provider.GetWorkspaceSymbolsAsync(
					new WorkspaceSymbolRequest("spa"),
					TestContext.CancellationToken
				);

				Assert.AreEqual("spa", client.GetLastRequestParameters("workspace/symbol").GetProperty("query").GetString());
				Assert.HasCount(1, symbols);
				Assert.AreEqual("spawn", symbols[0].Name);
				Assert.AreEqual(DocumentSymbolKind.Function, symbols[0].Kind);
				Assert.AreEqual("world", symbols[0].ContainerName);
				Assert.AreEqual(TestPaths.Script("world.test"), symbols[0].Location.DocumentId);
				Assert.AreEqual(new TextPositionRange(new TextPosition(4, 9), new TextPosition(4, 14)), symbols[0].Location.Range);
			}
		}
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_MapsAWorkspaceSymbolEntryWhoseLocationNamesNoRange()
	{
		// Synthetic: neither LuaLS nor Roslyn sends a bare { uri } location, so this entry is built from the specification.
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "Util",
						kind = 5,
						tags = new[] { 1 },
						location = new { uri = UriOf("util.test") },
						data = new { id = 17 },
					},
				}
			)
		);

		using (client)
		{
			using (provider)
			{
				IReadOnlyList<WorkspaceSymbol> symbols = await provider.GetWorkspaceSymbolsAsync(
					new WorkspaceSymbolRequest("ut"),
					TestContext.CancellationToken
				);

				Assert.HasCount(1, symbols);
				Assert.AreEqual(DocumentSymbolKind.Class, symbols[0].Kind);
				Assert.AreEqual(TestPaths.Script("util.test"), symbols[0].Location.DocumentId);
				Assert.IsNull(symbols[0].Location.Range);
				Assert.IsNull(symbols[0].ContainerName);
				Assert.IsNotNull(symbols[0].Data);
			}
		}
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_ReadsBothResultShapesOfOneAnswerInServerOrder()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "flat",
						kind = 13,
						location = new { uri = UriOf("a.test"), range = RangeOf(0, 0, 4) },
					},

					new
					{
						name = "lazy",
						kind = 13,
						location = new { uri = UriOf("b.test") },
					},
				}
			)
		);

		using (client)
		{
			using (provider)
			{
				IReadOnlyList<WorkspaceSymbol> symbols = await provider.GetWorkspaceSymbolsAsync(
					new WorkspaceSymbolRequest("a"),
					TestContext.CancellationToken
				);

				Assert.AreSequenceEqual(["flat", "lazy"], [.. symbols.Select(symbol => symbol.Name)]);
				Assert.IsNotNull(symbols[0].Location.Range);
				Assert.IsNull(symbols[1].Location.Range);
			}
		}
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_DropsASymbolWhoseUriIsNoLocalFile()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "metadata",
						kind = 5,
						location = new { uri = "csharp:/metadata/projects/x/List.cs", range = RangeOf(0, 0, 4) },
					},

					new
					{
						name = "local",
						kind = 5,
						location = new { uri = UriOf("a.test"), range = RangeOf(0, 0, 4) },
					},
				}
			)
		);

		using (client)
		{
			using (provider)
			{
				IReadOnlyList<WorkspaceSymbol> symbols = await provider.GetWorkspaceSymbolsAsync(
					new WorkspaceSymbolRequest("a"),
					TestContext.CancellationToken
				);

				Assert.AreEqual("local", symbols.Single().Name);
			}
		}
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_MapsAKindThatTheProtocolDoesNotDefineToFile()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "future",
						kind = 99,
						location = new { uri = UriOf("a.test") },
					},
				}
			)
		);

		using (client)
		{
			using (provider)
			{
				IReadOnlyList<WorkspaceSymbol> symbols = await provider.GetWorkspaceSymbolsAsync(
					new WorkspaceSymbolRequest("f"),
					TestContext.CancellationToken
				);

				Assert.AreEqual(DocumentSymbolKind.File, symbols.Single().Kind);
			}
		}
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_ForAMalformedRange_KeepsTheSymbolWithoutARange()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "odd",
						kind = 13,
						location = new { uri = UriOf("a.test"), range = RangeOf(-1, 0, 4) },
					},
				}
			)
		);

		using (client)
		{
			using (provider)
			{
				IReadOnlyList<WorkspaceSymbol> symbols = await provider.GetWorkspaceSymbolsAsync(
					new WorkspaceSymbolRequest("o"),
					TestContext.CancellationToken
				);

				Assert.IsNull(symbols.Single().Location.Range);
			}
		}
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_WhenTheServerDoesNotAdvertiseTheRequest_AnswersEmptyWithoutARequest()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
			client.SupportsWorkspaceSymbols = false
		);

		using (client)
		{
			using (provider)
			{
				IReadOnlyList<WorkspaceSymbol> symbols = await provider.GetWorkspaceSymbolsAsync(
					new WorkspaceSymbolRequest("a"),
					TestContext.CancellationToken
				);

				Assert.IsEmpty(symbols);
				Assert.AreEqual(0, client.GetSentMethodCount("workspace/symbol"));
			}
		}
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_StartsAStoppedServerAndAnswersOnceItInitialized()
	{
		using var client = new FakeStatefulLanguageServerClient
		{
			WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "Util",
						kind = 5,
						location = new { uri = UriOf("util.test"), range = RangeOf(1, 0, 4) },
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		Assert.AreEqual(0, client.StartCallCount);

		IReadOnlyList<WorkspaceSymbol> symbols = await provider.GetWorkspaceSymbolsAsync(
			new WorkspaceSymbolRequest("ut"),
			TestContext.CancellationToken
		);

		Assert.IsGreaterThanOrEqualTo(1, client.StartCallCount);
		Assert.AreEqual("Util", symbols.Single().Name);
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_ASearchThatStartsTheServerWaitsOnlyForTheRequestTimeoutAndTheStartGoesOn()
	{
		using var client = new FakeStatefulLanguageServerClient
		{
			WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "Util",
						kind = 5,
						location = new { uri = UriOf("util.test"), range = RangeOf(1, 0, 4) },
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[TestPaths.Root],
			client,

			LanguageServerFeatureProviderOptions.Default with
			{
				RequestTimeout = TimeSpan.FromMilliseconds(200),
			}
		);

		client.BlockNextEnsureStartedAsync();

		IReadOnlyList<WorkspaceSymbol> early = await provider.GetWorkspaceSymbolsAsync(
			new WorkspaceSymbolRequest("ut"),
			TestContext.CancellationToken
		);

		Assert.IsEmpty(early);
		Assert.AreEqual(0, client.GetSentMethodCount("workspace/symbol"));

		client.ReleaseEnsureStartedAsync();

		IReadOnlyList<WorkspaceSymbol> late = await provider.GetWorkspaceSymbolsAsync(
			new WorkspaceSymbolRequest("ut"),
			TestContext.CancellationToken
		);

		Assert.AreEqual("Util", late.Single().Name);
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_ANewerSearchThatCancelsTheOlderOne_DoesNotCancelTheStart()
	{
		using var client = new FakeStatefulLanguageServerClient
		{
			WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "Util",
						kind = 5,
						location = new { uri = UriOf("util.test"), range = RangeOf(1, 0, 4) },
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);
		using var superseded = new CancellationTokenSource();

		client.BlockNextEnsureStartedAsync();

		Task<IReadOnlyList<WorkspaceSymbol>> older = provider.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("u"), superseded.Token);

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => client.StartCallCount >= 1, TestPolling.DefaultTimeout));

		await superseded.CancelAsync();
		await Assert.ThrowsAsync<OperationCanceledException>(() => older);

		Task<IReadOnlyList<WorkspaceSymbol>> newer = provider.GetWorkspaceSymbolsAsync(
			new WorkspaceSymbolRequest("ut"),
			TestContext.CancellationToken
		);

		client.ReleaseEnsureStartedAsync();

		Assert.AreEqual("Util", (await newer).Single().Name);
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(1, client.StartCallCount, "The newer search must join the start the older one began.");
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_WithoutAClient_AnswersEmpty()
	{
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client: null);

		IReadOnlyList<WorkspaceSymbol> symbols = await provider.GetWorkspaceSymbolsAsync(
			new WorkspaceSymbolRequest("a"),
			TestContext.CancellationToken
		);

		Assert.IsEmpty(symbols);
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_WhenTheServerFailsToStart_AnswersEmptyWithoutARequest()
	{
		using var client = new FakeStatefulLanguageServerClient { StartResult = false, WorkspaceSymbolResponse = Json(Array.Empty<object>()) };
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		IReadOnlyList<WorkspaceSymbol> symbols = await provider.GetWorkspaceSymbolsAsync(
			new WorkspaceSymbolRequest("a"),
			TestContext.CancellationToken
		);

		Assert.IsEmpty(symbols);
		Assert.AreEqual(0, client.GetSentMethodCount("workspace/symbol"));
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_WhenTheServerAnswersNull_ReturnsAnEmptyList()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
			client.WorkspaceSymbolResponse = JsonSerializer.SerializeToElement<object?>(null)
		);

		using (client)
		{
			using (provider)
				Assert.IsEmpty(await provider.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("a"), TestContext.CancellationToken));
		}
	}

	[TestMethod]
	public async Task GetWorkspaceSymbolsAsync_CancelsTheRequestWithTheCallersToken()
	{
		CancellationToken observed = default;

		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
			client.WorkspaceSymbolHandler = async (_, token) =>
			{
				observed = token;
				await Task.Delay(Timeout.Infinite, token);

				return Json(Array.Empty<object>());
			}
		);

		using (client)
		{
			using (provider)
			{
				using var query = new CancellationTokenSource();
				Task<IReadOnlyList<WorkspaceSymbol>> pending = provider.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("a"), query.Token);

				Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/symbol", 1, TestPolling.DefaultTimeout));
				Assert.IsFalse(observed.IsCancellationRequested);

				await query.CancelAsync();

				Assert.IsTrue(observed.IsCancellationRequested);
				await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_SendsTheListedSymbolBackAndTakesTheRangeOfTheAnswer()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
		{
			client.SupportsWorkspaceSymbolResolve = true;

			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "Util",
						kind = 5,
						location = new { uri = UriOf("util.test") },
						data = new { id = 17 },
					},
				}
			);

			client.WorkspaceSymbolResolveResponse = Json(
				new
				{
					name = "Util",
					kind = 5,
					location = new { uri = UriOf("util.test"), range = RangeOf(7, 6, 10) },
					data = new { id = 17 },
				}
			);
		});

		using (client)
		{
			using (provider)
			{
				WorkspaceSymbol listed = (
					await provider.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("ut"), TestContext.CancellationToken)
				).Single();

				WorkspaceSymbol resolved = await ((IWorkspaceSymbolResolver)provider).ResolveAsync(listed, TestContext.CancellationToken);

				JsonElement sent = client.GetLastRequestParameters("workspaceSymbol/resolve");

				Assert.AreEqual("Util", sent.GetProperty("name").GetString());
				Assert.AreEqual(5, sent.GetProperty("kind").GetInt32());
				Assert.AreEqual(UriOf("util.test"), sent.GetProperty("location").GetProperty("uri").GetString());
				Assert.IsFalse(sent.GetProperty("location").TryGetProperty("range", out _));
				Assert.AreEqual(17, sent.GetProperty("data").GetProperty("id").GetInt32());

				Assert.AreNotSame(listed, resolved);
				Assert.AreEqual(new TextPositionRange(new TextPosition(7, 6), new TextPosition(7, 10)), resolved.Location.Range);
				Assert.AreEqual(TestPaths.Script("util.test"), resolved.Location.DocumentId);
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_ForASymbolThatHasARange_SendsNoRequest()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
		{
			client.SupportsWorkspaceSymbolResolve = true;

			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "Util",
						kind = 5,
						location = new { uri = UriOf("util.test"), range = RangeOf(1, 0, 4) },
					},
				}
			);
		});

		using (client)
		{
			using (provider)
			{
				WorkspaceSymbol listed = (
					await provider.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("ut"), TestContext.CancellationToken)
				).Single();

				WorkspaceSymbol resolved = await ((IWorkspaceSymbolResolver)provider).ResolveAsync(listed, TestContext.CancellationToken);

				Assert.AreSame(listed, resolved);
				Assert.AreEqual(0, client.GetSentMethodCount("workspaceSymbol/resolve"));
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_WhenTheServerCannotResolve_SendsNoRequest()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "Util",
						kind = 5,
						location = new { uri = UriOf("util.test") },
					},
				}
			)
		);

		using (client)
		{
			using (provider)
			{
				WorkspaceSymbol listed = (
					await provider.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("ut"), TestContext.CancellationToken)
				).Single();

				WorkspaceSymbol resolved = await ((IWorkspaceSymbolResolver)provider).ResolveAsync(listed, TestContext.CancellationToken);

				Assert.AreSame(listed, resolved);
				Assert.AreEqual(0, client.GetSentMethodCount("workspaceSymbol/resolve"));
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_ForASymbolOfAnotherProvider_SendsNoRequest()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
			client.SupportsWorkspaceSymbolResolve = true
		);

		using (client)
		{
			using (provider)
			{
				var foreign = new WorkspaceSymbol("Util", DocumentSymbolKind.Class, new WorkspaceSymbolLocation(TestPaths.Script("util.test")));

				WorkspaceSymbol resolved = await ((IWorkspaceSymbolResolver)provider).ResolveAsync(foreign, TestContext.CancellationToken);

				Assert.AreSame(foreign, resolved);
				Assert.AreEqual(0, client.GetSentMethodCount("workspaceSymbol/resolve"));
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_ForASymbolThatAnotherProviderListed_SendsNoRequestToThisServer()
	{
		(FakeStatefulLanguageServerClient listing, LanguageServerFeatureProvider lister) = await StartAsync(client =>
		{
			client.SupportsWorkspaceSymbolResolve = true;

			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "Util",
						kind = 5,
						location = new { uri = UriOf("util.test") },
						data = new { server = "A", id = 17 },
					},
				}
			);
		});

		(FakeStatefulLanguageServerClient other, LanguageServerFeatureProvider stranger) = await StartAsync(client =>
		{
			client.SupportsWorkspaceSymbolResolve = true;
			client.WorkspaceSymbolResponse = Json(Array.Empty<object>());

			client.WorkspaceSymbolResolveResponse = Json(
				new
				{
					name = "Other",
					kind = 12,
					location = new { uri = UriOf("other.test"), range = RangeOf(40, 1, 6) },
				}
			);
		});

		using (listing)
		{
			using (lister)
			{
				using (other)
				{
					using (stranger)
					{
						WorkspaceSymbol listed = (
							await lister.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("ut"), TestContext.CancellationToken)
						).Single();

						// The stranger is connected on the same generation as the lister, so only the owner of the offer tells them apart.
						Assert.IsEmpty(await stranger.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("ut"), TestContext.CancellationToken));
						Assert.AreEqual(listing.ConnectionGeneration, other.ConnectionGeneration);

						WorkspaceSymbol resolved = await ((IWorkspaceSymbolResolver)stranger).ResolveAsync(listed, TestContext.CancellationToken);

						Assert.AreSame(listed, resolved);
						Assert.AreEqual(0, other.GetSentMethodCount("workspaceSymbol/resolve"));
					}
				}
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_ForASymbolThatAnEarlierConnectionListed_SendsNoRequest()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
		{
			client.SupportsWorkspaceSymbolResolve = true;

			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "Util",
						kind = 5,
						location = new { uri = UriOf("util.test") },
						data = new { id = 17 },
					},
				}
			);

			client.WorkspaceSymbolResolveResponse = Json(
				new
				{
					name = "Util",
					kind = 5,
					location = new { uri = UriOf("util.test"), range = RangeOf(7, 6, 10) },
				}
			);
		});

		using (client)
		{
			using (provider)
			{
				WorkspaceSymbol listed = (
					await provider.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("ut"), TestContext.CancellationToken)
				).Single();

				long generation = client.ConnectionGeneration;

				// The server restarts: the new connection never minted the data of the symbol.
				client.IsReady = false;

				Assert.IsTrue(await client.EnsureStartedAsync(TestContext.CancellationToken));
				Assert.IsGreaterThan(generation, client.ConnectionGeneration);

				WorkspaceSymbol resolved = await ((IWorkspaceSymbolResolver)provider).ResolveAsync(listed, TestContext.CancellationToken);

				Assert.AreSame(listed, resolved);
				Assert.AreEqual(0, client.GetSentMethodCount("workspaceSymbol/resolve"));
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_WhenTheConnectionChangesWhileTheServerComputes_DiscardsTheAnswer()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
		{
			client.SupportsWorkspaceSymbolResolve = true;

			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "Util",
						kind = 5,
						location = new { uri = UriOf("util.test") },
						data = new { id = 17 },
					},
				}
			);

			client.WorkspaceSymbolResolveResponse = Json(
				new
				{
					name = "Util",
					kind = 5,
					location = new { uri = UriOf("util.test"), range = RangeOf(7, 6, 10) },
				}
			);
		});

		using (client)
		{
			using (provider)
			{
				WorkspaceSymbol listed = (
					await provider.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("ut"), TestContext.CancellationToken)
				).Single();

				// The restart happens as the server answers.
				int restarts = 0;

				client.WorkspaceSymbolResolveHandler = async (_, _) =>
				{
					restarts++;
					client.IsReady = false;
					await client.EnsureStartedAsync(CancellationToken.None);

					return Json(
						new
						{
							name = "Util",
							kind = 5,
							location = new { uri = UriOf("util.test"), range = RangeOf(7, 6, 10) },
						}
					);
				};

				WorkspaceSymbol resolved = await ((IWorkspaceSymbolResolver)provider).ResolveAsync(listed, TestContext.CancellationToken);

				Assert.AreEqual(1, restarts);
				Assert.AreSame(listed, resolved);
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_WhenTheAnswerHasNoRange_KeepsTheSymbol()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
		{
			client.SupportsWorkspaceSymbolResolve = true;

			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "Util",
						kind = 5,
						location = new { uri = UriOf("util.test") },
					},
				}
			);

			client.WorkspaceSymbolResolveResponse = Json(
				new
				{
					name = "Util",
					kind = 5,
					location = new { uri = UriOf("util.test") },
				}
			);
		});

		using (client)
		{
			using (provider)
			{
				WorkspaceSymbol listed = (
					await provider.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("ut"), TestContext.CancellationToken)
				).Single();

				WorkspaceSymbol resolved = await ((IWorkspaceSymbolResolver)provider).ResolveAsync(listed, TestContext.CancellationToken);

				Assert.AreSame(listed, resolved);
				Assert.AreEqual(1, client.GetSentMethodCount("workspaceSymbol/resolve"));
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_WhenTheRequestFails_KeepsTheSymbol()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(client =>
		{
			client.SupportsWorkspaceSymbolResolve = true;

			client.WorkspaceSymbolResponse = Json(
				new object[]
				{
					new
					{
						name = "Util",
						kind = 5,
						location = new { uri = UriOf("util.test") },
					},
				}
			);

			client.ThrowInvalidOperationOnNextRequestMethod = "workspaceSymbol/resolve";
		});

		using (client)
		{
			using (provider)
			{
				WorkspaceSymbol listed = (
					await provider.GetWorkspaceSymbolsAsync(new WorkspaceSymbolRequest("ut"), TestContext.CancellationToken)
				).Single();

				WorkspaceSymbol resolved = await ((IWorkspaceSymbolResolver)provider).ResolveAsync(listed, TestContext.CancellationToken);

				Assert.AreSame(listed, resolved);
			}
		}
	}

	[TestMethod]
	public async Task Members_RejectNullArguments()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider) = await StartAsync(_ => { });

		using (client)
		{
			using (provider)
			{
				await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => provider.GetWorkspaceSymbolsAsync(null!, TestContext.CancellationToken));

				await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
					((IWorkspaceSymbolResolver)provider).ResolveAsync(null!, TestContext.CancellationToken)
				);
			}
		}
	}

	[TestMethod]
	public void Provider_IsTheWorkspaceSymbolContractOfTheAggregateProvider()
	{
		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		Assert.IsInstanceOfType<IWorkspaceSymbolProvider>(provider);
		Assert.IsInstanceOfType<IWorkspaceSymbolResolver>(provider);
		Assert.IsTrue(typeof(IWorkspaceSymbolProvider).IsAssignableFrom(typeof(ILanguageServerFeatureProvider)));
	}

	public TestContext TestContext { get; set; } = null!;
}
