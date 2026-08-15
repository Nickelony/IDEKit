namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>
/// A concrete Roslyn provider a test constructs directly, standing in for a language package: it supplies the
/// identity and the language-specific watch pattern the abstract core expects.
/// </summary>
internal sealed class TestRoslynLanguageServerIntelliSenseProvider : RoslynLanguageServerIntelliSenseProvider
{
	/// <summary>The language identifier the test provider opens documents with.</summary>
	internal const string TestLanguageId = "vbtest";

	/// <summary>The display name the test provider reports in log and diagnostic text.</summary>
	internal const string TestDisplayName = "Test Roslyn";

	/// <summary>The language-specific watch pattern the test provider mirrors.</summary>
	internal static readonly IReadOnlyList<WorkspaceWatchSpecification> LanguageWatchSpecifications =
	[
		new WorkspaceWatchSpecification("*.vbtest", IncludeSubdirectories: true)
	];

	/// <summary>
	/// Initializes a new instance of the <see cref="TestRoslynLanguageServerIntelliSenseProvider"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">The workspace roots to watch.</param>
	/// <param name="client">The language server client, or <see langword="null"/> when unavailable.</param>
	/// <param name="options">The Roslyn session options, or <see langword="null"/> for the defaults.</param>
	/// <param name="providerOptions">The provider tunables, or <see langword="null"/> for the framework defaults.</param>
	public TestRoslynLanguageServerIntelliSenseProvider(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient? client,
		RoslynLanguageServerOptions? options = null,
		LanguageServerProviderOptions? providerOptions = null)
		: base(workspaceRootDirectoryPaths, LanguageWatchSpecifications, client, options, providerOptions)
	{ }

	/// <inheritdoc/>
	protected override string ProviderDisplayName => TestDisplayName;

	/// <inheritdoc/>
	protected override string LanguageId => TestLanguageId;
}
