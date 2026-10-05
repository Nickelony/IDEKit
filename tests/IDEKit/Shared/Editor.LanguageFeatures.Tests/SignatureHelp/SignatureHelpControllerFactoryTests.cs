#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Verifies that the factory builds a signature help controller over a real text area: the caret read, the engine dispatcher, and the creation rules.
/// The request coordination itself is covered by the controller tests, which run without an editor.
/// </summary>
/// <remarks>
/// The editors are never shown in a window, so the class also runs in a headless leg.
/// </remarks>
[STATestClass]
public sealed class SignatureHelpControllerFactoryTests
{
	[TestMethod]
	public void Create_NullTextAreaOrProvider_ThrowsArgumentNullException()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var provider = new ScriptedSignatureHelpProvider();

		Assert.ThrowsExactly<ArgumentNullException>(() => SignatureHelpControllerFactory.Create(null!, provider, CreateHooks()));
		Assert.ThrowsExactly<ArgumentNullException>(() => SignatureHelpControllerFactory.Create(editor.TextArea, null!, CreateHooks()));
	}

	[TestMethod]
	public void Create_NullRequiredHook_ThrowsArgumentNullException()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var provider = new ScriptedSignatureHelpProvider();

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			SignatureHelpControllerFactory.Create(
				editor.TextArea,
				provider,
				new SignatureHelpControllerHooks(BuildRequest: null!, ShowSignatureHelp: static (_, _) => { }, DismissSignatureHelp: static () => { })
			)
		);

		string? paramName = exception.ParamName;

		Assert.IsNotNull(paramName);
		Assert.Contains("BuildRequest", paramName);
	}

	[TestMethod]
	public void Create_OffTheTextAreaThread_ThrowsInvalidOperationException()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var provider = new ScriptedSignatureHelpProvider();

		Exception? failure = null;

		// A dedicated thread is not the text area's thread, and the controller refuses to be created there.
		var thread = new Thread(() =>
		{
			try
			{
				SignatureHelpControllerFactory.Create(editor.TextArea, provider, CreateHooks()).Dispose();
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.Start();
		thread.Join();

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
	}

	[TestMethod]
	public void ScheduleRequest_ReadsTheCaretOfTheTextAreaThroughTheEngineTimer()
	{
		TextEditor editor = TestHost.CreateEditor(new string('x', 20));

		var provider = new ScriptedSignatureHelpProvider();

		using SignatureHelpController controller = SignatureHelpControllerFactory.Create(
			textArea: editor.TextArea,
			provider,
			hooks: CreateHooks(editor),

			options: SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			}
		);

		editor.TextArea.Caret.Offset = 7;

		controller.ScheduleRequest();

		DispatcherTestHelpers.PumpUntil(() => provider.RequestOffsets.Count >= 1);

		// The caret delegate reads the caret of the text area the controller serves, and the debounce ran on its dispatcher.
		Assert.AreSequenceEqual([7], provider.RequestOffsets);
	}

	[TestMethod]
	public void ScheduleRequest_TextAreaWithoutDocument_SchedulesNothing()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		using SignatureHelpController controller = SignatureHelpControllerFactory.Create(
			editor.TextArea,
			new ScriptedSignatureHelpProvider(),
			CreateHooks()
		);

		editor.Document = null!;

		// A text area without a document has no caret, which the delegate reports as no offset.
		controller.ScheduleRequest();

		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	[DataRow(false, DisplayName = "KeepingTheAmbientSynchronizationContext")]
	[DataRow(true, DisplayName = "ClearingTheAmbientSynchronizationContext")]
	public void RequestAsync_ProviderCompletingOnAPoolThread_ShowsOnTheTextAreaThread(bool clearSynchronizationContext)
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		int showThreadId = 0;
		int providerThreadId = 0;

		var provider = new ScriptedSignatureHelpProvider
		{
			RespondAsync = async () =>
			{
				await Task.Run(() => providerThreadId = Environment.CurrentManagedThreadId).ConfigureAwait(false);
				return new SignatureHelpInformation([new SignatureInformation("sample(alpha)")]);
			},
		};

		using SignatureHelpController controller = SignatureHelpControllerFactory.Create(
			editor.TextArea,
			provider,
			CreateHooks(editor, show: _ => showThreadId = Environment.CurrentManagedThreadId)
		);

		// The headless Avalonia session runs a test body under a posting context, so the continuation would return to the UI thread by itself;
		// clearing the context makes the continuation resume on the pool, which is the path that must marshal the publication.
		SynchronizationContext? ambientContext = SynchronizationContext.Current;
		Task request;

		try
		{
			if (clearSynchronizationContext)
				SynchronizationContext.SetSynchronizationContext(null);

			request = controller.RequestAsync(2);
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(ambientContext);
		}

		DispatcherTestHelpers.PumpUntil(() => request.IsCompleted);
		request.GetAwaiter().GetResult();

		// The provider answered on a pool thread, and the controller still published on the text area's thread.
		Assert.AreEqual(Environment.CurrentManagedThreadId, showThreadId);
		Assert.AreNotEqual(showThreadId, providerThreadId);
	}

	private static SignatureHelpControllerHooks CreateHooks(TextEditor editor, Action<SignatureHelpInformation>? show = null)
	{
		return new SignatureHelpControllerHooks(
			BuildRequest: (offset, context) =>
				new SignatureHelpRequest(documentId: "factory-test.lua", documentText: editor.Text, caretOffset: offset, context),

			ShowSignatureHelp: (information, _) => show?.Invoke(information),
			DismissSignatureHelp: static () => { }
		);
	}

	private static SignatureHelpControllerHooks CreateHooks()
	{
		return new SignatureHelpControllerHooks(
			BuildRequest: static (_, _) => null,
			ShowSignatureHelp: static (_, _) => { },
			DismissSignatureHelp: static () => { }
		);
	}

	private sealed class ScriptedSignatureHelpProvider : ISignatureHelpProvider
	{
		public List<int> RequestOffsets { get; } = [];

		public Func<Task<SignatureHelpInformation?>> RespondAsync { get; set; } = static () => Task.FromResult<SignatureHelpInformation?>(null);

		public Task<SignatureHelpInformation?> GetSignatureHelpAsync(SignatureHelpRequest request, CancellationToken cancellationToken = default)
		{
			RequestOffsets.Add(request.CaretOffset);
			return RespondAsync();
		}
	}
}
