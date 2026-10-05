using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class ProjectDiagnosticsSourceTests
{
	private const string FirstId = "first.script";
	private const string SecondId = "second.script";

	private static readonly TimeSpan s_settleDelay = TimeSpan.FromMilliseconds(200.0);

	private static readonly Diagnostic s_firstError = Error("first error", 0);
	private static readonly Diagnostic s_firstChanged = Error("first error, reworded", 0);
	private static readonly Diagnostic s_secondError = Error("second error", 5);
	private static readonly Diagnostic s_secondChanged = Error("second error, reworded", 5);

	[TestMethod]
	public void HandleProjectChanged_AfterTheSettleDelay_RunsOneAnalysis_AndRaisesOncePerDocumentWithDiagnostics()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		// The source has no analysis until the host reports a change.
		host.Dispatcher.Advance(TimeSpan.FromSeconds(5.0));
		Assert.IsEmpty(host.Analyzer.Runs);

		host.Analyzer.SetAnswer(Result((FirstId, [s_firstError]), (SecondId, [s_secondError])));
		source.HandleProjectChanged();
		host.Dispatcher.Advance(s_settleDelay - TimeSpan.FromMilliseconds(1.0));

		Assert.IsEmpty(host.Analyzer.Runs);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(1.0));
		host.Dispatcher.PumpUntil(() => host.Updates.Count == 2);

		Assert.HasCount(1, host.Analyzer.Runs);

		Assert.AreSequenceEqual(
			[FirstId, SecondId],
			[.. host.Updates.Select(update => update.DocumentId)],
			Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
		);

		Assert.AreSequenceEqual([s_firstError], [.. source.GetDiagnostics(FirstId)]);
		Assert.AreSequenceEqual([s_secondError], [.. source.GetDiagnostics(SecondId)]);
		Assert.IsEmpty(source.GetDiagnostics("unknown.script"));
	}

	[TestMethod]
	public void HandleProjectChanged_RepeatedChanges_AnalyzeOnceAfterTheLastOne()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		host.Analyzer.SetAnswer(Result((FirstId, [s_firstError])));

		// Each change restarts the delay, so five changes 100 milliseconds apart start nothing until the last one settles.
		for (int change = 0; change < 5; change++)
		{
			source.HandleProjectChanged();
			host.Dispatcher.Advance(TimeSpan.FromMilliseconds(100.0));
		}

		Assert.IsEmpty(host.Analyzer.Runs);

		host.Dispatcher.Advance(s_settleDelay);
		host.Dispatcher.PumpUntil(() => host.Updates.Count == 1);

		Assert.HasCount(1, host.Analyzer.Runs);
	}

	[TestMethod]
	public void ChangeInOneDocument_ReanalyzesBoth_AndRaisesOnceForEachDocumentWhoseSetChanged()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		host.RunAnalysis(source, Result((FirstId, [s_firstError]), (SecondId, [s_secondError])), expectedUpdates: 2);

		// One analysis covers the whole project, so a change reported for the first document also moves the second document's set.
		host.RunAnalysis(source, Result((FirstId, [s_firstChanged]), (SecondId, [s_secondChanged])), expectedUpdates: 4);

		Assert.HasCount(2, host.Analyzer.Runs);

		Assert.AreSequenceEqual(
			[FirstId, SecondId],
			[.. host.Updates.Skip(2).Select(update => update.DocumentId)],
			Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
		);

		Assert.AreSequenceEqual([s_firstChanged], [.. source.GetDiagnostics(FirstId)]);
		Assert.AreSequenceEqual([s_secondChanged], [.. source.GetDiagnostics(SecondId)]);

		// A document whose set is the same as before raises nothing.
		host.RunAnalysis(source, Result((FirstId, [s_firstChanged]), (SecondId, [s_secondError])), expectedUpdates: 5);

		Assert.AreEqual(SecondId, host.Updates[4].DocumentId);

		host.Settle();

		Assert.HasCount(5, host.Updates);
	}

	[TestMethod]
	public void VanishedSet_RaisesOneEmptySet_AndStaysQuiet()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		host.RunAnalysis(source, Result((FirstId, [s_firstError]), (SecondId, [s_secondError])), expectedUpdates: 2);

		// The first document is absent from the result, so its underlines must clear.
		host.RunAnalysis(source, Result((SecondId, [s_secondError])), expectedUpdates: 3);

		Assert.AreEqual(FirstId, host.Updates[2].DocumentId);
		Assert.IsEmpty(host.Updates[2].Diagnostics);
		Assert.IsEmpty(source.GetDiagnostics(FirstId));
		Assert.AreSequenceEqual([s_secondError], [.. source.GetDiagnostics(SecondId)]);

		// Listing the first document with no diagnostics, or listing a document that never had any, changes nothing.
		host.RunAnalysis(source, Result((FirstId, []), (SecondId, [s_secondError]), ("third.script", [])), expectedUpdates: 3);
		host.Settle();

		Assert.HasCount(3, host.Updates);
	}

	[TestMethod]
	public void SupersededRun_IsCanceled_AndItsLateResultRaisesNothing()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		PendingAnalysis superseded = host.StartHeldAnalysis(source, expectedRuns: 1);

		// A change lands while the analysis runs: its text is gone, so its token is canceled at once.
		source.HandleProjectChanged();

		Assert.IsTrue(superseded.Token.IsCancellationRequested);

		// The analysis ignores the token and completes anyway, and the next run starts only after that.
		host.Dispatcher.Advance(s_settleDelay);
		superseded.Complete(Result((FirstId, [s_firstError])));
		host.Dispatcher.PumpUntil(() => host.Analyzer.Runs.Count == 2);
		host.Settle();

		PendingAnalysis newest = host.Analyzer.Runs[1];

		Assert.IsEmpty(host.Updates);
		Assert.IsEmpty(source.GetDiagnostics(FirstId));

		newest.Complete(Result((FirstId, [s_firstChanged])));
		host.Dispatcher.PumpUntil(() => host.Updates.Count == 1);

		Assert.AreSequenceEqual([s_firstChanged], [.. host.Updates[0].Diagnostics]);
	}

	[TestMethod]
	public void ResultCompletingAfterALaterChange_IsDropped_BeforeTheNextRunStarts()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		PendingAnalysis analysis = host.StartHeldAnalysis(source, expectedRuns: 1);

		// The change restarts the delay, and the analysis completes inside it, so no newer run exists yet to supersede the old one.
		source.HandleProjectChanged();
		analysis.Complete(Result((FirstId, [s_firstError])));
		host.Settle();

		Assert.IsEmpty(host.Updates);
		Assert.HasCount(1, host.Analyzer.Runs);
	}

	[TestMethod]
	public void ResultAlreadyPostedToTheDispatcher_IsDropped_WhenAChangeIsReportedBeforeItRuns()
	{
		using var host = new Host(installSynchronizationContext: false);
		using ProjectDiagnosticsSource source = host.Create();

		PendingAnalysis analysis = host.StartHeldAnalysis(source, expectedRuns: 1);

		analysis.Complete(Result((FirstId, [s_firstError])));
		host.WaitForQueuedWork(2);

		// The result waits on the dispatcher queue, and the host reports its change before the queue runs it.
		source.HandleProjectChanged();
		host.Dispatcher.PumpUntilIdle();

		Assert.IsEmpty(host.Updates);
		Assert.IsEmpty(source.GetDiagnostics(FirstId));
	}

	[TestMethod]
	public void ResultAlreadyPostedToTheDispatcher_IsDropped_WhenTheSourceIsDisposedBeforeItRuns()
	{
		using var host = new Host(installSynchronizationContext: false);
		using ProjectDiagnosticsSource source = host.Create();

		PendingAnalysis analysis = host.StartHeldAnalysis(source, expectedRuns: 1);

		analysis.Complete(Result((FirstId, [s_firstError])));
		host.WaitForQueuedWork(2);

		source.Dispose();
		host.Dispatcher.PumpUntilIdle();

		Assert.IsEmpty(host.Updates);
	}

	[TestMethod]
	public void Dispose_StopsThePendingAnalysis_AndLaterChangesDoNothing()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		host.Analyzer.SetAnswer(Result((FirstId, [s_firstError])));
		source.HandleProjectChanged();

		Assert.AreEqual(1, host.Dispatcher.PendingTimerCount);

		source.Dispose();
		source.HandleProjectChanged();

		Assert.AreEqual(0, host.Dispatcher.PendingTimerCount);

		host.Dispatcher.Advance(s_settleDelay * 3);
		host.Settle();

		Assert.IsEmpty(host.Analyzer.Runs);
		Assert.IsEmpty(host.Updates);
	}

	[TestMethod]
	public void Dispose_CancelsTheRunningAnalysis_AndItsLateResultRaisesNothing()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		host.RunAnalysis(source, Result((FirstId, [s_firstError])), expectedUpdates: 1);

		PendingAnalysis analysis = host.StartHeldAnalysis(source, expectedRuns: 2);

		source.Dispose();

		Assert.IsTrue(analysis.Token.IsCancellationRequested);

		analysis.Complete(Result((FirstId, [s_firstChanged]), (SecondId, [s_secondError])));
		host.Settle();

		Assert.HasCount(1, host.Updates);
		Assert.IsEmpty(source.GetDiagnostics(FirstId));
		Assert.IsEmpty(host.Logger.Entries);

		// Disposing again is harmless.
		source.Dispose();
	}

	[TestMethod]
	public void SubscriberThatDisposesTheSource_IsTheLastOneCalled()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		int laterSubscriberCalls = 0;
		int disposingSubscriberCalls = 0;

		source.DiagnosticsUpdated += (_, _) =>
		{
			disposingSubscriberCalls++;
			source.Dispose();
		};

		source.DiagnosticsUpdated += (_, _) => laterSubscriberCalls++;

		host.Analyzer.SetAnswer(Result((FirstId, [s_firstError]), (SecondId, [s_secondError])));
		source.HandleProjectChanged();
		host.Dispatcher.Advance(s_settleDelay);
		host.Dispatcher.PumpUntil(() => disposingSubscriberCalls > 0);
		host.Settle();

		// The first subscriber ran for the first document and disposed the source, so nothing else was raised.
		Assert.AreEqual(1, disposingSubscriberCalls);
		Assert.AreEqual(0, laterSubscriberCalls);
		Assert.HasCount(1, host.Updates);
	}

	[TestMethod]
	public void FailingAnalysis_KeepsThePreviousSets_AndLogsOnce()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		host.RunAnalysis(source, Result((FirstId, [s_firstError]), (SecondId, [s_secondError])), expectedUpdates: 2);

		host.Analyzer.SetFailure(new InvalidOperationException("the compiler crashed"));
		source.HandleProjectChanged();
		host.Dispatcher.Advance(s_settleDelay);
		host.Dispatcher.PumpUntil(() => host.Logger.Entries.Count == 1);
		host.Settle();

		RecordingLogger.LogEntry entry = host.Logger.Entries.Single();

		Assert.AreEqual(LogLevel.Warning, entry.Level);
		Assert.AreEqual(1052, entry.EventId.Id);
		Assert.AreEqual("ProjectDiagnosticsAnalysisFailed", entry.EventId.Name);
		Assert.AreEqual("the compiler crashed", entry.ExceptionMessage);
		Assert.HasCount(2, host.Updates);
		Assert.AreSequenceEqual([s_firstError], [.. source.GetDiagnostics(FirstId)]);
		Assert.AreSequenceEqual([s_secondError], [.. source.GetDiagnostics(SecondId)]);

		// The next analysis that succeeds replaces the kept sets.
		host.Analyzer.SetFailure(null);
		host.RunAnalysis(source, Result((FirstId, [s_firstChanged]), (SecondId, [s_secondError])), expectedUpdates: 3);

		Assert.AreEqual(FirstId, host.Updates[2].DocumentId);
	}

	[TestMethod]
	public void AnalysisWithAnInvalidResult_IsAFailedAnalysis_ThatKeepsThePreviousSets()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		host.RunAnalysis(source, Result((FirstId, [s_firstError])), expectedUpdates: 1);

		IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>?[] invalidResults =
		[
			null,
			new Dictionary<string, IReadOnlyList<Diagnostic>> { [FirstId] = null! },
			new Dictionary<string, IReadOnlyList<Diagnostic>> { [FirstId] = [s_firstChanged, null!] },
		];

		int expectedLogs = 0;

		foreach (IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>? invalid in invalidResults)
		{
			host.Analyzer.SetAnswer(invalid);
			source.HandleProjectChanged();
			host.Dispatcher.Advance(s_settleDelay);
			host.Dispatcher.PumpUntil(() => host.Logger.Entries.Count == expectedLogs + 1);

			expectedLogs++;
		}

		host.Settle();

		Assert.HasCount(1, host.Updates);
		Assert.IsTrue(host.Logger.Entries.All(entry => entry.EventId.Id == 1052));
		Assert.AreSequenceEqual([s_firstError], [.. source.GetDiagnostics(FirstId)]);
	}

	[TestMethod]
	public void ThrowingSubscriber_IsContainedAndLogged_AndTheNextSubscriberIsStillCalled()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		int laterSubscriberCalls = 0;

		source.DiagnosticsUpdated += (_, _) => throw new InvalidOperationException("the subscriber failed");
		source.DiagnosticsUpdated += (_, _) => laterSubscriberCalls++;

		host.RunAnalysis(source, Result((FirstId, [s_firstError])), expectedUpdates: 1);

		Assert.AreEqual(1, laterSubscriberCalls);

		RecordingLogger.LogEntry entry = host.Logger.Entries.Single();

		Assert.AreEqual(LogLevel.Warning, entry.Level);
		Assert.AreEqual(1053, entry.EventId.Id);
		Assert.AreEqual("the subscriber failed", entry.ExceptionMessage);
	}

	[TestMethod]
	public void Hook_IsCalledOnTheDispatcherThread_OnTheTurnThatStartsTheRun_AndTheEventIsRaisedOnIt()
	{
		using var host = new Host(installSynchronizationContext: false);
		using ProjectDiagnosticsSource source = host.Create();

		host.Analyzer.SetAnswer(Result((FirstId, [s_firstError])));
		source.HandleProjectChanged();
		host.Dispatcher.Advance(s_settleDelay);

		// The timer turn called the hook, so the host captures its text before the dispatcher runs anything else.
		Assert.HasCount(1, host.Analyzer.Runs);
		Assert.AreEqual(host.Dispatcher.OwnerThreadId, host.Analyzer.Runs[0].ThreadId);

		host.Dispatcher.PumpUntil(() => host.Updates.Count == 1);

		Assert.AreEqual(host.Dispatcher.OwnerThreadId, host.UpdateThreadIds.Single());
	}

	[TestMethod]
	public void CanceledRunThatIgnoresItsToken_FinishesBeforeTheNextRunStarts_AndOnlyOneStartWaits()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		PendingAnalysis first = host.StartHeldAnalysis(source, expectedRuns: 1);

		// Two changes supersede the first run, and the settle delay passes after each, while the first call has not returned.
		source.HandleProjectChanged();
		host.Dispatcher.Advance(s_settleDelay);
		source.HandleProjectChanged();
		host.Dispatcher.Advance(s_settleDelay);
		host.Settle();

		Assert.IsTrue(first.Token.IsCancellationRequested);
		Assert.HasCount(1, host.Analyzer.Runs);

		// The first call returns, and exactly one waiting start begins, on a fresh turn.
		first.Complete(Result((FirstId, [s_firstError])));
		host.Dispatcher.PumpUntil(() => host.Analyzer.Runs.Count == 2);
		host.Settle();

		Assert.HasCount(2, host.Analyzer.Runs);
		Assert.IsFalse(host.Analyzer.Runs[1].Token.IsCancellationRequested);
		Assert.IsEmpty(host.Updates);

		host.Analyzer.Runs[1].Complete(Result((FirstId, [s_firstChanged])));
		host.Dispatcher.PumpUntil(() => host.Updates.Count == 1);

		Assert.AreEqual(1, host.Analyzer.MaxConcurrent);
		Assert.AreSequenceEqual([s_firstChanged], [.. host.Updates[0].Diagnostics]);
	}

	[TestMethod]
	public void ResultIdsEqualUnderTheComparer_AddUpUnderTheFirstSpelling()
	{
		using var host = new Host();

		using ProjectDiagnosticsSource source = host.Create(
			ProjectDiagnosticsSourceOptions.Default with
			{
				DocumentIdComparer = StringComparer.OrdinalIgnoreCase,
			}
		);

		// The result keys are ordinal, so the two spellings are two entries of the dictionary that name one document.
		var answer = new Dictionary<string, IReadOnlyList<Diagnostic>>(StringComparer.Ordinal)
		{
			["C:\\p\\Script.txt"] = [s_firstError],
			["C:\\p\\SCRIPT.TXT"] = [s_secondError],
			["c:\\p\\other.txt"] = [s_firstChanged],
		};

		host.RunAnalysis(source, answer, expectedUpdates: 2);

		Assert.HasCount(2, host.Updates);
		Assert.AreEqual("C:\\p\\Script.txt", host.Updates[0].DocumentId);
		Assert.AreSequenceEqual([s_firstError, s_secondError], [.. host.Updates[0].Diagnostics]);
		Assert.AreSequenceEqual([s_firstError, s_secondError], [.. source.GetDiagnostics("c:\\p\\script.txt")]);
	}

	[TestMethod]
	public void CancellationFromInsideTheHook_AndANullTask_AreFailedAnalyses_ThatKeepThePreviousSets()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		host.RunAnalysis(source, Result((FirstId, [s_firstError])), expectedUpdates: 1);

		// A timeout inside the host cancels its own work while the source's token is not canceled.
		host.Analyzer.SetFailure(new TaskCanceledException("a timeout inside the host"));
		source.HandleProjectChanged();
		host.Dispatcher.Advance(s_settleDelay);
		host.Dispatcher.PumpUntil(() => host.Logger.Entries.Count == 1);

		host.Analyzer.SetFailure(null);
		host.Analyzer.SetReturnsNoTask();
		source.HandleProjectChanged();
		host.Dispatcher.Advance(s_settleDelay);
		host.Dispatcher.PumpUntil(() => host.Logger.Entries.Count == 2);
		host.Settle();

		Assert.IsTrue(host.Logger.Entries.All(entry => entry.EventId.Id == 1052 && entry.Level == LogLevel.Warning));
		Assert.HasCount(1, host.Updates);
		Assert.AreSequenceEqual([s_firstError], [.. source.GetDiagnostics(FirstId)]);
	}

	[TestMethod]
	public void CancellationThatTheSourceAsked_IsNotAFailure()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		PendingAnalysis analysis = host.StartHeldAnalysis(source, expectedRuns: 1);

		source.HandleProjectChanged();

		Assert.IsTrue(analysis.Token.IsCancellationRequested);

		analysis.Fail(new TaskCanceledException("the host observed the token"));
		host.Settle();

		Assert.IsEmpty(host.Logger.Entries);
	}

	[TestMethod]
	public void ChangeReportedByASubscriber_TakesEffectFromTheNextAnalysis_AndTheRestOfTheRaiseHappens()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		int notifications = 0;

		source.DiagnosticsUpdated += (_, _) =>
		{
			if (notifications++ == 0)
				source.HandleProjectChanged();
		};

		host.Analyzer.SetAnswer(Result((FirstId, [s_firstError]), (SecondId, [s_secondError])));
		source.HandleProjectChanged();
		host.Dispatcher.Advance(s_settleDelay);
		host.Dispatcher.PumpUntil(() => host.Updates.Count == 2);

		// The subscriber's change did not stop the second document's set, and it armed the next analysis.
		Assert.AreEqual(2, notifications);
		Assert.AreEqual(1, host.Dispatcher.PendingTimerCount);

		host.Dispatcher.Advance(s_settleDelay);
		host.Dispatcher.PumpUntil(() => host.Analyzer.Runs.Count == 2);
	}

	[TestMethod]
	public void Dispose_FromAnotherThread_Throws_AndLeavesTheSourceRunning()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		Exception? failure = null;

		var thread = new Thread(() =>
		{
			try
			{
				source.Dispose();
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.Start();
		thread.Join();

		Assert.IsInstanceOfType<InvalidOperationException>(failure);

		// The failed call disposed nothing, so the source still analyzes.
		host.RunAnalysis(source, Result((FirstId, [s_firstError])), expectedUpdates: 1);
	}

	[TestMethod]
	public void Controller_ShowsTheSetsOfTheSource_AndClearsAVanishedOne()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource source = host.Create();

		List<IReadOnlyList<Diagnostic>> shown = [];

		using var controller = new DiagnosticsController(
			documentId: FirstId,
			provider: null,
			source,
			hooks: new DiagnosticsControllerHooks { GetText = static () => "text", ShowDiagnostics = shown.Add },
			dispatcher: host.Dispatcher
		);

		host.Dispatcher.PumpUntilIdle();

		host.RunAnalysis(source, Result((FirstId, [s_firstError]), (SecondId, [s_secondError])), expectedUpdates: 2);
		host.Dispatcher.PumpUntil(() => controller.Current.Count == 1);

		Assert.AreSequenceEqual([s_firstError], [.. controller.Current]);

		host.RunAnalysis(source, Result((SecondId, [s_secondError])), expectedUpdates: 3);
		host.Dispatcher.PumpUntil(() => controller.Current.Count == 0);
	}

	[TestMethod]
	public void GetDiagnostics_MatchesTheDocumentIdThroughTheComparer()
	{
		using var host = new Host();
		using ProjectDiagnosticsSource ordinal = host.Create();

		using ProjectDiagnosticsSource ignoringCase = host.Create(
			ProjectDiagnosticsSourceOptions.Default with
			{
				DocumentIdComparer = StringComparer.OrdinalIgnoreCase,
			}
		);

		host.Analyzer.SetAnswer(Result((FirstId, [s_firstError])));

		ordinal.HandleProjectChanged();
		ignoringCase.HandleProjectChanged();

		host.Dispatcher.Advance(s_settleDelay);
		host.Dispatcher.PumpUntil(() => ordinal.GetDiagnostics(FirstId).Count == 1 && ignoringCase.GetDiagnostics(FirstId).Count == 1);

		Assert.IsEmpty(ordinal.GetDiagnostics(FirstId.ToUpperInvariant()));
		Assert.HasCount(1, ignoringCase.GetDiagnostics(FirstId.ToUpperInvariant()));
		Assert.ThrowsExactly<ArgumentNullException>(() => ordinal.GetDiagnostics(null!));
	}

	[TestMethod]
	public void Constructor_AndHandleProjectChanged_RejectInvalidArgumentsAndOtherThreads()
	{
		using var host = new Host();

		Assert.ThrowsExactly<ArgumentNullException>(() => new ProjectDiagnosticsSource(default, host.Dispatcher));

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new ProjectDiagnosticsSource(new ProjectDiagnosticsSourceHooks(host.Analyzer.AnalyzeAsync), null!)
		);

		using ProjectDiagnosticsSource source = host.Create();

		Exception? constructionFailure = null;
		Exception? notifyFailure = null;

		var thread = new Thread(() =>
		{
			try
			{
				new ProjectDiagnosticsSource(new ProjectDiagnosticsSourceHooks(host.Analyzer.AnalyzeAsync), host.Dispatcher).Dispose();
			}
			catch (Exception exception)
			{
				constructionFailure = exception;
			}

			try
			{
				source.HandleProjectChanged();
			}
			catch (Exception exception)
			{
				notifyFailure = exception;
			}
		});

		thread.Start();
		thread.Join();

		Assert.IsInstanceOfType<InvalidOperationException>(constructionFailure);
		Assert.IsInstanceOfType<InvalidOperationException>(notifyFailure);
	}

	[TestMethod]
	public void Options_DefaultSettleDelayAndValidation()
	{
		Assert.AreEqual(TimeSpan.FromMilliseconds(500.0), ProjectDiagnosticsSourceOptions.Default.SettleDelay);
		Assert.IsNull(ProjectDiagnosticsSourceOptions.Default.DocumentIdComparer);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			ProjectDiagnosticsSourceOptions.Default with
			{
				SettleDelay = TimeSpan.FromMilliseconds(-1.0),
			}
		);
	}

	private static Diagnostic Error(string message, int offset) => new(DiagnosticSeverity.Error, message, new TextRange(offset, 2));

	private static IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>> Result(params (string DocumentId, Diagnostic[] Diagnostics)[] documents) =>
		documents.ToDictionary(document => document.DocumentId, document => (IReadOnlyList<Diagnostic>)document.Diagnostics);

	/// <summary>An analysis the source started, which a test completes when the analyzer holds its answers.</summary>
	private sealed class PendingAnalysis(CancellationToken token, int threadId, Action onFinished)
	{
		private readonly TaskCompletionSource<IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>> _answer = new(
			TaskCreationOptions.RunContinuationsAsynchronously
		);

		private int _isFinished;

		internal CancellationToken Token { get; } = token;

		internal int ThreadId { get; } = threadId;

		internal Task<IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>> Answer => _answer.Task;

		// The analysis ignores the token on purpose, so the source's own checks decide whether a late answer is raised.
		internal void Complete(IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>> result)
		{
			Finish();
			_answer.TrySetResult(result);
		}

		internal void Fail(Exception exception)
		{
			Finish();
			_answer.TrySetException(exception);
		}

		// The analyzer counts the calls that have not returned, so the count drops before the source can see the end of the call.
		private void Finish()
		{
			if (Interlocked.Exchange(ref _isFinished, 1) == 0)
				onFinished();
		}
	}

	private sealed class ScriptedProjectAnalyzer
	{
		private readonly object _sync = new();
		private readonly List<PendingAnalysis> _runs = [];

		private IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>? _answer = new Dictionary<string, IReadOnlyList<Diagnostic>>();
		private Exception? _failure;

		private bool _hold;
		private bool _returnsNoTask;

		private int _running;
		private int _maxConcurrent;

		internal IReadOnlyList<PendingAnalysis> Runs
		{
			get
			{
				lock (_sync)
					return [.. _runs];
			}
		}

		// The most calls that were running at once.
		internal int MaxConcurrent
		{
			get
			{
				lock (_sync)
					return _maxConcurrent;
			}
		}

		// The answer is null for a test of an analysis that breaks its contract.
		internal void SetAnswer(IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>? answer)
		{
			lock (_sync)
				_answer = answer;
		}

		// A hook that breaks its contract by returning no task.
		internal void SetReturnsNoTask()
		{
			lock (_sync)
				_returnsNoTask = true;
		}

		internal void SetFailure(Exception? failure)
		{
			lock (_sync)
				_failure = failure;
		}

		// A held analysis completes only when the test completes it.
		internal void SetHold()
		{
			lock (_sync)
				_hold = true;
		}

		internal Task<IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>> AnalyzeAsync(CancellationToken cancellationToken)
		{
			var run = new PendingAnalysis(
				cancellationToken,
				Environment.CurrentManagedThreadId,

				() =>
				{
					lock (_sync)
						_running--;
				}
			);

			lock (_sync)
			{
				_runs.Add(run);
				_maxConcurrent = Math.Max(_maxConcurrent, ++_running);

				if (_returnsNoTask)
					return null!;

				if (!_hold)
				{
					if (_failure is { } failure)
						run.Fail(failure);
					else
						run.Complete(_answer!);
				}
			}

			return run.Answer;
		}
	}

	/// <summary>Owns the dispatcher, the analyzer and the recorded events one source test drives.</summary>
	private sealed class Host(bool installSynchronizationContext = true) : IDisposable
	{
		private readonly object _sync = new();
		private readonly List<DiagnosticsUpdatedEventArgs> _updates = [];
		private readonly List<int> _updateThreadIds = [];

		internal FakePumpedDispatcher Dispatcher { get; } = new(installSynchronizationContext);

		internal ScriptedProjectAnalyzer Analyzer { get; } = new();

		internal RecordingLogger Logger { get; } = new();

		internal IReadOnlyList<DiagnosticsUpdatedEventArgs> Updates
		{
			get
			{
				lock (_sync)
					return [.. _updates];
			}
		}

		internal IReadOnlyList<int> UpdateThreadIds
		{
			get
			{
				lock (_sync)
					return [.. _updateThreadIds];
			}
		}

		internal ProjectDiagnosticsSource Create(ProjectDiagnosticsSourceOptions? options = null)
		{
			var source = new ProjectDiagnosticsSource(
				hooks: new ProjectDiagnosticsSourceHooks(Analyzer.AnalyzeAsync),
				Dispatcher,

				options: (options ?? ProjectDiagnosticsSourceOptions.Default) with
				{
					SettleDelay = s_settleDelay,
				},

				Logger
			);

			source.DiagnosticsUpdated += (_, e) =>
			{
				lock (_sync)
				{
					_updates.Add(e);
					_updateThreadIds.Add(Environment.CurrentManagedThreadId);
				}
			};

			return source;
		}

		// Reports a change, lets the settle delay pass, and waits until the answer of the analysis has been raised.
		internal void RunAnalysis(ProjectDiagnosticsSource source, IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>> answer, int expectedUpdates)
		{
			int runs = Analyzer.Runs.Count;

			Analyzer.SetAnswer(answer);
			source.HandleProjectChanged();
			Dispatcher.Advance(s_settleDelay);

			Dispatcher.PumpUntil(() => Analyzer.Runs.Count == runs + 1);
			Dispatcher.PumpUntil(() => Updates.Count >= expectedUpdates);
		}

		// Starts an analysis that the analyzer holds until the test completes it.
		internal PendingAnalysis StartHeldAnalysis(ProjectDiagnosticsSource source, int expectedRuns, bool notify = true)
		{
			Analyzer.SetHold();

			if (notify)
				source.HandleProjectChanged();

			Dispatcher.Advance(s_settleDelay);
			Dispatcher.PumpUntil(() => Analyzer.Runs.Count == expectedRuns);

			return Analyzer.Runs[^1];
		}

		// Waits for work that another thread queued, without running it.
		internal void WaitForQueuedWork(int count) =>
			Assert.IsTrue(SpinWait.SpinUntil(() => Dispatcher.QueuedCount >= count, TimeSpan.FromSeconds(5.0)), "The result was never posted.");

		// Runs the dispatcher for a short wall-clock window, so a result that should be dropped has had every chance to be raised.
		internal void Settle()
		{
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();

			while (stopwatch.Elapsed < TimeSpan.FromMilliseconds(100.0))
			{
				Dispatcher.PumpUntilIdle();
				Thread.Sleep(5);
			}

			Dispatcher.PumpUntilIdle();
		}

		public void Dispose()
		{
			Logger.Dispose();
			Dispatcher.Dispose();
		}
	}
}
