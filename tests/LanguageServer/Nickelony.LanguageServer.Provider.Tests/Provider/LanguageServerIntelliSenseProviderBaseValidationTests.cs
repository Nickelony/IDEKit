namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers constructor validation: workspace roots, provider options, unusable document paths, and the
/// missing-client report contract.
/// </summary>
[TestClass]
public sealed class LanguageServerIntelliSenseProviderBaseValidationTests
{
	private const string Content = "line one";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-validation-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");

	[TestMethod]
	public void Constructor_WithNullWorkspaceRoots_ThrowsArgumentNullException()
		=> Assert.ThrowsExactly<ArgumentNullException>(() => new TestLanguageServerProvider(null!, new FakeLanguageServerClient()));

	[TestMethod]
	public void Constructor_WithEmptyWorkspaceRoots_ModelsAFolderlessProvider()
	{
		using var provider = new TestLanguageServerProvider([], new FakeLanguageServerClient());

		Assert.IsFalse(provider.IsAvailable);
	}

	[TestMethod]
	public void Constructor_WithWhitespaceWorkspaceRoot_ThrowsArgumentException()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TestLanguageServerProvider(["   "], new FakeLanguageServerClient()));

	[TestMethod]
	public void Constructor_WithDuplicateWorkspaceRoots_ThrowsArgumentException()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TestLanguageServerProvider([s_workspaceRoot, s_workspaceRoot], new FakeLanguageServerClient()));

	[TestMethod]
	public void Constructor_WithCaseVariantDuplicateRoots_ThrowsOnCaseInsensitivePlatforms()
	{
		if (LanguageServerPaths.UsesCaseSensitiveLocalPaths)
			Assert.Inconclusive("Case-variant duplicates are distinct paths under case-sensitive path identity.");

		string root = Path.Combine(Path.GetTempPath(), "ls-root-case");
		string caseVariantRoot = Path.Combine(Path.GetTempPath(), "LS-ROOT-CASE");

		Assert.ThrowsExactly<ArgumentException>(() => new TestLanguageServerProvider([root, caseVariantRoot], new FakeLanguageServerClient()));
	}

	[TestMethod]
	public void Constructor_WhenConstructionFailsAfterClientTransfer_DisposesTheClient()
	{
		using var client = new FakeLanguageServerClient();

		// An invalid watch specification passes the constructor's own argument checks but fails the snapshot-tracker
		// validation that runs after the constructor takes ownership of the client.
		Assert.ThrowsExactly<ArgumentException>(() => new ThrowingWatchSpecificationsProvider([s_workspaceRoot], client));

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.IsTrue(client.IsDisposed);
	}

	[TestMethod]
	public void Constructor_WhenRootValidationFails_DisposesTheClient()
	{
		using var client = new FakeLanguageServerClient();

		// Ownership transfers to the provider as the constructor's first action, so a failure during
		// validation - which used to precede the transfer - must still dispose the caller's client. A
		// whitespace-only root still fails normalization (an empty list now models a folderless provider).
		Assert.ThrowsExactly<ArgumentException>(() => new TestLanguageServerProvider(["   "], client));

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.IsTrue(client.IsDisposed);
	}

	[TestMethod]
	public void Constructor_WithDefaultOptions_IsValid()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client, LanguageServerProviderOptions.Default);

		// Before the first operation the provider advertises nothing: unavailable state, no availability, no
		// capabilities, and no diagnostics.
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.IsFalse(provider.SupportsReferences);
		Assert.IsFalse(provider.SupportsRename);
		Assert.IsFalse(provider.SupportsFormatting);
		Assert.IsFalse(provider.SupportsDocumentSymbols);
		Assert.IsFalse(provider.SupportsCodeActions);
		Assert.AreEqual(0, provider.GetDiagnostics(s_filePath).Count);
		Assert.AreEqual(0, client.StartCallCount, "Reading cached diagnostics must not start the language server.");
	}

	[TestMethod]
	public void Constructor_WithNonPositiveRequestTimeout_ThrowsArgumentOutOfRangeException()
	{
		using var client = new FakeLanguageServerClient();

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { RequestTimeout = TimeSpan.Zero }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { RequestTimeout = TimeSpan.FromMilliseconds(-2) }));
	}

	[TestMethod]
	public void Constructor_WithTooLargeRequestTimeout_ThrowsArgumentOutOfRangeException()
	{
		using var client = new FakeLanguageServerClient();

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { RequestTimeout = TimeSpan.FromMilliseconds((double)int.MaxValue + 1) }));
	}

	[TestMethod]
	public void Constructor_WithInvalidThresholds_ThrowArgumentOutOfRangeException()
	{
		using var client = new FakeLanguageServerClient();

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { RequestTimeoutRestartThreshold = -1 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { HardStartupFailureThreshold = 0 }));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { MaxTrackedIdleDocuments = -1 }));
	}

	[TestMethod]
	public void Constructor_WithDisabledTimeoutRestart_IsValid()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { RequestTimeoutRestartThreshold = 0 });

		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
	}

	[TestMethod]
	public void Constructor_WithZeroRequestOnlyDocumentCap_IsValid()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { MaxTrackedIdleDocuments = 0 });

		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
	}

	[TestMethod]
	public async Task DocumentMembers_WithUnusablePath_AreNoOps()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		Assert.AreEqual(0, provider.GetDiagnostics("   ").Count);
		provider.OpenDocument("   ", Content);
		provider.UpdateDocument("   ", Content);
		provider.CloseDocument("   ");
		provider.MoveDocument("   ", s_filePath, Content);
		provider.MoveDocument(s_filePath, "   ", Content);

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	/// <summary>
	/// Test provider whose watch specifications are rejected by the framework's snapshot tracker, so construction
	/// fails after the base class has taken ownership of the client.
	/// </summary>
	private sealed class ThrowingWatchSpecificationsProvider : TestLanguageServerProvider
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="ThrowingWatchSpecificationsProvider"/> class.
		/// </summary>
		/// <param name="workspaceRootDirectoryPaths">The workspace root directories.</param>
		/// <param name="client">The language-server client owned by the provider.</param>
		public ThrowingWatchSpecificationsProvider(IReadOnlyList<string> workspaceRootDirectoryPaths, ILanguageServerClient client)
			: base(workspaceRootDirectoryPaths, client, [new WorkspaceWatchSpecification(string.Empty, IncludeSubdirectories: false)])
		{ }
	}
}
