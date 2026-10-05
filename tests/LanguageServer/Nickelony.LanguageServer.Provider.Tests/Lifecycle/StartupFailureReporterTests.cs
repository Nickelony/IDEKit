using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers how a startup failure reaches the log and the host, and that each persistence reaches the host once per streak.</summary>
[TestClass]
public sealed class StartupFailureReporterTests
{
	private sealed class TestHost : IDisposable
	{
		private readonly object _syncRoot = new();
		private readonly List<StartupFailure> _failures = [];

		internal TestHost(
			int hardStartupFailureThreshold = 3,
			string? transientMessage = "start failed persistent=False",
			string? persistentMessage = "start failed persistent=True",
			string? missingClientMessage = "no client"
		)
		{
			Events = new ProviderEvents(new object(), Disposal, Logger);

			Events.StartupFailed += (_, eventArguments) =>
			{
				lock (_syncRoot)
					_failures.Add(eventArguments.Failure);
			};

			StartupState = new StartupState(Client, Disposal);

			Reporter = new StartupFailureReporter(
				new StartupFailureReporterContext(
					StartupState,
					Events,
					WorkspaceRootsDisplayText: "workspace",
					ProviderDisplayName: "Test",
					TransientStartupFailureMessage: transientMessage,
					PersistentStartupFailureMessage: persistentMessage,
					MissingClientFailureMessage: missingClientMessage,
					Options: new LanguageServerFeatureProviderOptions { HardStartupFailureThreshold = hardStartupFailureThreshold },
					Logger
				)
			);
		}

		internal FakeLanguageServerClient Client { get; } = new() { IsReady = true, ConnectionGeneration = 1 };

		internal ProviderLifetime Disposal { get; } = new();

		internal RecordingLogger Logger { get; } = new();

		internal ProviderEvents Events { get; }

		internal StartupState StartupState { get; }

		internal StartupFailureReporter Reporter { get; }

		internal IReadOnlyList<StartupFailure> Failures
		{
			get
			{
				lock (_syncRoot)
					return [.. _failures];
			}
		}

		public void Dispose()
		{
			Client.Dispose();
			Logger.Dispose();
		}
	}

	private static int CountLogs(TestHost fixture, LogLevel level) => fixture.Logger.Entries.Count(entry => entry.Level == level);

	[TestMethod]
	public void ReportStartupFailure_ForATransientFailure_LogsAWarningNamingTheAttemptAndRaisesTheFailure()
	{
		using var fixture = new TestHost(hardStartupFailureThreshold: 3);

		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 1, isPersistentFailure: false);

		Assert.Contains("Warn|Failed to start the Test language server for workspace 'workspace' (attempt 1/3).|", fixture.Logger.Logs);

		StartupFailure failure = Assert.ContainsSingle(fixture.Failures);

		Assert.AreEqual("start failed persistent=False", failure.Message);
		Assert.AreEqual(ProviderFailureCodes.StartFailed, failure.Code);
		Assert.IsNull(failure.Exception);
		Assert.IsFalse(failure.IsPersistent);
	}

	[TestMethod]
	public void ReportStartupFailure_WithAnException_RaisesTheFailureWithThatException()
	{
		using var fixture = new TestHost(hardStartupFailureThreshold: 3);
		var exception = new InvalidOperationException("The server did not start.");

		fixture.Reporter.ReportStartupFailure(exception, consecutiveStartupFailures: 1, isPersistentFailure: false);

		StartupFailure failure = Assert.ContainsSingle(fixture.Failures);

		Assert.AreSame(exception, failure.Exception);
	}

	[TestMethod]
	public void ReportStartupFailure_ForRepeatedTransientFailures_LogsEachAttemptAndRaisesTheFailureOnce()
	{
		using var fixture = new TestHost(hardStartupFailureThreshold: 5);

		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 1, isPersistentFailure: false);
		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 2, isPersistentFailure: false);
		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 3, isPersistentFailure: false);

		Assert.AreEqual(3, CountLogs(fixture, LogLevel.Warning));
		Assert.HasCount(1, fixture.Failures);
	}

	[TestMethod]
	public void ReportStartupFailure_ForAPersistentFailure_LogsAnErrorAndRaisesItOnceBesideTheTransientOne()
	{
		using var fixture = new TestHost(hardStartupFailureThreshold: 2);

		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 1, isPersistentFailure: false);
		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 2, isPersistentFailure: true);
		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 3, isPersistentFailure: true);

		// Each persistence reaches the host once, but every attempt is logged.
		Assert.AreEqual(1, CountLogs(fixture, LogLevel.Warning));
		Assert.AreEqual(2, CountLogs(fixture, LogLevel.Error));
		Assert.AreSequenceEqual([false, true], fixture.Failures.Select(static failure => failure.IsPersistent));

		// The message follows the persistence of each failure, so the host can tell a retrying provider from a stopped one.
		Assert.AreSequenceEqual(
			["start failed persistent=False", "start failed persistent=True"],
			fixture.Failures.Select(static failure => failure.Message)
		);

		Assert.Contains(
			static log =>
				log.StartsWith("Error|Test language server failed to start", StringComparison.Ordinal)
				&& log.Contains("times consecutively", StringComparison.Ordinal),

			fixture.Logger.Logs
		);
	}

	[TestMethod]
	public void ReportStartupFailure_AfterASuccessfulStart_ReportsTheNextFailureOfEachPersistenceAgain()
	{
		using var fixture = new TestHost();

		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 1, isPersistentFailure: false);
		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 2, isPersistentFailure: true);

		Assert.HasCount(2, fixture.Failures);

		Assert.IsTrue(fixture.StartupState.TryCompleteSuccessfulStart(connectionGeneration: 1, out _));

		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 1, isPersistentFailure: false);
		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 2, isPersistentFailure: true);

		Assert.HasCount(4, fixture.Failures);
	}

	[TestMethod]
	public void ReportStartupFailure_WithAnException_AttachesItToTheLogEntry()
	{
		using var fixture = new TestHost(hardStartupFailureThreshold: 2);

		fixture.Reporter.ReportStartupFailure(
			new InvalidOperationException("the server crashed"),
			consecutiveStartupFailures: 1,
			isPersistentFailure: false
		);

		fixture.Reporter.ReportStartupFailure(
			new InvalidOperationException("the server crashed again"),
			consecutiveStartupFailures: 2,
			isPersistentFailure: true
		);

		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.EndsWith("|the server crashed", StringComparison.Ordinal),
			fixture.Logger.Logs
		);

		Assert.Contains(
			static log => log.StartsWith("Error|", StringComparison.Ordinal) && log.EndsWith("|the server crashed again", StringComparison.Ordinal),
			fixture.Logger.Logs
		);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("   ")]
	public void ReportStartupFailure_WhenTheMessageIsBlank_RaisesNothing(string? blankMessage)
	{
		using var fixture = new TestHost(transientMessage: blankMessage);

		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 1, isPersistentFailure: false);

		Assert.IsEmpty(fixture.Failures);
		Assert.AreEqual(1, CountLogs(fixture, LogLevel.Warning));
	}

	[TestMethod]
	public void ReportStartupFailure_WhenTheTransientMessageIsBlank_StillReportsTheLaterPersistentFailure()
	{
		using var fixture = new TestHost(transientMessage: " ");

		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 1, isPersistentFailure: false);
		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 2, isPersistentFailure: true);

		// The two persistences are claimed separately, so a blank transient message does not swallow the persistent report.
		StartupFailure failure = Assert.ContainsSingle(fixture.Failures);

		Assert.IsTrue(failure.IsPersistent);
		Assert.AreEqual("start failed persistent=True", failure.Message);
	}

	[TestMethod]
	public void ReportMissingClientFailure_LogsAnErrorAndRaisesAPersistentFailureOnce()
	{
		using var fixture = new TestHost();

		fixture.Reporter.ReportMissingClientFailure();
		fixture.Reporter.ReportMissingClientFailure();

		Assert.AreEqual(1, CountLogs(fixture, LogLevel.Error));

		StartupFailure failure = Assert.ContainsSingle(fixture.Failures);

		Assert.AreEqual("no client", failure.Message);
		Assert.AreEqual(ProviderFailureCodes.ClientUnavailable, failure.Code);
		Assert.IsTrue(failure.IsPersistent);
	}

	[TestMethod]
	public void ReportMissingClientFailure_WhenTheMessageIsBlank_LogsButRaisesNothing()
	{
		using var fixture = new TestHost(missingClientMessage: " ");

		fixture.Reporter.ReportMissingClientFailure();

		Assert.AreEqual(1, CountLogs(fixture, LogLevel.Error));
		Assert.IsEmpty(fixture.Failures);
	}

	[TestMethod]
	public void ReportMissingClientFailure_AfterAPersistentStartupFailure_ReportsNothingMore()
	{
		using var fixture = new TestHost(hardStartupFailureThreshold: 1);

		fixture.Reporter.ReportStartupFailure(exception: null, consecutiveStartupFailures: 1, isPersistentFailure: true);
		fixture.Reporter.ReportMissingClientFailure();

		// Both reports share the one persistent claim, so the host hears of a persistent failure once.
		Assert.HasCount(1, fixture.Failures);
		Assert.AreEqual(1, CountLogs(fixture, LogLevel.Error));
	}
}
