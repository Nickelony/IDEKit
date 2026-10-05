using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Builds a completion request controller over a manual dispatcher, a scripted editor and a recording presenter, so controller tests share one host
/// instead of repeating the delegates. The host owns the dispatcher the controller runs on.
/// </summary>
/// <remarks>
/// The editor is the <see cref="Text"/> and the <see cref="CaretOffset"/> of the host: a test types by changing them before it lets
/// an answer arrive. The delegate properties are mutable on purpose: a test starts from the defaults and swaps in the behavior it needs
/// before it creates the controller. The host disposes its dispatcher, which restores the thread's synchronization context.
/// </remarks>
internal sealed class CompletionControllerTestHost : IDisposable
{
	/// <summary>The debounce delay of the controller under test; a test advances the virtual clock to or past it, or stops short of it.</summary>
	internal static readonly TimeSpan s_debounceDelay = TimeSpan.FromMilliseconds(10.0);

	/// <summary>The document identifier that the controller of the tests sends with each request.</summary>
	internal const string DocumentId = "file:///test.lua";

	/// <param name="text">The text of the scripted editor.</param>
	/// <param name="caretOffset">The caret of the scripted editor, or <see langword="null"/> for the end of the text.</param>
	/// <param name="installSynchronizationContext">
	/// Whether the dispatcher is the synchronization context of the test thread, as the dispatcher of a running editor is. A test that needs
	/// a continuation to resume on the thread pool passes <see langword="false"/>.
	/// </param>
	internal CompletionControllerTestHost(string text = "", int? caretOffset = null, bool installSynchronizationContext = true)
	{
		Dispatcher = new FakePumpedDispatcher(installSynchronizationContext);
		Text = text;
		CaretOffset = caretOffset ?? text.Length;
	}

	/// <summary>The dispatcher the controller runs on; the test pumps it and moves its clock.</summary>
	internal FakePumpedDispatcher Dispatcher { get; }

	/// <summary>The logger the controller reports request failures to.</summary>
	internal RecordingLogger Logger { get; } = new();

	/// <summary>The provider the controller asks.</summary>
	internal ScriptedCompletionProvider Provider { get; set; } = new();

	/// <summary>The presenter that receives the decisions of the controller.</summary>
	internal RecordingCompletionPresenter Presenter { get; } = new();

	/// <summary>The text of the scripted editor.</summary>
	internal string Text { get; set; }

	/// <summary>The caret of the scripted editor.</summary>
	internal int CaretOffset { get; set; }

	/// <summary>Whether the scripted editor shows a document; an editor without one captures no context.</summary>
	internal bool HasDocument { get; set; } = true;

	/// <summary>Holds the scope hook the controller is created with, or <see langword="null"/> to treat every offset as code.</summary>
	internal Func<int, CompletionScopes>? ScopeAt { get; set; }

	/// <summary>
	/// Holds the context-version provider the controller is created with, or <see langword="null"/> for a version that never changes.
	/// </summary>
	internal Func<int>? ContextVersionProvider { get; set; }

	/// <summary>Holds the options the controller is created with; the default only shortens the debounce delay.</summary>
	internal CompletionControllerOptions Options { get; set; } = CompletionControllerOptions.Default with { RequestDebounceDelay = s_debounceDelay };

	/// <summary>Creates a controller wired to this host.</summary>
	/// <returns>The created controller.</returns>
	internal CompletionController CreateController()
	{
		return new CompletionController(
			CaptureContext,
			DocumentId,
			Provider,
			Presenter,
			Dispatcher,
			hooks: new CompletionControllerHooks { ScopeAt = ScopeAt, ContextVersionProvider = ContextVersionProvider },
			Options,
			kernel: null,
			Logger
		);
	}

	/// <summary>Inserts text at the caret of the scripted editor and moves the caret behind it, as typing does.</summary>
	/// <param name="typed">The inserted text.</param>
	internal void Type(string typed)
	{
		Text = Text.Insert(CaretOffset, typed);
		CaretOffset += typed.Length;
	}

	/// <summary>Runs a request to completion by pumping the dispatcher.</summary>
	/// <param name="request">The task the controller returned.</param>
	/// <returns>The result of the request.</returns>
	internal bool RunToCompletion(Task<bool> request)
	{
		Dispatcher.PumpUntil(() => request.IsCompleted);
		return request.GetAwaiter().GetResult();
	}

	/// <summary>Disposes the dispatcher, which restores the synchronization context it installed.</summary>
	public void Dispose() => Dispatcher.Dispose();

	private CompletionEditorContext? CaptureContext() => HasDocument ? new CompletionEditorContext(new StringTextSnapshot(Text), CaretOffset) : null;

	/// <summary>A provider answer that waits until the test releases it, and records the token it received.</summary>
	internal sealed class GatedAnswer
	{
		private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

		/// <param name="items">The labels of the items the answer returns when released.</param>
		internal GatedAnswer(params string[] items) => Items = [.. items.Select(label => new CompletionItem(label))];

		/// <summary>The items the answer returns when released.</summary>
		internal IReadOnlyList<CompletionItem> Items { get; }

		/// <summary>The token the controller passed to the provider.</summary>
		internal CancellationToken Token { get; private set; }

		/// <summary>A task that completes when the controller called the provider.</summary>
		internal Task Started => _started.Task;

		/// <summary>The answer to give to <see cref="ScriptedCompletionProvider.Answer"/>.</summary>
		internal async Task<IReadOnlyList<CompletionItem>> AnswerAsync(CompletionRequest request, CancellationToken cancellationToken)
		{
			Token = cancellationToken;
			_started.TrySetResult();

			await _gate.Task.ConfigureAwait(true);

			return Items;
		}

		/// <summary>Lets the provider return its items.</summary>
		internal void Release() => _gate.TrySetResult();
	}
}
