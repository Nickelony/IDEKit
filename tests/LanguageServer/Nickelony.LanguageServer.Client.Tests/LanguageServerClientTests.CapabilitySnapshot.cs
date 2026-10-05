using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	[TestMethod]
	public void TryMarkConnectionUnhealthy_DuringStartup_PreventsLaterReadinessPublication()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 4,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);

		Assert.IsTrue(client.TryMarkConnectionUnhealthy(4));
		Assert.IsFalse(client.IsReady);

		// A late startup completion that only validates the generation number must not resurrect the invalidated connection.
		SetReadyState(client, true);

		Assert.IsFalse(client.IsReady);
	}

	[TestMethod]
	public void GetRequiredReadySession_WhenClientIsNotReady_ThrowsConnectionUnavailableException()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);

		Assert.IsFalse(client.IsReady);

		LanguageServerConnectionUnavailableException exception = Assert.ThrowsExactly<LanguageServerConnectionUnavailableException>(
			client.Composition.State.GetRequiredReadySession
		);

		// The whole connection-usability family derives from one base type, so a host needs a single catch clause.
		Assert.IsInstanceOfType<LanguageServerConnectionException>(exception);
	}

	[TestMethod]
	public void GetRequiredReadySession_WhenActiveSessionGenerationDoesNotMatchPublishedReadyGeneration_ThrowsConnectionUnavailableException()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession readySession = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		ConnectionSession replacementSession = CreateConnectionSession(
			client,
			generation: 2,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, readySession);
		SetReadyState(client, true);
		client.Composition.State.SetActiveSession(replacementSession);

		Assert.ThrowsExactly<LanguageServerConnectionUnavailableException>(client.Composition.State.GetRequiredReadySession);
	}

	[TestMethod]
	public void TryMarkConnectionUnhealthy_WhenReady_LogsRestartWarningAndResetsPublishedCapabilities()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 5,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "renameProvider": true,
				    "semanticTokensProvider": {
				      "full": {
				        "delta": true
				      },
				      "legend": {
				        "tokenTypes": ["function"],
				        "tokenModifiers": ["declaration"]
				      }
				    }
				  }
				}
				"""
			)
		);

		SetReadyState(client, true);

		int connectionUnavailableCount = 0;
		long unavailableGeneration = 0;

		client.ConnectionUnavailable += (_, eventArgs) =>
		{
			connectionUnavailableCount++;
			unavailableGeneration = eventArgs.ConnectionGeneration;
		};

		client.TryMarkConnectionUnhealthy(client.ConnectionGeneration);

		Assert.IsFalse(client.IsReady);
		Assert.AreEqual(5L, client.ConnectionGeneration);
		Assert.AreEqual(TextDocumentSyncKind.None, client.NegotiatedCapabilities.TextDocumentSyncKind);
		Assert.IsEmpty(client.NegotiatedCapabilities.SemanticTokenTypes);
		Assert.IsEmpty(client.NegotiatedCapabilities.SemanticTokenModifiers);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCompletionResolve);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCodeActions);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCodeActionResolve);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsReferences);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsRename);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsFormatting);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsSemanticTokensFull);
		Assert.AreEqual(1, connectionUnavailableCount);
		Assert.AreEqual(5L, unavailableGeneration);

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("generation 5", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("restart", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void ConnectionUnavailable_WhenASubscriberThrows_LaterSubscribersAreStillNotified()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 5,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		int laterSubscriberCount = 0;

		client.ConnectionUnavailable += (_, _) => throw new InvalidOperationException("Simulated subscriber failure.");
		client.ConnectionUnavailable += (_, _) => laterSubscriberCount++;

		Assert.IsTrue(client.TryMarkConnectionUnhealthy(client.ConnectionGeneration));
		Assert.AreEqual(1, laterSubscriberCount);
	}

	[TestMethod]
	public void TryMarkConnectionUnhealthy_CalledTwiceForTheSameGeneration_IsIdempotent()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 5,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		int connectionUnavailableCount = 0;

		client.ConnectionUnavailable += (_, _) => connectionUnavailableCount++;

		Assert.IsTrue(client.TryMarkConnectionUnhealthy(5));
		Assert.IsFalse(client.TryMarkConnectionUnhealthy(5));
		Assert.AreEqual(1, connectionUnavailableCount);
	}

	[TestMethod]
	public void TryMarkConnectionUnhealthy_StaleGenerationDoesNotOverwriteActiveSnapshot()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession oldSession = CreateConnectionSession(
			client,
			generation: 5,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		ConnectionSession newSession = CreateConnectionSession(
			client,
			generation: 6,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, newSession);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "renameProvider": true
				  }
				}
				"""
			)
		);

		SetReadyState(client, true);

		int connectionUnavailableCount = 0;
		client.ConnectionUnavailable += (_, _) => connectionUnavailableCount++;

		bool markedUnhealthy = client.TryMarkConnectionUnhealthy(GetConnectionGeneration(oldSession));

		Assert.IsFalse(markedUnhealthy);

		Assert.AreEqual(6L, client.ConnectionGeneration);
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.Full, client.NegotiatedCapabilities.TextDocumentSyncKind);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsRename);
		Assert.AreEqual(0, connectionUnavailableCount);
	}

	[TestMethod]
	public void DetachActiveSession_ResetsPublishedCapabilitySnapshot()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 9,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "referencesProvider": true,
				    "renameProvider": true,
				    "documentFormattingProvider": true,
				    "semanticTokensProvider": {
				      "full": {
				        "delta": true
				      },
				      "legend": {
				        "tokenTypes": ["function"],
				        "tokenModifiers": ["declaration"]
				      }
				    }
				  }
				}
				"""
			)
		);

		SetReadyState(client, true);

		object? detachedSession = client.Composition.State.DetachActiveSession();

		Assert.AreSame(session, detachedSession);
		Assert.AreEqual(0L, client.ConnectionGeneration);
		Assert.IsFalse(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.None, client.NegotiatedCapabilities.TextDocumentSyncKind);
		Assert.IsEmpty(client.NegotiatedCapabilities.SemanticTokenTypes);
		Assert.IsEmpty(client.NegotiatedCapabilities.SemanticTokenModifiers);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCompletionResolve);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCodeActionResolve);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsReferences);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsRename);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsFormatting);
	}

	[TestMethod]
	public void SetCapabilityReadinessForGeneration_StaleGenerationDoesNotClearActiveSnapshot()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession oldSession = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		ConnectionSession newSession = CreateConnectionSession(
			client,
			generation: 2,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, newSession);

		CaptureServerCapabilities(
			client,

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 1
				    },
				    "documentFormattingProvider": true
				  }
				}
				"""
			)
		);

		SetReadyState(client, true);

		client.Composition.State.MarkCapabilityNotReadyForGeneration(GetConnectionGeneration(oldSession));

		Assert.AreEqual(2L, client.ConnectionGeneration);
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.Full, client.NegotiatedCapabilities.TextDocumentSyncKind);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsFormatting);
	}

	[TestMethod]
	public void CaptureServerCapabilitiesForGeneration_StaleGenerationDoesNotOverwriteActiveCapabilities()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession oldSession = CreateConnectionSession(
			client,
			generation: 3,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		ConnectionSession newSession = CreateConnectionSession(
			client,
			generation: 4,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, newSession);

		client.Composition.State.CaptureServerCapabilitiesForGeneration(
			GetConnectionGeneration(newSession),

			DeserializeInitializeResponse(
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
				        "tokenTypes": ["function"],
				        "tokenModifiers": ["declaration"]
				      }
				    }
				  }
				}
				"""
			)
		);

		client.Composition.State.CaptureServerCapabilitiesForGeneration(
			GetConnectionGeneration(oldSession),

			DeserializeInitializeResponse(
				"""
				{
				  "capabilities": {
				    "textDocumentSync": {
				      "change": 2
				    },
				    "renameProvider": true
				  }
				}
				"""
			)
		);

		Assert.AreEqual(4L, client.ConnectionGeneration);
		Assert.AreEqual(TextDocumentSyncKind.Full, client.NegotiatedCapabilities.TextDocumentSyncKind);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsSemanticTokensFull);

		Assert.AreSequenceEqual(["function"], [.. client.NegotiatedCapabilities.SemanticTokenTypes]);
		Assert.AreSequenceEqual(["declaration"], [.. client.NegotiatedCapabilities.SemanticTokenModifiers]);

		Assert.IsFalse(client.NegotiatedCapabilities.SupportsRename);
	}
}
