using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	public void TryMarkTransportUnhealthy_DuringStartup_PreventsLaterReadinessPublication()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 4, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);

		Assert.IsTrue(client.TryMarkTransportUnhealthy(4));
		Assert.IsFalse(client.IsReady);

		// A late startup completion that only validates the generation number must not resurrect the invalidated transport.
		SetReadyState(client, true);

		Assert.IsFalse(client.IsReady);
	}

	[TestMethod]
	public void GetRequiredReadySession_WhenClientIsNotReady_ThrowsTransportUnavailableException()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);

		Assert.IsFalse(client.IsReady);

		LanguageServerTransportUnavailableException exception = Assert.ThrowsExactly<LanguageServerTransportUnavailableException>(
			() => client.CapabilityStore.GetRequiredReadySession());

		// The whole transport-usability family derives from one base type, so a host needs a single catch clause.
		Assert.IsInstanceOfType<LanguageServerTransportException>(exception);
	}

	[TestMethod]
	public void GetRequiredReadySession_WhenActiveSessionGenerationDoesNotMatchPublishedReadyGeneration_ThrowsTransportUnavailableException()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession readySession = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);
		TransportSession replacementSession = CreateTransportSession(client, 2, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, readySession);
		SetReadyState(client, true);
		client.CapabilityStore.SetActiveSession(replacementSession);

		Assert.ThrowsExactly<LanguageServerTransportUnavailableException>(() => client.CapabilityStore.GetRequiredReadySession());
	}

	[TestMethod]
	public void TryMarkTransportUnhealthy_WhenReady_LogsRestartWarningAndResetsPublishedCapabilities()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 5, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
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
			"""));

		SetReadyState(client, true);

		int transportUnavailableCount = 0;
		long unavailableGeneration = 0;

		client.TransportUnavailable += (_, eventArgs) =>
		{
			transportUnavailableCount++;
			unavailableGeneration = eventArgs.Generation;
		};

		client.TryMarkTransportUnhealthy(client.TransportGeneration);

		Assert.IsFalse(client.IsReady);
		Assert.AreEqual(5L, client.TransportGeneration);
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
		Assert.AreEqual(0, client.SemanticTokenTypes.Count);
		Assert.AreEqual(0, client.SemanticTokenModifiers.Count);
		Assert.IsFalse(client.SupportsCompletionResolve);
		Assert.IsFalse(client.SupportsCodeActions);
		Assert.IsFalse(client.SupportsCodeActionResolve);
		Assert.IsFalse(client.SupportsReferences);
		Assert.IsFalse(client.SupportsRename);
		Assert.IsFalse(client.SupportsFormatting);
		Assert.IsFalse(client.SupportsSemanticTokensFull);
		Assert.AreEqual(1, transportUnavailableCount);
		Assert.AreEqual(5L, unavailableGeneration);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("generation 5", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("restart", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void TransportUnavailable_WhenASubscriberThrows_LaterSubscribersAreStillNotified()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 5, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		int laterSubscriberCount = 0;

		client.TransportUnavailable += (_, _) => throw new InvalidOperationException("Simulated subscriber failure.");
		client.TransportUnavailable += (_, _) => laterSubscriberCount++;

		Assert.IsTrue(client.TryMarkTransportUnhealthy(client.TransportGeneration));
		Assert.AreEqual(1, laterSubscriberCount);
	}

	[TestMethod]
	public void TryMarkTransportUnhealthy_CalledTwiceForTheSameGeneration_IsIdempotent()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 5, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		int transportUnavailableCount = 0;

		client.TransportUnavailable += (_, _) => transportUnavailableCount++;

		Assert.IsTrue(client.TryMarkTransportUnhealthy(5));
		Assert.IsFalse(client.TryMarkTransportUnhealthy(5));
		Assert.AreEqual(1, transportUnavailableCount);
	}

	[TestMethod]
	public void TryMarkTransportUnhealthy_StaleGenerationDoesNotOverwriteActiveSnapshot()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession oldSession = CreateTransportSession(client, 5, process: null, Stream.Null, Stream.Null);
		TransportSession newSession = CreateTransportSession(client, 6, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, newSession);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    },
			    "renameProvider": true
			  }
			}
			"""));

		SetReadyState(client, true);

		int transportUnavailableCount = 0;
		client.TransportUnavailable += (_, _) => transportUnavailableCount++;

		bool markedUnhealthy = client.TryMarkTransportUnhealthy(GetTransportGeneration(oldSession));

		Assert.IsFalse(markedUnhealthy);

		Assert.AreEqual(6L, client.TransportGeneration);
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.Full, client.TextDocumentSyncKind);
		Assert.IsTrue(client.SupportsRename);
		Assert.AreEqual(0, transportUnavailableCount);
	}

	[TestMethod]
	public void DetachActiveSession_ResetsPublishedCapabilitySnapshot()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 9, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
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
			"""));

		SetReadyState(client, true);

		object? detachedSession = client.CapabilityStore.DetachActiveSession();

		Assert.AreSame(session, detachedSession);
		Assert.AreEqual(0L, client.TransportGeneration);
		Assert.IsFalse(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
		Assert.AreEqual(0, client.SemanticTokenTypes.Count);
		Assert.AreEqual(0, client.SemanticTokenModifiers.Count);
		Assert.IsFalse(client.SupportsCompletionResolve);
		Assert.IsFalse(client.SupportsCodeActionResolve);
		Assert.IsFalse(client.SupportsReferences);
		Assert.IsFalse(client.SupportsRename);
		Assert.IsFalse(client.SupportsFormatting);
	}

	[TestMethod]
	public void SetCapabilityReadinessForGeneration_StaleGenerationDoesNotClearActiveSnapshot()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession oldSession = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);
		TransportSession newSession = CreateTransportSession(client, 2, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, newSession);

		CaptureServerCapabilities(client, DeserializeInitializeResponse(
			"""
			{
			  "capabilities": {
			    "textDocumentSync": {
			      "change": 1
			    },
			    "documentFormattingProvider": true
			  }
			}
			"""));

		SetReadyState(client, true);

		client.CapabilityStore.SetCapabilityReadinessForGeneration(GetTransportGeneration(oldSession), false);

		Assert.AreEqual(2L, client.TransportGeneration);
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(TextDocumentSyncKind.Full, client.TextDocumentSyncKind);
		Assert.IsTrue(client.SupportsFormatting);
	}

	[TestMethod]
	public void CaptureServerCapabilitiesForGeneration_StaleGenerationDoesNotOverwriteActiveCapabilities()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession oldSession = CreateTransportSession(client, 3, process: null, Stream.Null, Stream.Null);
		TransportSession newSession = CreateTransportSession(client, 4, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, newSession);

		client.CapabilityStore.CaptureServerCapabilitiesForGeneration(
			GetTransportGeneration(newSession),
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
			"""));

		client.CapabilityStore.CaptureServerCapabilitiesForGeneration(
			GetTransportGeneration(oldSession),
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
			"""));

		Assert.AreEqual(4L, client.TransportGeneration);
		Assert.AreEqual(TextDocumentSyncKind.Full, client.TextDocumentSyncKind);
		Assert.IsTrue(client.SupportsSemanticTokensFull);

		CollectionAssert.AreEqual(new[] { "function" }, client.SemanticTokenTypes.ToArray());
		CollectionAssert.AreEqual(new[] { "declaration" }, client.SemanticTokenModifiers.ToArray());

		Assert.IsFalse(client.SupportsRename);
	}
}
