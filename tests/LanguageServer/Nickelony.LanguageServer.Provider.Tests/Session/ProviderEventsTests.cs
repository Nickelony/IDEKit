using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the provider events' admission, isolation and closing rules directly.</summary>
[TestClass]
public sealed class ProviderEventsTests
{
	private static readonly Diagnostic[] s_diagnostics = [new(DiagnosticSeverity.Error, "message", new TextRange(0, 1))];

	[TestMethod]
	public void RaiseDiagnosticsUpdated_PassesTheSenderAndTheEventData()
	{
		object sender = new();

		var gate = new ProviderEvents(sender, new ProviderLifetime(), NullLogger.Instance);

		object? observedSender = null;
		DiagnosticsUpdatedEventArgs? observedArguments = null;

		gate.DiagnosticsUpdated += (eventSender, eventArguments) =>
		{
			observedSender = eventSender;
			observedArguments = eventArguments;
		};

		gate.RaiseDiagnosticsUpdated("file.lua", s_diagnostics);

		Assert.AreSame(sender, observedSender);
		Assert.AreEqual("file.lua", observedArguments?.DocumentId);
		Assert.AreSequenceEqual(s_diagnostics, observedArguments?.Diagnostics);
	}

	[TestMethod]
	public void EachRaiseReachesOnlyItsOwnEvent()
	{
		var gate = new ProviderEvents(new object(), new ProviderLifetime(), NullLogger.Instance);
		List<string> raised = [];

		gate.DiagnosticsUpdated += (_, _) => raised.Add("diagnostics");
		gate.SemanticTokensUpdated += (_, _) => raised.Add("semantic tokens");
		gate.CapabilitiesChanged += (_, _) => raised.Add("capabilities");
		gate.StartupFailed += (_, _) => raised.Add("startup failure");
		gate.WorkspaceFileWatchFailed += (_, _) => raised.Add("watcher failure");

		gate.RaiseSemanticTokensUpdated("file.lua", []);
		gate.RaiseWorkspaceFileWatchFailed(new WorkspaceFileWatchFailure(ProviderFailureCodes.FileWatchStartFailed, "failed"));
		gate.RaiseCapabilitiesChanged();
		gate.RaiseStartupFailed(new StartupFailure(ProviderFailureCodes.StartFailed, "failed", isPersistent: false));
		gate.RaiseDiagnosticsUpdated("file.lua", s_diagnostics);

		Assert.AreSequenceEqual(["semantic tokens", "watcher failure", "capabilities", "startup failure", "diagnostics"], raised);
	}

	[TestMethod]
	public void Remove_StopsTheHandler()
	{
		var gate = new ProviderEvents(new object(), new ProviderLifetime(), NullLogger.Instance);

		int count = 0;
		void CountRaise(object? _1, EventArgs _2) => count++;

		gate.CapabilitiesChanged += CountRaise;
		gate.RaiseCapabilitiesChanged();
		gate.CapabilitiesChanged -= CountRaise;
		gate.RaiseCapabilitiesChanged();

		Assert.AreEqual(1, count);
	}

	[TestMethod]
	public void Raise_AfterClose_InvokesNothingForSubscribersAddedBefore()
	{
		var gate = new ProviderEvents(new object(), new ProviderLifetime(), NullLogger.Instance);
		int count = 0;

		gate.CapabilitiesChanged += (_, _) => count++;
		gate.Close();

		gate.RaiseCapabilitiesChanged();

		Assert.AreEqual(0, count);
	}

	[TestMethod]
	public void Close_ReleasesTheSubscriberReferences()
	{
		var gate = new ProviderEvents(new object(), new ProviderLifetime(), NullLogger.Instance);
		WeakReference subscriber = Subscribe(gate);

		gate.Close();

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Assert.IsFalse(subscriber.IsAlive, "The gate must not retain a subscriber after it closed.");

		// The events stay reachable during the collection, so the assertion cannot pass because the whole object was collected.
		GC.KeepAlive(gate);
	}

	[TestMethod]
	public void Raise_AfterDisposalStarted_InvokesNothingBeforeTheEventsClose()
	{
		var disposal = new ProviderLifetime();
		var gate = new ProviderEvents(new object(), disposal, NullLogger.Instance);

		int count = 0;

		gate.CapabilitiesChanged += (_, _) => count++;
		disposal.Signal();

		gate.RaiseCapabilitiesChanged();

		Assert.AreEqual(0, count);
	}

	[TestMethod]
	public void Add_AfterDisposalStarted_KeepsNoReferenceToTheHandler()
	{
		var disposal = new ProviderLifetime();
		var gate = new ProviderEvents(new object(), disposal, NullLogger.Instance);

		disposal.Signal();

		// The disposal alone stops admission, so the events keep nothing even though they were never closed.
		WeakReference subscriber = Subscribe(gate);

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Assert.IsFalse(subscriber.IsAlive, "The events must not keep a handler added after disposal started.");

		GC.KeepAlive(gate);
	}

	[TestMethod]
	public void Close_IsIdempotent()
	{
		var disposal = new ProviderLifetime();
		var gate = new ProviderEvents(new object(), disposal, NullLogger.Instance);

		int count = 0;

		gate.CapabilitiesChanged += (_, _) => count++;
		disposal.Signal();

		gate.Close();
		gate.Close();

		gate.RaiseCapabilitiesChanged();

		Assert.AreEqual(0, count);
	}

	[TestMethod]
	public void Raise_WhenASubscriberThrows_LogsAndStillNotifiesLaterSubscribers()
	{
		using var logger = new RecordingLogger();
		var gate = new ProviderEvents(new object(), new ProviderLifetime(), logger);

		int laterCount = 0;

		gate.CapabilitiesChanged += (_, _) => throw new InvalidOperationException("subscriber failure");
		gate.CapabilitiesChanged += (_, _) => laterCount++;

		gate.RaiseCapabilitiesChanged();

		Assert.AreEqual(1, laterCount);
		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning));

		Assert.Contains(
			log =>
				log.Contains("Capability-change subscriber", StringComparison.Ordinal)
				&& log.Contains("subscriber failure", StringComparison.Ordinal),

			logger.Logs
		);
	}

	[TestMethod]
	public void Raise_WhenDisposalStartsInASubscriber_SkipsLaterSubscribers()
	{
		var disposal = new ProviderLifetime();
		var gate = new ProviderEvents(new object(), disposal, NullLogger.Instance);

		int laterCount = 0;

		gate.CapabilitiesChanged += (_, _) => disposal.Signal();
		gate.CapabilitiesChanged += (_, _) => laterCount++;

		gate.RaiseCapabilitiesChanged();

		Assert.AreEqual(0, laterCount);
	}

	[TestMethod]
	public void Raise_InvokesHandlersOutsideTheLock()
	{
		var gate = new ProviderEvents(new object(), new ProviderLifetime(), NullLogger.Instance);
		bool otherThreadFinished = false;

		gate.CapabilitiesChanged += (_, _) =>
		{
			// A second thread that subscribes needs the gate's lock, so it can only finish while the lock is free.
			var otherThread = Task.Run(() => gate.StartupFailed += static (_, _) => { });
			otherThreadFinished = otherThread.Wait(TestPolling.DefaultTimeout);
		};

		gate.RaiseCapabilitiesChanged();

		Assert.IsTrue(otherThreadFinished, "A handler ran while the raise still held the gate's lock.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference Subscribe(ProviderEvents gate)
	{
		object target = new();

		gate.CapabilitiesChanged += (_, _) => GC.KeepAlive(target);

		return new WeakReference(target);
	}
}
