using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Builds a completion request gate over a manual dispatcher and records what the gate reported, so gate tests share one host
/// instead of repeating the delegates. The host owns the dispatcher the gate runs on.
/// </summary>
/// <remarks>
/// The delegate properties are mutable on purpose: a test starts from the defaults and swaps in the behavior it needs before it creates the gate,
/// and the recorder lists observe what the gate applied and reported. The host disposes its dispatcher, which restores the thread's
/// synchronization context.
/// </remarks>
internal sealed class CompletionRequestGateTestHost : IDisposable
{
	/// <summary>The debounce delay of the gate under test; a test advances the virtual clock to or past it, or stops short of it.</summary>
	internal static readonly TimeSpan s_debounceDelay = TimeSpan.FromMilliseconds(10.0);

	/// <param name="installSynchronizationContext">
	/// Whether the dispatcher is the synchronization context of the test thread, as the dispatcher of a running editor is. A test that needs
	/// a continuation to resume on the thread pool passes <see langword="false"/>.
	/// </param>
	internal CompletionRequestGateTestHost(bool installSynchronizationContext = true) =>
		Dispatcher = new FakePumpedDispatcher(installSynchronizationContext);

	/// <summary>The dispatcher the gate runs on; the test pumps it and moves its clock.</summary>
	internal FakePumpedDispatcher Dispatcher { get; }

	/// <summary>The logger the gate reports request failures to.</summary>
	internal RecordingLogger Logger { get; } = new();

	/// <summary>The decisions the gate handed to the apply delegate, in order.</summary>
	internal List<CompletionSessionDirective> Applied { get; } = [];

	/// <summary>The threads the apply delegate ran on, in order.</summary>
	internal List<int> ApplyThreadIds { get; } = [];

	/// <summary>The values the gate reported for the scheduled-request flag, in order.</summary>
	internal List<bool> ScheduledFlags { get; } = [];

	/// <summary>Holds the answer of the apply delegate, which defaults to reporting an opened window.</summary>
	internal Func<CompletionSessionDirective, bool> ApplyDirective { get; set; } = static _ => true;

	/// <summary>Holds the scheduled callback the gate is created with, or <see langword="null"/> for a gate that schedules nothing.</summary>
	internal Func<CancellationToken, Task>? ScheduledRequestAsync { get; set; }

	/// <summary>
	/// Holds the context-version provider the gate is created with, or <see langword="null"/> for a host whose version never changes.
	/// </summary>
	internal Func<int>? ContextVersionProvider { get; set; }

	/// <summary>Creates a decision that opens a window with one item, the shape most tests apply.</summary>
	/// <param name="label">The label of the item.</param>
	/// <returns>The decision.</returns>
	internal static CompletionSessionDirective OpenDecision(string label = "sample") =>
		CompletionSessionDirective.Open([new CompletionItem(label)], TextRange.FromBounds(0, 5));

	/// <summary>Creates a gate wired to this host's recorders, with the debounce delay <see cref="s_debounceDelay"/>.</summary>
	/// <returns>The created gate.</returns>
	internal CompletionRequestGate CreateGate()
	{
		return new CompletionRequestGate(
			Dispatcher,

			applyDecision: decision =>
			{
				Applied.Add(decision);
				ApplyThreadIds.Add(Environment.CurrentManagedThreadId);

				return ApplyDirective(decision);
			},

			ScheduledRequestAsync,
			ContextVersionProvider,
			setRequestScheduled: ScheduledFlags.Add,

			options: CompletionControllerOptions.Default with
			{
				RequestDebounceDelay = s_debounceDelay,
			},

			Logger
		);
	}

	/// <summary>Runs a gate request to completion by pumping the dispatcher.</summary>
	/// <param name="request">The task the gate returned.</param>
	/// <returns>The result of the request.</returns>
	internal bool RunToCompletion(Task<bool> request)
	{
		Dispatcher.PumpUntil(() => request.IsCompleted);
		return request.GetAwaiter().GetResult();
	}

	/// <summary>Disposes the dispatcher, which restores the synchronization context it installed.</summary>
	public void Dispose() => Dispatcher.Dispose();

	/// <summary>A provider callback that waits until the test releases it, and records the token it received.</summary>
	internal sealed class GatedDecision
	{
		private readonly TaskCompletionSource<CompletionSessionDirective> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

		/// <summary>The decision the callback returns when released; defaults to <see cref="OpenDecision"/>.</summary>
		internal CompletionSessionDirective Decision { get; init; } = OpenDecision();

		/// <summary>The token the gate passed to the callback.</summary>
		internal CancellationToken Token { get; private set; }

		/// <summary>A task that completes when the gate called the callback.</summary>
		internal Task Started => _started.Task;

		/// <summary>The callback to give to <see cref="CompletionRequestGate.RequestAsync"/>.</summary>
		/// <param name="cancellationToken">The token of the request.</param>
		/// <returns>The task that completes with the decision once the test releases it.</returns>
		internal Task<CompletionSessionDirective> RunAsync(CancellationToken cancellationToken)
		{
			Token = cancellationToken;
			_started.SetResult();

			return _gate.Task;
		}

		/// <summary>Releases the callback with its decision.</summary>
		internal void Release() => _gate.SetResult(Decision);

		/// <summary>Fails the callback with the given exception.</summary>
		/// <param name="exception">The failure.</param>
		internal void Fail(Exception exception) => _gate.SetException(exception);
	}
}
