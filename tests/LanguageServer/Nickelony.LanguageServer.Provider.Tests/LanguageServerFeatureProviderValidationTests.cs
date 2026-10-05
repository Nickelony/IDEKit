using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers constructor validation: workspace roots, provider options, and unusable document paths.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderValidationTests
{
	private const string Content = "line one";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-validation-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");

	[TestMethod]
	public void Constructor_WithNullWorkspaceRoots_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => TestProviders.Create(null!, new FakeLanguageServerClient()));

	[TestMethod]
	public void Constructor_WithEmptyWorkspaceRoots_ModelsAFolderlessProvider()
	{
		using LanguageServerFeatureProvider provider = TestProviders.Create([], new FakeLanguageServerClient());
		Assert.IsFalse(provider.IsAvailable);
	}

	[TestMethod]
	public void Constructor_WithWhitespaceWorkspaceRoot_ThrowsArgumentException() =>
		Assert.ThrowsExactly<ArgumentException>(() => TestProviders.Create(["   "], new FakeLanguageServerClient()));

	[TestMethod]
	public void Constructor_WithARelativeWorkspaceRoot_NamesTheConstructorParameter()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			TestProviders.Create(["relative/root"], new FakeLanguageServerClient())
		);

		Assert.AreEqual("workspaceRootDirectoryPaths", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_WithDuplicateWorkspaceRoots_ThrowsArgumentException() =>
		Assert.ThrowsExactly<ArgumentException>(() => TestProviders.Create([s_workspaceRoot, s_workspaceRoot], new FakeLanguageServerClient()));

	[TestMethod]
	public void Constructor_WithCaseVariantRootsUnderACaseInsensitiveComparison_ThrowsForTheDuplicate()
	{
		string root = Path.Combine(Path.GetTempPath(), "ls-root-case");
		string caseVariantRoot = Path.Combine(Path.GetTempPath(), "LS-ROOT-CASE");

		Assert.ThrowsExactly<ArgumentException>(() =>
			TestProviders.Create([root, caseVariantRoot], new FakeLanguageServerClient(), TestOptions.CaseInsensitive)
		);
	}

	[TestMethod]
	public void Constructor_WithCaseVariantRootsUnderACaseSensitiveComparison_AcceptsBothRoots()
	{
		string root = Path.Combine(Path.GetTempPath(), "ls-root-case");
		string caseVariantRoot = Path.Combine(Path.GetTempPath(), "LS-ROOT-CASE");

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[root, caseVariantRoot],
			new FakeLanguageServerClient(),
			TestOptions.CaseSensitive
		);

		Assert.IsNotNull(provider);
	}

	[TestMethod]
	public void Constructor_WhenConstructionFailsAfterClientTransfer_DisposesTheClient()
	{
		using var client = new FakeLanguageServerClient();

		// An invalid watch specification passes the constructor's own argument checks but fails the snapshot-tracker
		// validation that runs after the constructor takes ownership of the client.
		Assert.ThrowsExactly<ArgumentException>(() =>
			TestProviders.Create(
				[s_workspaceRoot],
				client,

				profile: TestProviders.CreateProfile() with
				{
					WatchSpecifications = [new FileWatchSpecification(string.Empty, IncludeSubdirectories: false)],
				}
			)
		);

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.IsTrue(client.IsDisposed);
	}

	[TestMethod]
	public void Constructor_WhenRootValidationFails_DisposesTheClient()
	{
		using var client = new FakeLanguageServerClient();

		// Ownership transfers to the provider as the constructor's first action, so a failure during
		// validation must still dispose the caller's client. A whitespace-only root fails normalization;
		// an empty list is a folderless provider.
		Assert.ThrowsExactly<ArgumentException>(() => TestProviders.Create(["   "], client));

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.IsTrue(client.IsDisposed);
	}

	[TestMethod]
	public async Task Constructor_ProviderDisplayName_ReachesTheLogTextOfTheCollaborators()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			workspaceRootDirectoryPaths: [s_workspaceRoot],
			client,
			logger: logger,

			profile: TestProviders.CreateProfile() with
			{
				WatchSpecifications = [],
				LanguageDisplayName = "Example",
			}
		);

		await provider
			.GetHoverAsync(
				filePath: s_filePath,
				documentText: "line one",
				position: new TextPosition(0, 0),
				cancellationToken: TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		// The log names the provider first and the workspace root second, so a swapped pair of display strings shows here.
		string expectedLog = $"Failed to start the Example language server for workspace '{LocalPath.Normalize(s_workspaceRoot)}'";

		Assert.Contains(log => log.Contains(expectedLog, StringComparison.Ordinal), logger.Logs);
	}

	[TestMethod]
	public async Task Constructor_ProviderDisplayName_ReachesTheRestartLogOfTheLifecycleCoordinator()
	{
		using var logger = new RecordingLogger(LogLevel.Information);
		using var client = new FakeLanguageServerClient { IsReady = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			workspaceRootDirectoryPaths: [s_workspaceRoot],
			client,
			logger: logger,

			profile: TestProviders.CreateProfile() with
			{
				WatchSpecifications = [],
				LanguageDisplayName = "Example",
			}
		);

		await provider
			.GetHoverAsync(
				filePath: s_filePath,
				documentText: "line one",
				position: new TextPosition(0, 0),
				cancellationToken: TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		// The session drops while the provider still counts its start as successful, so the next call restarts it.
		client.IsReady = false;

		await provider
			.GetHoverAsync(
				filePath: s_filePath,
				documentText: "line one",
				position: new TextPosition(0, 0),
				cancellationToken: TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		// The coordinator names the provider first and the workspace root second in its own logs, apart from the reporter's.
		string expectedLog = $"Example language server connection dropped for workspace '{LocalPath.Normalize(s_workspaceRoot)}'";

		Assert.Contains(log => log.Contains(expectedLog, StringComparison.Ordinal), logger.Logs);
	}

	[TestMethod]
	public void Constructor_WithDefaultOptions_IsValid()
	{
		using var client = new FakeLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client, LanguageServerFeatureProviderOptions.Default);

		// Before the first operation the provider advertises nothing: unavailable state, no availability, no
		// capabilities, and no diagnostics.
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.AreEqual(LanguageServerFeatureProviderCapabilities.None, provider.Capabilities);

		Assert.IsEmpty(provider.GetDiagnostics(s_filePath));
		Assert.AreEqual(0, client.StartCallCount, "Reading cached diagnostics must not start the language server.");
	}

	[TestMethod]
	public async Task DocumentMembers_WithUnusablePath_AreNoOps()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		Assert.IsEmpty(provider.GetDiagnostics("   "));
		provider.OpenDocument("   ", Content);
		provider.UpdateDocument("   ", Content);
		provider.CloseDocument("   ");

		provider.RenameDocument("   ", s_filePath, Content);
		provider.RenameDocument(s_filePath, "   ", Content);

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsEmpty(client.GetSentMethodNames());
	}

	public TestContext? TestContext { get; set; }
}
