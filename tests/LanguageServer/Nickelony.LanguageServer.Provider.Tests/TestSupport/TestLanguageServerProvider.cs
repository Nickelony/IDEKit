using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.DocumentSymbols;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using Nickelony.IDEKit.IntelliSense.Signatures;
using System.Collections.Concurrent;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Minimal provider for framework tests: implements every hook with deterministic test behavior and records the
/// hook invocations the tests assert against. Requests are routed through the framework request pipeline so its
/// document synchronization and dispatch behavior is exercised.
/// </summary>
internal class TestLanguageServerProvider : LanguageServerIntelliSenseProviderBase
{
	private readonly ConcurrentDictionary<string, IReadOnlyList<TextDiagnostic>> _diagnostics = new(LanguageServerPaths.LocalPathComparer);
	private readonly WorkspaceFileWatcherFactory? _workspaceFileWatcherFactory;
	private TestDocumentStore? _documentStore;

	/// <summary>
	/// Initializes a new instance of the <see cref="TestLanguageServerProvider"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">The workspace root directories.</param>
	/// <param name="client">The language-server client, or <see langword="null"/> when unavailable.</param>
	/// <param name="options">The provider tunables, or <see langword="null"/> for the defaults.</param>
	/// <param name="workspaceFileWatcherFactory">A workspace file watcher factory, used for testing.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	public TestLanguageServerProvider(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient? client,
		LanguageServerProviderOptions? options = null,
		WorkspaceFileWatcherFactory? workspaceFileWatcherFactory = null,
		ILogger? logger = null)
		: this(workspaceRootDirectoryPaths, client, TestWatchSpecifications, options, workspaceFileWatcherFactory, logger)
	{ }

	/// <summary>
	/// Initializes the provider with caller-supplied watch specifications, so derived test providers can disable
	/// watching or pass an invalid specification for construction-failure tests.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">The workspace root directories.</param>
	/// <param name="client">The language-server client, or <see langword="null"/> when unavailable.</param>
	/// <param name="watchSpecifications">The watch specifications passed to the framework, or an empty list to disable watching.</param>
	/// <param name="options">The provider tunables, or <see langword="null"/> for the defaults.</param>
	/// <param name="workspaceFileWatcherFactory">A workspace file watcher factory, used for testing.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	internal TestLanguageServerProvider(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient? client,
		IReadOnlyList<WorkspaceWatchSpecification> watchSpecifications,
		LanguageServerProviderOptions? options = null,
		WorkspaceFileWatcherFactory? workspaceFileWatcherFactory = null,
		ILogger? logger = null)
		: this(workspaceRootDirectoryPaths, client, new TestDocumentStore(), watchSpecifications, options, workspaceFileWatcherFactory, logger)
	{ }

	/// <summary>
	/// Initializes the provider with a caller-created document store, so the instance can be kept for typed access
	/// without a downcast of the store the framework owns.
	/// </summary>
	private TestLanguageServerProvider(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient? client,
		TestDocumentStore documentStore,
		IReadOnlyList<WorkspaceWatchSpecification> watchSpecifications,
		LanguageServerProviderOptions? options,
		WorkspaceFileWatcherFactory? workspaceFileWatcherFactory,
		ILogger? logger)
		: base(workspaceRootDirectoryPaths, documentStore, watchSpecifications, client, options, logger)
	{
		_documentStore = documentStore;
		_workspaceFileWatcherFactory = workspaceFileWatcherFactory;
	}

	/// <summary>
	/// Gets the watch specifications the default constructor passes to the framework.
	/// </summary>
	private static IReadOnlyList<WorkspaceWatchSpecification> TestWatchSpecifications { get; } = [new WorkspaceWatchSpecification("*.test", IncludeSubdirectories: true)];

	/// <inheritdoc/>
	protected override IWorkspaceFileWatcher CreateWorkspaceFileWatcher(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Action<IWorkspaceFileWatcher, Exception?> onWatcherFailed)
	{
		return _workspaceFileWatcherFactory is null
			? base.CreateWorkspaceFileWatcher(workspaceRootDirectoryPath, dispatchAsync, onWatcherFailed)
			: _workspaceFileWatcherFactory(workspaceRootDirectoryPath, dispatchAsync, onWatcherFailed);
	}

	/// <summary>
	/// Gets the versions of documents whose post-synchronization hook ran.
	/// </summary>
	public ConcurrentQueue<int> SynchronizedDocumentVersions { get; } = new();

	/// <summary>
	/// Gets the file paths reported through the tracked-document invalidation hook.
	/// </summary>
	public ConcurrentQueue<string> InvalidatedPaths { get; } = new();

	/// <summary>
	/// Gets the file paths reported through the move hook.
	/// </summary>
	public ConcurrentQueue<string> MovedPaths { get; } = new();

	/// <summary>
	/// Gets or sets a factory for the diagnostics the diagnostics hook returns; when unset, the hook returns a
	/// single warning diagnostic whose message carries the tracked document version.
	/// </summary>
	public Func<string, IReadOnlyList<TextDiagnostic>>? DiagnosticsFactory { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the diagnostics hook suppresses the payload by returning
	/// <see langword="null"/>, so tests can pin the "suppressed payload raises no event" contract.
	/// </summary>
	public bool SuppressDiagnosticsPayload { get; set; }

	/// <summary>
	/// Gets the number of <see cref="OnDisposing"/> invocations.
	/// </summary>
	public int OnDisposingCallCount { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the owned client was already disposed when <see cref="OnDisposing"/> ran.
	/// </summary>
	public bool? ClientWasDisposedAtOnDisposing { get; private set; }

	/// <summary>
	/// Gets the position of the most recent request sent through the position-request pipeline.
	/// </summary>
	public ProtocolPosition? LastRequestedPosition { get; private set; }

	/// <summary>
	/// Gets or sets a value indicating whether the post-synchronization hook throws, for containment tests.
	/// </summary>
	public bool ThrowOnSynchronizedHook { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the tracked-diagnostics hook throws, for containment tests.
	/// </summary>
	public bool ThrowOnTrackedDiagnostics { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the configuration-path probe throws, for containment tests.
	/// </summary>
	public bool ThrowOnConfigurationPathProbe { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the settings-payload hook throws, for containment tests.
	/// </summary>
	public bool ThrowOnSettingsPayload { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the startup-failure hook throws, for containment tests.
	/// </summary>
	public bool ThrowOnStartupFailureHook { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the missing-client failure hook throws, for containment tests.
	/// </summary>
	public bool ThrowOnMissingClientFailureHook { get; set; }

	/// <summary>
	/// Gets the exact message the missing-client failure hook returns. The embedded unique token lets a test prove
	/// the reported failure carries this hook's own payload instead of a framework string that happens to match.
	/// </summary>
	public string MissingClientFailureMessage { get; } = "The test language server executable is unavailable (" + Guid.NewGuid().ToString("N") + ").";

	/// <summary>
	/// Gets or sets a value indicating whether the move hook throws, for containment tests.
	/// </summary>
	public bool ThrowOnMovedHook { get; set; }

	/// <inheritdoc/>
	protected override void OnDisposing()
	{
		OnDisposingCallCount++;
		ClientWasDisposedAtOnDisposing = (Client as FakeLanguageServerClient)?.IsDisposed;
	}

	/// <summary>
	/// Sends a request through the position-based request pipeline and records the clamped position.
	/// </summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="position">The zero-based line and character position.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The documented fallback value of the request.</returns>
	public Task<TextHoverInfo?> GetHoverAtPositionAsync(string filePath, string content, TextPosition position, CancellationToken cancellationToken = default)
	{
		return SendDocumentPositionRequestAsync<object?, TextHoverInfo?>(
			filePath, content, position, "test/positionHover",
			buildParameters: (textDocument, protocolPosition) =>
			{
				LastRequestedPosition = protocolPosition;
				return textDocument;
			},
			parseResponse: static _ => null,
			fallbackValue: null,
			cancellationToken);
	}

	/// <summary>
	/// Sends a request through the document-request pipeline with a caller-supplied capability gate.
	/// </summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="supportsRequest">The capability gate to evaluate.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The documented fallback value of the request.</returns>
	public Task<TextHoverInfo?> SendGatedRequestAsync(string filePath, string content, Func<ILanguageServerClient, bool> supportsRequest,
		CancellationToken cancellationToken = default)
	{
		return SendDocumentRequestAsync<object?, TextHoverInfo?>(
			filePath, content, "test/gatedRequest",
			supportsRequest,
			buildParameters: static textDocument => textDocument,
			parseResponse: static _ => null,
			fallbackValue: null,
			cancellationToken);
	}

	/// <summary>
	/// Sends a request through the document-request pipeline and returns the parser result, so tests can verify
	/// that a real response reaches the response parser instead of only the documented fallback value.
	/// </summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed response text, or the documented fallback value.</returns>
	public Task<string?> SendParsedRequestAsync(string filePath, string content, CancellationToken cancellationToken = default)
	{
		return SendDocumentRequestAsync<object?, string?>(
			filePath, content, "test/parsedRequest",
			supportsRequest: static _ => true,
			buildParameters: static textDocument => textDocument,
			parseResponse: static response => response?.ToString(),
			fallbackValue: null,
			cancellationToken);
	}

	/// <summary>
	/// Sends a request whose expected response payload is a non-nullable struct, so tests can verify that a
	/// dispatcher fallback returns the caller's fallback value instead of a parsed default struct.
	/// </summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed response text, or the documented fallback value.</returns>
	public Task<string?> SendStructResponseRequestAsync(string filePath, string content, CancellationToken cancellationToken = default)
	{
		return SendDocumentRequestAsync<TestStructResponse, string?>(
			filePath, content, "test/structResponse",
			supportsRequest: static _ => true,
			buildParameters: static textDocument => textDocument,
			parseResponse: static response => $"parsed:{response.Value}",
			fallbackValue: "fallback",
			cancellationToken);
	}

	/// <summary>
	/// Sends a request whose response parser can tell a null payload apart, so tests can verify that a
	/// reference-type null response returns the caller's fallback value instead of reaching the parser.
	/// </summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed response text, or the documented fallback value.</returns>
	public Task<string> SendNullSensitiveRequestAsync(string filePath, string content, CancellationToken cancellationToken = default)
	{
		return SendDocumentRequestAsync<object?, string>(
			filePath, content, "test/nullSensitiveRequest",
			supportsRequest: static _ => true,
			buildParameters: static textDocument => textDocument,
			parseResponse: static response => response?.ToString() ?? "parsed-null",
			fallbackValue: "fallback",
			cancellationToken);
	}

	/// <summary>
	/// Gets the current tracked snapshot for one document path, for tests that drive internal framework seams.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>The tracked snapshot, or <see langword="null"/> when the document is not tracked.</returns>
	public DocumentSnapshot? GetTrackedSnapshot(string filePath)
		=> _documentStore!.GetDocumentSnapshot(filePath);

	/// <inheritdoc/>
	protected override string ProviderDisplayName => "Test";

	/// <inheritdoc/>
	protected override string LanguageId => "test";

	/// <inheritdoc/>
	protected override bool IsConfigurationPath(string normalizedPath)
	{
		if (ThrowOnConfigurationPathProbe)
			throw new InvalidOperationException("Simulated configuration-path probe failure.");

		return Path.GetFileName(normalizedPath).StartsWith(".test", StringComparison.Ordinal);
	}

	/// <inheritdoc/>
	protected override object CreateSettingsPayload()
	{
		if (ThrowOnSettingsPayload)
			throw new InvalidOperationException("Simulated settings-payload failure.");

		return new TestSettingsPayload(true);
	}

	/// <inheritdoc/>
	protected override string? CreateStartupFailureMessage(bool isPersistentFailure)
	{
		if (ThrowOnStartupFailureHook)
			throw new InvalidOperationException("Simulated startup-failure hook failure.");

		return isPersistentFailure
			? "The test language server failed to start repeatedly."
			: "The test language server failed to start.";
	}

	/// <inheritdoc/>
	protected override string? CreateMissingClientFailureMessage()
	{
		if (ThrowOnMissingClientFailureHook)
			throw new InvalidOperationException("Simulated missing-client failure hook failure.");

		return MissingClientFailureMessage;
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<TextDiagnostic> GetTrackedDiagnostics(string normalizedFilePath)
	{
		if (ThrowOnTrackedDiagnostics)
			throw new InvalidOperationException("Simulated tracked-diagnostics failure.");

		return _diagnostics.TryGetValue(normalizedFilePath, out IReadOnlyList<TextDiagnostic>? diagnostics) ? diagnostics : [];
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<SemanticToken> GetTrackedSemanticTokens(string normalizedFilePath) => [];

	/// <inheritdoc/>
	protected override bool TryStoreSemanticTokens(string normalizedFilePath, int documentVersion, IReadOnlyList<SemanticToken> semanticTokens) => false;

	/// <inheritdoc/>
	protected override void InvalidateTrackedDocumentSynchronization(string filePath)
		=> _documentStore!.Invalidate(filePath);

	/// <inheritdoc/>
	protected override IReadOnlyList<TextDiagnostic>? HandleDiagnosticsPayload(
		string filePath,
		PublishDiagnosticsParams parameters,
		DocumentSnapshot document)
	{
		if (SuppressDiagnosticsPayload)
			return null;

		IReadOnlyList<TextDiagnostic> diagnostics = DiagnosticsFactory?.Invoke(filePath)
			?? [new TextDiagnostic(TextDiagnosticSeverity.Warning, $"test:{document.Version}", 0, 0)];

		_diagnostics[filePath] = diagnostics;
		return diagnostics;
	}

	/// <inheritdoc/>
	protected override Task OnDocumentSynchronizedAsync(DocumentSnapshot document, CancellationToken cancellationToken)
	{
		if (ThrowOnSynchronizedHook)
			throw new InvalidOperationException("Simulated synchronization-hook failure.");

		SynchronizedDocumentVersions.Enqueue(document.Version);
		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	protected override void OnTrackedDocumentInvalidated(string filePath)
		=> InvalidatedPaths.Enqueue(filePath);

	/// <inheritdoc/>
	protected override void OnDocumentMoved(string filePath)
	{
		MovedPaths.Enqueue(filePath);

		if (ThrowOnMovedHook)
			throw new InvalidOperationException("Simulated move-hook failure.");
	}

	/// <inheritdoc/>
	public override Task<IReadOnlyList<TextCompletionItem>> GetCompletionItemsAsync(LanguageServerCompletionRequest request,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<TextCompletionItem>>([]);

	/// <inheritdoc/>
	public override Task<TextHoverInfo?> GetHoverAsync(LanguageServerHoverRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return SendDocumentRequestAsync<object?, TextHoverInfo?>(
			request.FilePath, request.DocumentText, "test/hover",
			supportsRequest: static _ => true,
			buildParameters: static textDocument => textDocument,
			parseResponse: static _ => null,
			fallbackValue: null,
			cancellationToken);
	}

	/// <inheritdoc/>
	public override Task<TextDefinitionLocation?> GetDefinitionAsync(LanguageServerDefinitionRequest request,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<TextDefinitionLocation?>(null);

	/// <inheritdoc/>
	public override Task<TextSignatureHelp?> GetSignatureHelpAsync(LanguageServerSignatureHelpRequest request,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<TextSignatureHelp?>(null);

	/// <inheritdoc/>
	public override Task<IReadOnlyList<TextDocumentSymbol>> GetDocumentSymbolsAsync(
		LanguageServerDocumentSymbolRequest request, CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<TextDocumentSymbol>>([]);

	/// <inheritdoc/>
	public override Task<IReadOnlyList<TextCodeAction>> GetCodeActionsAsync(LanguageServerCodeActionRequest request,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<TextCodeAction>>([]);

	/// <inheritdoc/>
	public override Task<IReadOnlyList<TextReferenceLocation>> GetReferencesAsync(LanguageServerReferenceRequest request,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<TextReferenceLocation>>([]);

	/// <inheritdoc/>
	public override Task<TextWorkspaceEdit?> RenameSymbolAsync(LanguageServerRenameRequest request,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<TextWorkspaceEdit?>(null);

	/// <inheritdoc/>
	public override Task<TextWorkspaceEdit?> FormatDocumentAsync(LanguageServerFormattingRequest request,
		CancellationToken cancellationToken = default)
		=> Task.FromResult<TextWorkspaceEdit?>(null);
}
