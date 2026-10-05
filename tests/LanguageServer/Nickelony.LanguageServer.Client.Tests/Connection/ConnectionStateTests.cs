namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ConnectionStateTests
{
	private static readonly InitializeResponse s_hoverServer = WireJsonTestHelpers.Deserialize<InitializeResponse>(
		"""{ "capabilities": { "textDocumentSync": 2, "hoverProvider": true } }"""
	);

	private static ConnectionState NewState(bool requireTextDocumentSynchronization = false) => new(requireTextDocumentSynchronization);

	private static ConnectionSession NewSession(long generation) =>
		ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation, process: null);

	[TestMethod]
	public void NewState_IsNotReadyAndHasNoGenerationOrSession()
	{
		ConnectionState state = NewState();

		Assert.IsFalse(state.IsReady);
		Assert.AreEqual(0, state.ConnectionGeneration);
		Assert.IsNull(state.ActiveSession);
		Assert.AreEqual(NegotiatedServerCapabilities.None, state.Capabilities);
	}

	[TestMethod]
	public void NextConnectionGeneration_StartsAtOneAndNeverRepeats()
	{
		ConnectionState state = NewState();

		Assert.AreEqual(1, state.NextConnectionGeneration());
		Assert.AreEqual(2, state.NextConnectionGeneration());
		Assert.AreEqual(3, state.NextConnectionGeneration());
	}

	[TestMethod]
	public void SetActiveSession_PublishesTheGenerationAcceptingServerMessagesButNotReady()
	{
		ConnectionState state = NewState();
		ConnectionSession session = NewSession(generation: 1);

		state.SetActiveSession(session);

		Assert.AreSame(session, state.ActiveSession);
		Assert.AreEqual(1, state.ConnectionGeneration);
		Assert.IsFalse(state.IsReady);
		Assert.IsTrue(state.CanAcceptServerMessagesForGeneration(1));
	}

	[TestMethod]
	public void MarkCapabilityReadyForGeneration_CurrentGeneration_PublishesReadiness()
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 1));

		state.MarkCapabilityReadyForGeneration(1);

		Assert.IsTrue(state.IsReady);
	}

	[TestMethod]
	public void MarkCapabilityReadyForGeneration_StaleGeneration_IsIgnored()
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 2));

		state.MarkCapabilityReadyForGeneration(1);

		Assert.IsFalse(state.IsReady);
	}

	[TestMethod]
	public void MarkCapabilityNotReadyForGeneration_CurrentGeneration_ResetsTheSnapshotButKeepsTheGeneration()
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 1));
		state.MarkCapabilityReadyForGeneration(1);

		state.MarkCapabilityNotReadyForGeneration(1);

		Assert.IsFalse(state.IsReady);
		Assert.AreEqual(1, state.ConnectionGeneration);
		Assert.IsFalse(state.CanAcceptServerMessagesForGeneration(1));
	}

	[TestMethod]
	public void CaptureServerCapabilitiesForGeneration_CurrentGeneration_PublishesTheNegotiatedCapabilities()
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 1));

		state.CaptureServerCapabilitiesForGeneration(1, s_hoverServer);

		Assert.IsTrue(state.Capabilities.SupportsHover);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, state.Capabilities.TextDocumentSyncKind);
	}

	[TestMethod]
	public void CaptureServerCapabilitiesForGeneration_StaleGeneration_PublishesNothing()
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 2));

		state.CaptureServerCapabilitiesForGeneration(1, s_hoverServer);

		Assert.IsFalse(state.Capabilities.SupportsHover);
	}

	[TestMethod]
	public void CaptureServerCapabilitiesForGeneration_ResponseTheClientCannotUse_ThrowsAndPublishesNothing()
	{
		ConnectionState state = NewState(requireTextDocumentSynchronization: true);
		state.SetActiveSession(NewSession(generation: 1));

		Assert.ThrowsExactly<NotSupportedException>(() =>
			state.CaptureServerCapabilitiesForGeneration(
				1,
				WireJsonTestHelpers.Deserialize<InitializeResponse>("""{ "capabilities": { "hoverProvider": true } }""")
			)
		);

		Assert.IsFalse(state.Capabilities.SupportsHover);
	}

	[TestMethod]
	public void TryMarkConnectionUnhealthy_CurrentGeneration_ResetsTheSnapshotAndReportsWhetherItWasReady()
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 1));
		state.MarkCapabilityReadyForGeneration(1);

		Assert.IsTrue(state.TryMarkConnectionUnhealthy(1, out bool wasReady));

		Assert.IsTrue(wasReady);
		Assert.IsFalse(state.IsReady);
		Assert.AreEqual(1, state.ConnectionGeneration);
	}

	[TestMethod]
	public void TryMarkConnectionUnhealthy_BeforeReadiness_ReportsNotReady()
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 1));

		Assert.IsTrue(state.TryMarkConnectionUnhealthy(1, out bool wasReady));
		Assert.IsFalse(wasReady);
	}

	[TestMethod]
	public void TryMarkConnectionUnhealthy_SecondCallForTheSameGeneration_IsANoOp()
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 1));

		Assert.IsTrue(state.TryMarkConnectionUnhealthy(1, out _));
		Assert.IsFalse(state.TryMarkConnectionUnhealthy(1, out bool wasReady));
		Assert.IsFalse(wasReady);
	}

	[TestMethod]
	[DataRow(0L, DisplayName = "the no-connection sentinel")]
	[DataRow(-1L, DisplayName = "a negative generation")]
	[DataRow(2L, DisplayName = "a stale or unknown generation")]
	public void TryMarkConnectionUnhealthy_GenerationThatDoesNotOwnTheSnapshot_IsRejected(long generation)
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 1));
		state.MarkCapabilityReadyForGeneration(1);

		Assert.IsFalse(state.TryMarkConnectionUnhealthy(generation, out _));
		Assert.IsTrue(state.IsReady);
	}

	[TestMethod]
	public void MarkCapabilityReadyForGeneration_AfterTheGenerationWasMarkedUnhealthy_DoesNotRepublishReadiness()
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 1));
		Assert.IsTrue(state.TryMarkConnectionUnhealthy(1, out _));

		// A startup that was still in flight when the generation was invalidated must not publish it as ready.
		state.MarkCapabilityReadyForGeneration(1);

		Assert.IsFalse(state.IsReady);
	}

	[TestMethod]
	public void CaptureServerCapabilitiesForGeneration_AfterTheGenerationWasMarkedUnhealthy_DoesNotRepublishCapabilities()
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 1));
		Assert.IsTrue(state.TryMarkConnectionUnhealthy(1, out _));

		state.CaptureServerCapabilitiesForGeneration(1, s_hoverServer);

		Assert.IsFalse(state.Capabilities.SupportsHover);
	}

	[TestMethod]
	public void DetachActiveSession_ReturnsTheSessionAndResetsTheSnapshotToNoGeneration()
	{
		ConnectionState state = NewState();
		ConnectionSession session = NewSession(generation: 1);

		state.SetActiveSession(session);
		state.MarkCapabilityReadyForGeneration(1);

		Assert.AreSame(session, state.DetachActiveSession());

		Assert.IsNull(state.ActiveSession);
		Assert.IsFalse(state.IsReady);
		Assert.AreEqual(0, state.ConnectionGeneration);
		Assert.IsNull(state.DetachActiveSession());
	}

	[TestMethod]
	public void TryDetachSpecificActiveSession_OnlyDetachesTheSessionThatIsStillActive()
	{
		ConnectionState state = NewState();

		ConnectionSession first = NewSession(generation: 1);
		ConnectionSession second = NewSession(generation: 2);

		state.SetActiveSession(first);
		state.SetActiveSession(second);

		state.MarkCapabilityReadyForGeneration(2);

		Assert.IsFalse(state.TryDetachSpecificActiveSession(first, out bool staleWasReady));
		Assert.IsFalse(staleWasReady);
		Assert.AreSame(second, state.ActiveSession);

		Assert.IsTrue(state.TryDetachSpecificActiveSession(second, out bool wasReady));
		Assert.IsTrue(wasReady);
		Assert.IsNull(state.ActiveSession);
	}

	[TestMethod]
	public void GetRequiredReadySession_BeforeReadiness_ThrowsConnectionUnavailable()
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 1));

		Assert.ThrowsExactly<LanguageServerConnectionUnavailableException>(state.GetRequiredReadySession);
	}

	[TestMethod]
	public void GetRequiredReadySession_WhenReady_ReturnsTheActiveSession()
	{
		ConnectionState state = NewState();
		ConnectionSession session = NewSession(generation: 1);

		state.SetActiveSession(session);
		state.MarkCapabilityReadyForGeneration(1);

		Assert.AreSame(session, state.GetRequiredReadySession());
	}

	[TestMethod]
	public void CanAcceptRequestResultForSession_OnlyForTheReadyActiveSession()
	{
		ConnectionState state = NewState();
		ConnectionSession first = NewSession(generation: 1);

		state.SetActiveSession(first);

		Assert.IsFalse(state.CanAcceptRequestResultForSession(first), "A session that is not ready yet cannot deliver results.");

		state.MarkCapabilityReadyForGeneration(1);

		Assert.IsTrue(state.CanAcceptRequestResultForSession(first));

		state.SetActiveSession(NewSession(generation: 2));

		Assert.IsFalse(state.CanAcceptRequestResultForSession(first), "A superseded session cannot deliver results.");
	}

	[TestMethod]
	public void CanAcceptServerMessagesForGeneration_RejectsGenerationZeroAndStaleGenerations()
	{
		ConnectionState state = NewState();
		state.SetActiveSession(NewSession(generation: 2));

		Assert.IsTrue(state.CanAcceptServerMessagesForGeneration(2));
		Assert.IsFalse(state.CanAcceptServerMessagesForGeneration(0));
		Assert.IsFalse(state.CanAcceptServerMessagesForGeneration(1));
		Assert.IsFalse(state.CanAcceptServerMessagesForGeneration(3));
	}
}
