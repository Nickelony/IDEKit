#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Verifies that the factory builds a code-action controller over a real text area: the selection source, the engine dispatcher,
/// and the creation rules. The request coordination itself is covered by the controller tests, which run without an editor.
/// </summary>
/// <remarks>
/// The editors are never shown in a window, so the class also runs in a headless leg.
/// </remarks>
[STATestClass]
public sealed class CodeActionControllerFactoryTests
{
	[TestMethod]
	public void Create_NullTextAreaOrProvider_ThrowsArgumentNullException()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var provider = new RecordingProvider();

		CodeActionControllerHooks hooks = CreateHooks();

		Assert.ThrowsExactly<ArgumentNullException>(() => CodeActionControllerFactory.Create(textArea: null!, provider, hooks));
		Assert.ThrowsExactly<ArgumentNullException>(() => CodeActionControllerFactory.Create(editor.TextArea, provider: null!, hooks));
	}

	[TestMethod]
	public void Create_MissingRequestBuilder_ThrowsArgumentNullException()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var provider = new RecordingProvider();
		var hooks = new CodeActionControllerHooks(BuildRequest: null!, ExecuteActionAsync: static (_, _) => Task.CompletedTask);

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			CodeActionControllerFactory.Create(editor.TextArea, provider, hooks)
		);

		string? paramName = exception.ParamName;

		Assert.IsNotNull(paramName);
		Assert.Contains("BuildRequest", paramName);
	}

	[TestMethod]
	public void Create_MissingExecuteHook_ThrowsArgumentNullException()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var provider = new RecordingProvider();
		var hooks = new CodeActionControllerHooks(BuildRequest: static _ => null, ExecuteActionAsync: null!);

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			CodeActionControllerFactory.Create(editor.TextArea, provider, hooks)
		);

		string? paramName = exception.ParamName;

		Assert.IsNotNull(paramName);
		Assert.Contains("ExecuteActionAsync", paramName);
	}

	[TestMethod]
	public void Create_OffTheTextAreaThread_ThrowsInvalidOperationException()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var provider = new RecordingProvider();

		CodeActionControllerHooks hooks = CreateHooks();

		Exception? failure = null;

		// A dedicated thread is not the text area's thread, and the factory refuses to create a controller there.
		var thread = new Thread(() =>
		{
			try
			{
				CodeActionControllerFactory.Create(editor.TextArea, provider, hooks).Dispose();
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.Start();
		thread.Join();

		Assert.IsInstanceOfType<InvalidOperationException>(failure);

		// The engine rejects an off-thread read of the text area with the same exception type, so the stack tells where the factory stopped: it
		// refused the thread before it built the selection source, which would subscribe to the text area.
		Assert.DoesNotContain(nameof(CodeActionSelectionSource), failure?.StackTrace ?? string.Empty);
	}

	[TestMethod]
	public void RequestAsync_ReadsTheContextOfTheTextArea()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		var provider = new RecordingProvider();
		var contexts = new List<CodeActionSelection>();

		using CodeActionController controller = CreateController(editor, provider, contexts);

		editor.Select(2, 3);

		RunToCompletion(controller.RequestAsync());

		Assert.HasCount(1, contexts);
		Assert.AreEqual("one\ntwo\nthree", contexts[0].GetText());
		Assert.AreEqual(new TextRange(2, 3), contexts[0].Range);
		Assert.HasCount(1, provider.Requests);
	}

	[TestMethod]
	public void RequestAsync_WithActions_PublishesTheCaretLineAsTheIndicatorLine()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		var provider = new RecordingProvider { Result = [new CodeAction("Fix it")] };

		using CodeActionController controller = CreateController(editor, provider);

		editor.CaretOffset = 4;

		RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(controller.HasActions);
		Assert.AreSequenceEqual([2], [.. controller.GetMarkedLineNumbers()]);
	}

	[TestMethod]
	public void CaretMove_Settles_RequestsAgainAndMovesTheIndicator()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		var provider = new RecordingProvider { Result = [new CodeAction("Fix it")] };

		using CodeActionController controller = CreateController(editor, provider);

		RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		// The caret leaves the indicator line, which clears the indicator at once; the debounced request then publishes the new line.
		editor.CaretOffset = 4;
		Assert.IsFalse(controller.HasActions);

		DispatcherTestHelpers.PumpUntil(() =>
		{
			IReadOnlyList<int> lines = controller.GetMarkedLineNumbers();
			return controller.HasActions && lines.Count == 1 && lines[0] == 2;
		});

		Assert.HasCount(2, provider.Requests);
	}

	[TestMethod]
	public void DocumentEdit_Settles_RequestsAgainForTheEditedDocument()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		var provider = new RecordingProvider { Result = [new CodeAction("Fix it")] };

		using CodeActionController controller = CreateController(editor, provider);

		RunToCompletion(controller.RequestAsync());
		Assert.HasCount(1, provider.Requests);

		// A document edit is a context change like a caret move, so the controller requests again through its debounced path even though
		// neither the caret nor the selection moved.
		editor.Document.Insert(editor.Document.TextLength, " four");

		DispatcherTestHelpers.PumpUntil(() => provider.Requests.Count >= 2);

		Assert.Contains("four", provider.Requests[^1].DocumentText);
	}

	[TestMethod]
	public void DocumentSwap_RequestsAgainForTheNewDocumentAndIgnoresTheOldOne()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		var provider = new RecordingProvider { Result = [new CodeAction("Fix it")] };

		using CodeActionController controller = CreateController(editor, provider);

		TextDocument firstDocument = editor.Document;

		editor.Document = new TextDocument("alpha\nbeta");

		DispatcherTestHelpers.PumpUntil(() => provider.Requests.Count > 0 && provider.Requests[^1].DocumentText == "alpha\nbeta");

		int requestCount = provider.Requests.Count;

		// Only the new document is observed: changing the old one must not schedule a request.
		firstDocument.Insert(0, "ignored ");

		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(40.0));

		Assert.HasCount(requestCount, provider.Requests);
	}

	[TestMethod]
	public void RequestAsync_PoolThreadProviderContinuation_PublishesOnTheDispatcherThread()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var provider = new RecordingProvider
		{
			Respond = async _ =>
			{
				providerStarted.SetResult();

				await providerGate.Task.ConfigureAwait(false);
				return [new CodeAction("Fix it")];
			},
		};

		using CodeActionController controller = CreateController(editor, provider);

		int dispatcherThreadId = Environment.CurrentManagedThreadId;
		int publishThreadId = 0;

		controller.Changed += (_, _) => publishThreadId = Environment.CurrentManagedThreadId;

		// The provider continuation resumes on a thread-pool thread, so the publish step must still run on the dispatcher thread the test body runs on.
		Task request = controller.RequestAsync();

		// Wait for the provider to reach the gate instead of a fixed window, so the release cannot race the provider's start on a loaded machine.
		DispatcherTestHelpers.PumpUntil(() => providerStarted.Task.IsCompleted);
		providerGate.SetResult();
		RunToCompletion(request);

		Assert.IsTrue(controller.HasActions);
		Assert.AreEqual(dispatcherThreadId, publishThreadId, "The publish must run on the dispatcher thread.");
	}

	[TestMethod]
	public void Dispose_ClearsTheActionsAndStopsRequestingOnTextAreaChanges()
	{
		TextEditor editor = TestHost.CreateEditor("one\ntwo\nthree");

		var provider = new RecordingProvider { Result = [new CodeAction("Fix it")] };

		CodeActionController controller = CreateController(editor, provider);

		RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		controller.Dispose();

		Assert.IsFalse(controller.HasActions);

		editor.CaretOffset = 4;
		editor.Document.Insert(0, "zero ");
		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(40.0));

		Assert.HasCount(1, provider.Requests);
	}

	private static void RunToCompletion(Task task)
	{
		DispatcherTestHelpers.PumpUntil(() => task.IsCompleted);
		task.GetAwaiter().GetResult();
	}

	// The debounce delay is one millisecond, so a scheduled request runs within the pump of a test, and a negative check that pumps for a short
	// window still sees a request that should not have been suppressed.
	private static CodeActionController CreateController(TextEditor editor, ICodeActionProvider provider, List<CodeActionSelection>? contexts = null)
	{
		return CodeActionControllerFactory.Create(
			textArea: editor.TextArea,
			provider,
			hooks: CreateHooks(contexts),
			options: CodeActionPresenterTestHost.s_fastOptions
		);
	}

	private static CodeActionControllerHooks CreateHooks(List<CodeActionSelection>? contexts = null)
	{
		return new CodeActionControllerHooks(
			BuildRequest: context =>
			{
				contexts?.Add(context);
				return new CodeActionRequest("factory-test.lua", context.GetText(), context.Range);
			},

			ExecuteActionAsync: static (_, _) => Task.CompletedTask
		);
	}

	private sealed class RecordingProvider : ICodeActionProvider
	{
		public List<CodeActionRequest> Requests { get; } = [];

		public IReadOnlyList<CodeAction> Result { get; init; } = [];

		public Func<CodeActionRequest, Task<IReadOnlyList<CodeAction>>>? Respond { get; init; }

		public Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken = default)
		{
			Requests.Add(request);
			return Respond is { } respond ? respond(request) : Task.FromResult(Result);
		}
	}
}
