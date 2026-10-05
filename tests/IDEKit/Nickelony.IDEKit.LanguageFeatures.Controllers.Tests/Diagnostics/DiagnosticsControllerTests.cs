using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class DiagnosticsControllerTests
{
	private const string DocumentId = "diagnostics-test.lua";

	private static readonly TimeSpan s_settleDelay = TimeSpan.FromMilliseconds(300.0);

	private static readonly Diagnostic s_pulled = new(DiagnosticSeverity.Error, "pulled", new TextRange(0, 2));
	private static readonly Diagnostic s_pushed = new(DiagnosticSeverity.Warning, "pushed", new TextRange(3, 2));

	[TestMethod]
	public void Creation_WithAProvider_PullsOnceOnTheNextDispatcherTurn()
	{
		using var host = new Host();
		using DiagnosticsController controller = host.Create();

		Assert.IsEmpty(host.Provider.Requests);

		host.Provider.Answer = [s_pulled];
		host.Dispatcher.PumpUntil(() => controller.Current.Count == 1);

		Assert.HasCount(1, host.Provider.Requests);
		Assert.AreEqual(new DiagnosticsRequest(DocumentId, "one two"), host.Provider.Requests[0].Request);
		Assert.AreSame(host.Shown[^1], controller.Current);
	}

	[TestMethod]
	public void HandleTextChanged_RepeatedEdits_PullOnceAfterTheTextSettles()
	{
		using var host = new Host();
		using DiagnosticsController controller = host.Create();

		host.Dispatcher.PumpUntilIdle();
		Assert.HasCount(1, host.Provider.Requests);

		// Each edit restarts the delay, so five keystrokes 100 milliseconds apart pull nothing until the last one settles.
		for (int keystroke = 0; keystroke < 5; keystroke++)
		{
			host.Text += "x";
			controller.HandleTextChanged();
			host.Dispatcher.Advance(TimeSpan.FromMilliseconds(100.0));
		}

		Assert.HasCount(1, host.Provider.Requests);

		host.Dispatcher.Advance(s_settleDelay);

		Assert.HasCount(2, host.Provider.Requests);
		Assert.AreEqual("one twoxxxxx", host.Provider.Requests[1].Request.DocumentText);
	}

	[TestMethod]
	public void PullAnsweringAfterAnEdit_IsDiscarded_AndItsTokenIsCanceled()
	{
		using var host = new Host();
		using DiagnosticsController controller = host.Create();

		host.Dispatcher.PumpUntilIdle();

		PendingPull pull = host.StartHeldPull(controller);

		// The edit lands while the pull is in flight: the answer describes text that is gone.
		host.Text = "changed";
		controller.HandleTextChanged();

		Assert.IsTrue(pull.Token.IsCancellationRequested);

		pull.Complete([s_pulled]);
		host.Settle();

		Assert.IsEmpty(controller.Current);
	}

	[TestMethod]
	public void PullAnsweringAfterAPush_IsDiscarded()
	{
		using var host = new Host();
		using DiagnosticsController controller = host.Create(withSource: true);

		host.Dispatcher.PumpUntilIdle();

		PendingPull pull = host.StartHeldPull(controller);

		host.Source.Raise(DocumentId, [s_pushed]);
		host.Dispatcher.PumpUntilIdle();

		Assert.AreSequenceEqual([s_pushed], [.. controller.Current]);

		// The push is the producer's newer word, so the older pull cannot replace it.
		pull.Complete([s_pulled]);
		host.Settle();

		Assert.AreSequenceEqual([s_pushed], [.. controller.Current]);
	}

	[TestMethod]
	public void PushedSet_ReplacesAPulledOne_AndAPullStartedAfterThePushReplacesIt()
	{
		using var host = new Host();
		host.Provider.Answer = [s_pulled];

		using DiagnosticsController controller = host.Create(withSource: true);

		host.Dispatcher.PumpUntil(() => controller.Current.Count == 1 && controller.Current[0] == s_pulled);

		host.Source.Raise(DocumentId, [s_pushed]);
		host.Dispatcher.PumpUntilIdle();

		Assert.AreSequenceEqual([s_pushed], [.. controller.Current]);

		controller.HandleTextChanged();
		host.Dispatcher.Advance(s_settleDelay);
		host.Dispatcher.PumpUntil(() => controller.Current.Count == 1 && controller.Current[0] == s_pulled);
	}

	[TestMethod]
	public void PushComputedForOlderText_IsShownAtItsOffsets_WithoutMappingThroughTheEditsSince()
	{
		using var host = new Host();
		using DiagnosticsController controller = host.Create(withProvider: false, withSource: true);

		host.Dispatcher.PumpUntilIdle();

		// An edit lands before the push arrives, and the push still names the offsets of the text its producer read.
		host.Text = "inserted one two";
		controller.HandleTextChanged();
		host.Source.Raise(DocumentId, [s_pushed]);
		host.Dispatcher.PumpUntilIdle();

		Assert.AreSequenceEqual([s_pushed], [.. controller.Current]);
		Assert.IsTrue(controller.TryGetShownRange(0, out TextRange range));
		Assert.AreEqual(s_pushed.Range, range);
		Assert.AreEqual(s_pushed, controller.FindAt(s_pushed.Range.Offset));
	}

	[TestMethod]
	public void PushForAnotherDocument_IsIgnored()
	{
		using var host = new Host();
		using DiagnosticsController controller = host.Create(withProvider: false, withSource: true);

		host.Source.Raise("other.lua", [s_pushed]);
		host.Dispatcher.PumpUntilIdle();

		Assert.IsEmpty(controller.Current);
	}

	[TestMethod]
	public void Creation_WithASource_ShowsTheDiagnosticsItAlreadyHolds()
	{
		using var host = new Host();
		host.Source.Current[DocumentId] = [s_pushed];

		using DiagnosticsController controller = host.Create(withProvider: false, withSource: true);

		host.Dispatcher.PumpUntilIdle();

		Assert.AreSequenceEqual([s_pushed], [.. controller.Current]);
	}

	[TestMethod]
	public void FindAt_WithoutTheHook_AnswersTheRangesOfTheCurrentSet()
	{
		using var host = new Host();
		host.Provider.Answer = [s_pulled, s_pushed];

		using DiagnosticsController controller = host.Create();

		host.Dispatcher.PumpUntil(() => controller.Current.Count == 2);

		Assert.AreSame(s_pulled, controller.FindAt(1));
		Assert.AreSame(s_pushed, controller.FindAt(3));
		Assert.IsNull(controller.FindAt(2));
	}

	[TestMethod]
	public void FindAt_WithTheHook_AnswersTheShownIndexItReports()
	{
		using var host = new Host();
		host.Provider.Answer = [s_pulled, s_pushed];

		// The editor moved its decorations: offset 10 is where the second diagnostic's text sits now.
		host.FindShownIndexAt = offset =>
			offset switch
			{
				10 => 1,
				11 => 7,
				_ => null,
			};

		using DiagnosticsController controller = host.Create();

		host.Dispatcher.PumpUntil(() => controller.Current.Count == 2);

		Assert.AreSame(s_pushed, controller.FindAt(10));
		Assert.IsNull(controller.FindAt(1));
		Assert.IsNull(controller.FindAt(11), "An index outside the shown set finds nothing.");
	}

	[TestMethod]
	public void Dispose_CancelsThePullInFlight_AndDropsItsAnswer()
	{
		using var host = new Host();
		DiagnosticsController controller = host.Create();

		host.Dispatcher.PumpUntilIdle();
		int shownCount = host.Shown.Count;

		PendingPull pull = host.StartHeldPull(controller);

		Assert.IsFalse(pull.Token.IsCancellationRequested);

		controller.Dispose();

		Assert.IsTrue(pull.Token.IsCancellationRequested);

		pull.Complete([s_pulled]);
		host.Settle();

		Assert.HasCount(shownCount, host.Shown);
		Assert.IsEmpty(controller.Current);
	}

	[TestMethod]
	public void Dispose_StopsTheScheduledPullAndTheSubscription_AndDetachesOnce()
	{
		using var host = new Host();
		DiagnosticsController controller = host.Create(withSource: true);

		host.Dispatcher.PumpUntilIdle();
		int shownCount = host.Shown.Count;

		// The edit arms the settle timer, which disposal cancels.
		controller.HandleTextChanged();

		controller.Dispose();
		controller.Dispose();

		Assert.AreEqual(0, host.Source.HandlerCount);
		Assert.AreEqual(1, host.DetachCount);

		host.Source.Raise(DocumentId, [s_pushed]);
		controller.HandleTextChanged();
		host.Dispatcher.Advance(s_settleDelay + s_settleDelay);

		Assert.HasCount(1, host.Provider.Requests);
		Assert.HasCount(shownCount, host.Shown);
		Assert.IsNull(controller.FindAt(0));
	}

	[TestMethod]
	public void HandleDocumentReplaced_ClearsTheShownSet_AndStopsPullsAndPushes()
	{
		using var host = new Host();
		host.Provider.Answer = [s_pulled];

		using DiagnosticsController controller = host.Create(withSource: true);

		host.Dispatcher.PumpUntil(() => controller.Current.Count == 1);

		// The edit arms the settle timer, which the swap cancels.
		controller.HandleTextChanged();
		controller.HandleDocumentReplaced();

		Assert.IsEmpty(controller.Current);
		Assert.IsEmpty(host.Shown[^1]);
		Assert.AreEqual(0, host.Source.HandlerCount);

		host.Source.Raise(DocumentId, [s_pushed]);
		controller.HandleTextChanged();
		host.Dispatcher.Advance(s_settleDelay + s_settleDelay);

		Assert.HasCount(1, host.Provider.Requests);
		Assert.IsEmpty(controller.Current);
		Assert.AreEqual(0, host.DetachCount);
	}

	[TestMethod]
	public void HandleDocumentReplaced_CancelsThePullInFlight_AndDropsItsAnswer()
	{
		using var host = new Host();
		using DiagnosticsController controller = host.Create();

		host.Dispatcher.PumpUntilIdle();

		PendingPull pull = host.StartHeldPull(controller);

		controller.HandleDocumentReplaced();

		Assert.IsTrue(pull.Token.IsCancellationRequested);

		pull.Complete([s_pulled]);
		host.Settle();

		Assert.IsEmpty(controller.Current);
	}

	[TestMethod]
	public void PullFailure_KeepsTheShownSet_AndIsLogged()
	{
		using var host = new Host();
		host.Provider.Answer = [s_pulled];

		using DiagnosticsController controller = host.Create();

		host.Dispatcher.PumpUntil(() => controller.Current.Count == 1);

		host.Provider.Failure = new InvalidOperationException("The analyzer failed.");
		controller.HandleTextChanged();
		host.Dispatcher.Advance(s_settleDelay);
		host.Dispatcher.PumpUntil(() => host.Logger.Entries.Any(entry => entry.EventId.Id == 1050));

		Assert.AreSequenceEqual([s_pulled], [.. controller.Current]);
	}

	[TestMethod]
	public void ThrowingShowHook_IsContainedAndLogged_AndTheSetStillBecomesCurrent()
	{
		using var host = new Host();
		host.Provider.Answer = [s_pulled];
		host.ShowFailure = new InvalidOperationException("The editor failed.");

		using DiagnosticsController controller = host.Create();

		host.Dispatcher.PumpUntil(() => controller.Current.Count == 1);

		Assert.Contains(entry => entry.EventId.Id == 1051, host.Logger.Entries);
	}

	[TestMethod]
	public void ProviderAnswerWithNullEntries_ShowsTheOthers()
	{
		using var host = new Host();
		host.Provider.Answer = [s_pulled, null!, s_pushed];

		using DiagnosticsController controller = host.Create();

		host.Dispatcher.PumpUntil(() => controller.Current.Count == 2);

		Assert.AreSequenceEqual([s_pulled, s_pushed], [.. controller.Current]);
	}

	[TestMethod]
	public void Changed_IsRaisedForEachShownSet()
	{
		using var host = new Host();
		host.Provider.Answer = [s_pulled];

		using DiagnosticsController controller = host.Create(withSource: true);

		int changedCount = 0;
		controller.Changed += (_, _) => changedCount++;

		host.Dispatcher.PumpUntil(() => controller.Current.Count == 1);

		// The source's first delivery and the first pull each showed a set.
		Assert.AreEqual(2, changedCount);

		host.Source.Raise(DocumentId, [s_pushed]);
		host.Dispatcher.PumpUntilIdle();

		Assert.AreEqual(3, changedCount);
	}

	[TestMethod]
	public void Dispose_EmptiesTheCurrentSet()
	{
		using var host = new Host();
		host.Provider.Answer = [s_pulled];

		DiagnosticsController controller = host.Create();

		host.Dispatcher.PumpUntil(() => controller.Current.Count == 1);

		controller.Dispose();

		Assert.IsEmpty(controller.Current);
		Assert.IsFalse(controller.TryGetShownRange(0, out _));
	}

	[TestMethod]
	public void TryGetShownRange_WithoutTheHook_AnswersTheProducersRange()
	{
		using var host = new Host();
		host.Provider.Answer = [s_pulled, s_pushed];

		using DiagnosticsController controller = host.Create();

		host.Dispatcher.PumpUntil(() => controller.Current.Count == 2);

		Assert.IsTrue(controller.TryGetShownRange(1, out TextRange range));
		Assert.AreEqual(s_pushed.Range, range);
		Assert.IsFalse(controller.TryGetShownRange(2, out _));
		Assert.IsFalse(controller.TryGetShownRange(-1, out _));
	}

	[TestMethod]
	public void FindAtAndTryGetShownRange_WithTheHooks_AnswerTheShownRange()
	{
		using var host = new Host();
		host.Provider.Answer = [s_pulled, s_pushed];

		// An edit moved the second diagnostic to offset 10 and deleted the text of the first one.
		host.FindShownIndexAt = offset => offset is >= 10 and < 12 ? 1 : null;
		host.GetShownRange = index => index == 1 ? new TextRange(10, 2) : null;

		using DiagnosticsController controller = host.Create();

		host.Dispatcher.PumpUntil(() => controller.Current.Count == 2);

		Assert.IsTrue(controller.TryGetShownRange(1, out TextRange range));
		Assert.AreEqual(new TextRange(10, 2), range);
		Assert.IsFalse(controller.TryGetShownRange(0, out _), "A diagnostic whose text was deleted is not shown.");

		// The answer is the diagnostic as it is shown now: the producer's entry with the moved range.
		Diagnostic? found = controller.FindAt(11);

		Assert.IsNotNull(found);
		Assert.AreEqual(new TextRange(10, 2), found.Range);
		Assert.AreEqual(s_pushed.Message, found.Message);
		Assert.AreEqual(s_pushed.Severity, found.Severity);
		Assert.AreEqual(s_pushed.Range, controller.Current[1].Range, "Current keeps the producer's range.");
	}

	[TestMethod]
	public void Constructor_InvalidArguments_Throw()
	{
		using var dispatcher = new FakePumpedDispatcher(installSynchronizationContext: false);
		var hooks = new DiagnosticsControllerHooks { GetText = static () => string.Empty, ShowDiagnostics = static _ => { } };
		var source = new RecordingDiagnosticsSource();

		Assert.ThrowsExactly<ArgumentException>(() => new DiagnosticsController(documentId: " ", provider: null, source, hooks, dispatcher));
		Assert.ThrowsExactly<ArgumentException>(() => new DiagnosticsController(DocumentId, provider: null, source: null, hooks, dispatcher));
		Assert.ThrowsExactly<ArgumentNullException>(() => new DiagnosticsController(DocumentId, provider: null, source, hooks: null!, dispatcher));
		Assert.ThrowsExactly<ArgumentNullException>(() => new DiagnosticsController(DocumentId, provider: null, source, hooks, dispatcher: null!));

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new DiagnosticsController(
				DocumentId,
				provider: null,
				source,
				hooks: new DiagnosticsControllerHooks { GetText = null!, ShowDiagnostics = static _ => { } },
				dispatcher
			)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new DiagnosticsController(
				DocumentId,
				provider: null,
				source,
				hooks: new DiagnosticsControllerHooks { GetText = static () => "", ShowDiagnostics = null! },
				dispatcher
			)
		);

		Exception? offThreadFailure = null;

		var thread = new Thread(() =>
		{
			try
			{
				new DiagnosticsController(DocumentId, provider: null, source, hooks, dispatcher).Dispose();
			}
			catch (Exception exception)
			{
				offThreadFailure = exception;
			}
		});

		thread.Start();
		thread.Join();

		Assert.IsInstanceOfType<InvalidOperationException>(offThreadFailure);
	}

	[TestMethod]
	public void Options_DefaultSettleDelayAndValidation()
	{
		Assert.AreEqual(TimeSpan.FromMilliseconds(300.0), DiagnosticsControllerOptions.Default.SettleDelay);
		Assert.IsNull(DiagnosticsControllerOptions.Default.DocumentIdComparer);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			DiagnosticsControllerOptions.Default with
			{
				SettleDelay = TimeSpan.FromMilliseconds(-1.0),
			}
		);
	}

	/// <summary>A pull the provider received, which a test completes when the provider holds its answers.</summary>
	private sealed class PendingPull(DiagnosticsRequest request, CancellationToken token)
	{
		private readonly TaskCompletionSource<IReadOnlyList<Diagnostic>> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal DiagnosticsRequest Request { get; } = request;

		internal CancellationToken Token { get; } = token;

		internal Task<IReadOnlyList<Diagnostic>> Answer => _answer.Task;

		// The provider ignores the token on purpose, so the controller's own checks decide whether a late answer is shown.
		internal void Complete(IReadOnlyList<Diagnostic> diagnostics) => _answer.TrySetResult(diagnostics);

		internal void Fail(Exception exception) => _answer.TrySetException(exception);
	}

	private sealed class ScriptedDiagnosticsProvider : IDiagnosticsProvider
	{
		internal List<PendingPull> Requests { get; } = [];

		internal IReadOnlyList<Diagnostic> Answer { get; set; } = [];

		internal Exception? Failure { get; set; }

		internal bool Hold { get; set; }

		public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(DiagnosticsRequest request, CancellationToken cancellationToken = default)
		{
			var pull = new PendingPull(request, cancellationToken);
			Requests.Add(pull);

			if (!Hold)
			{
				if (Failure is { } failure)
					pull.Fail(failure);
				else
					pull.Complete(Answer);
			}

			return pull.Answer;
		}
	}

	private sealed class RecordingDiagnosticsSource : IDiagnosticsSource
	{
		public event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated;

		internal Dictionary<string, IReadOnlyList<Diagnostic>> Current { get; } = [];

		internal int HandlerCount => DiagnosticsUpdated?.GetInvocationList().Length ?? 0;

		public IReadOnlyList<Diagnostic> GetDiagnostics(string documentId) =>
			Current.TryGetValue(documentId, out IReadOnlyList<Diagnostic>? diagnostics) ? diagnostics : [];

		internal void Raise(string documentId, IReadOnlyList<Diagnostic> diagnostics) =>
			DiagnosticsUpdated?.Invoke(this, new DiagnosticsUpdatedEventArgs(documentId, diagnostics));
	}

	/// <summary>Owns the dispatcher, the provider, the source and the editor hooks one controller test drives.</summary>
	private sealed class Host : IDisposable
	{
		internal FakePumpedDispatcher Dispatcher { get; } = new(installSynchronizationContext: true);

		internal ScriptedDiagnosticsProvider Provider { get; } = new();

		internal RecordingDiagnosticsSource Source { get; } = new();

		internal RecordingLogger Logger { get; } = new();

		internal string Text { get; set; } = "one two";

		internal List<IReadOnlyList<Diagnostic>> Shown { get; } = [];

		internal Exception? ShowFailure { get; set; }

		internal Func<int, int?>? FindShownIndexAt { get; set; }

		internal Func<int, TextRange?>? GetShownRange { get; set; }

		internal int DetachCount { get; private set; }

		internal DiagnosticsController Create(bool withProvider = true, bool withSource = false)
		{
			var hooks = new DiagnosticsControllerHooks
			{
				GetText = () => Text,

				ShowDiagnostics = diagnostics =>
				{
					Shown.Add(diagnostics);

					if (ShowFailure is { } failure)
						throw failure;
				},

				FindShownIndexAt = FindShownIndexAt,
				GetShownRange = GetShownRange,
				Detach = () => DetachCount++,
			};

			return new DiagnosticsController(
				DocumentId,
				provider: withProvider ? Provider : null,
				source: withSource ? Source : null,
				hooks,
				Dispatcher,

				options: DiagnosticsControllerOptions.Default with
				{
					SettleDelay = s_settleDelay,
				},

				Logger
			);
		}

		// An edit starts the pull once the text settles, and the provider holds its answer until the test completes it.
		internal PendingPull StartHeldPull(DiagnosticsController controller)
		{
			int requestCount = Provider.Requests.Count;

			Provider.Hold = true;
			controller.HandleTextChanged();
			Dispatcher.Advance(s_settleDelay);

			Assert.HasCount(requestCount + 1, Provider.Requests);

			return Provider.Requests[^1];
		}

		// Runs the dispatcher for a short wall-clock window, so an answer that should be dropped has had every chance to be shown.
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
