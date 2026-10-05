using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Owns a test-language provider and records what its observers see, so a test asserts the synchronized, invalidated and moved documents,
/// and sends requests through the framework pipeline.
/// </summary>
internal sealed class LanguageServerFeatureProviderTestHost : IDisposable
{
	/// <param name="workspaceRootDirectoryPaths">The workspace root directories.</param>
	/// <param name="client">The language-server client, or <see langword="null"/> when unavailable.</param>
	/// <param name="options">The provider tunables, or <see langword="null"/> for the defaults.</param>
	/// <param name="workspaceFileWatcherFactory">A workspace file watcher factory that replaces the default.</param>
	/// <param name="workspaceSnapshotTrackerFactory">A workspace snapshot tracker factory that replaces the default.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <param name="profile">The profile, or <see langword="null"/> for <see cref="TestProviders.Profile"/>.</param>
	public LanguageServerFeatureProviderTestHost(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient? client,
		LanguageServerFeatureProviderOptions? options = null,
		FileWatcherFactory? workspaceFileWatcherFactory = null,
		FileSnapshotTrackerFactory? workspaceSnapshotTrackerFactory = null,
		ILogger? logger = null,
		LanguageServerProfile? profile = null
	)
	{
		Provider = TestProviders.Create(
			workspaceRootDirectoryPaths,
			client,
			options,
			workspaceFileWatcherFactory,
			workspaceSnapshotTrackerFactory,
			logger,
			profile,

			testHooks: new LanguageServerFeatureProviderTestHooks
			{
				DocumentSynchronizedObserver = document => SynchronizedDocumentVersions.Enqueue(document.Version),
				DocumentInvalidatedObserver = InvalidatedPaths.Enqueue,
				DocumentRenamedObserver = RenamedPaths.Enqueue,
			}
		);
	}

	/// <summary>The provider under test.</summary>
	public LanguageServerFeatureProvider Provider { get; }

	public ConcurrentQueue<int> SynchronizedDocumentVersions { get; } = new();

	public ConcurrentQueue<string> InvalidatedPaths { get; } = new();

	public ConcurrentQueue<string> RenamedPaths { get; } = new();

	public PositionPayload? LastRequestedPosition { get; private set; }

	/// <summary>Sends a request through the position-based request pipeline and records the protocol position of the offset.</summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="offset">The zero-based document offset.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The documented fallback value of the request.</returns>
	public Task<HoverInformation?> GetHoverAtOffsetAsync(string filePath, string content, int offset, CancellationToken cancellationToken = default)
	{
		return Provider.Composition.RequestPipeline.SendAsync(
			filePath,
			documentText: content,

			request: new DocumentRequestDescriptor<object?, HoverInformation?>(
				method: "test/positionHover",

				buildParameters: (textDocument, protocolPosition) =>
				{
					LastRequestedPosition = protocolPosition;
					return textDocument;
				},

				parseResponse: static (_, _) => null,
				fallbackValue: null,
				offset: offset
			),

			cancellationToken
		);
	}

	/// <summary>Sends a request through the document-request pipeline with a caller-supplied capability gate.</summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="supportsRequest">The capability gate to evaluate.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The documented fallback value of the request.</returns>
	public Task<HoverInformation?> SendGatedRequestAsync(
		string filePath,
		string content,
		Func<ILanguageServerClient, bool> supportsRequest,
		CancellationToken cancellationToken = default
	)
	{
		return Provider.Composition.RequestPipeline.SendAsync(
			filePath,
			documentText: content,

			request: new DocumentRequestDescriptor<object?, HoverInformation?>(
				method: "test/gatedRequest",
				buildParameters: static (textDocument, _) => textDocument,
				parseResponse: static (_, _) => null,
				fallbackValue: null,
				supportsRequest
			),

			cancellationToken
		);
	}

	/// <summary>
	/// Sends a request through the document-request pipeline and returns the parser result, so tests can verify that a real response reaches
	/// the response parser instead of only the documented fallback value.
	/// </summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed response text, or the documented fallback value.</returns>
	public Task<string?> SendParsedRequestAsync(string filePath, string content, CancellationToken cancellationToken = default)
	{
		return Provider.Composition.RequestPipeline.SendAsync(
			filePath,
			documentText: content,

			request: new DocumentRequestDescriptor<object?, string?>(
				method: "test/parsedRequest",
				buildParameters: static (textDocument, _) => textDocument,
				parseResponse: static (response, _) => response?.ToString(),
				fallbackValue: null,
				supportsRequest: static _ => true
			),

			cancellationToken
		);
	}

	/// <summary>
	/// Sends a request whose expected response payload is a non-nullable struct, so tests can verify that a request-runner fallback returns
	/// the caller's fallback value instead of a parsed default struct.
	/// </summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed response text, or the documented fallback value.</returns>
	public Task<string?> SendStructResponseRequestAsync(string filePath, string content, CancellationToken cancellationToken = default)
	{
		return Provider.Composition.RequestPipeline.SendAsync(
			filePath,
			documentText: content,

			request: new DocumentRequestDescriptor<StubStructResponse, string?>(
				method: "test/structResponse",
				buildParameters: static (textDocument, _) => textDocument,
				parseResponse: static (response, _) => $"parsed:{response.Value}",
				fallbackValue: "fallback",
				supportsRequest: static _ => true
			),

			cancellationToken
		);
	}

	/// <summary>
	/// Sends a request whose response parser can tell a <see langword="null"/> payload apart, so tests can verify that a reference-type
	/// <see langword="null"/> response returns the caller's fallback value instead of reaching the parser.
	/// </summary>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed response text, or the documented fallback value.</returns>
	public Task<string> SendNullSensitiveRequestAsync(string filePath, string content, CancellationToken cancellationToken = default)
	{
		return Provider.Composition.RequestPipeline.SendAsync(
			filePath,
			documentText: content,

			request: new DocumentRequestDescriptor<object?, string>(
				method: "test/nullSensitiveRequest",
				buildParameters: static (textDocument, _) => textDocument,
				parseResponse: static (response, _) => response?.ToString() ?? "parsed-null",
				fallbackValue: "fallback",
				supportsRequest: static _ => true
			),

			cancellationToken
		);
	}

	/// <inheritdoc/>
	public void Dispose() => Provider.Dispose();
}
