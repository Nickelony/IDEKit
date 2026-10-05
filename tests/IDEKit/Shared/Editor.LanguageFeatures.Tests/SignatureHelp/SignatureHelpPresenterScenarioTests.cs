#if AVALONIAEDIT
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.AutoClosing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SignatureHelp;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionInputTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.AutoClosing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SignatureHelp;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionInputTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Covers a controller that the presenter overload of the factory wires to a shown popup: the caret that moves inside the call
/// and out of it, the popup that closes on its own, and the answers that arrive after a dismissal.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class SignatureHelpPresenterScenarioTests
{
	private const string Text = "call(alpha, beta) + rest";

	// The offset after the opening parenthesis, where the request that opens the popup asks.
	private const int OpenOffset = 5;

	private static readonly SignatureHelpInformation s_answer = new([new SignatureInformation("call(alpha, beta)")]);

	[TestMethod]
	public void Create_NullRequestBuilderOrPresenter_ThrowsArgumentNullException()
	{
		using var host = new PopupTestHost(Text);
		using var presenter = new SignatureHelpPresenter(host.Editor.TextArea, static _ => null);

		var provider = new ScriptedSignatureHelpProvider();

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			SignatureHelpControllerFactory.Create(textArea: host.Editor.TextArea, provider, presenter, buildRequest: null!)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			SignatureHelpControllerFactory.Create(textArea: host.Editor.TextArea, provider, presenter: null!, buildRequest: static (_, _) => null)
		);
	}

	[TestMethod]
	public void CaretMoveInsideTheCall_SchedulesAContentChangeRetrigger()
	{
		using var scenario = new Scenario();
		scenario.Open();

		scenario.Editor.TextArea.Caret.Offset = 8;
		DispatcherTestHelpers.PumpUntil(() => scenario.Provider.Contexts.Count >= 2);

		SignatureHelpContext retrigger = scenario.Provider.Contexts[1];

		Assert.AreEqual(SignatureHelpTriggerKind.ContentChange, retrigger.TriggerKind);
		Assert.IsTrue(retrigger.IsRetrigger);
		Assert.AreEqual(8, scenario.Provider.Offsets[1]);
		Assert.IsTrue(scenario.Presenter.IsOpen);
	}

	[TestMethod]
	public void RetriggerAnsweringNothing_DismissesThePopup()
	{
		using var scenario = new Scenario();
		scenario.Open();

		// The caret left the call, so the provider has no signature for it.
		scenario.Provider.Answer = null;
		scenario.Editor.TextArea.Caret.Offset = Text.Length;
		DispatcherTestHelpers.PumpUntil(() => !scenario.Presenter.IsOpen);

		Assert.IsFalse(scenario.Controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void CaretMoveBeforeTheOpenOffset_DismissesAtOnce()
	{
		using var scenario = new Scenario();
		scenario.Open();

		scenario.Editor.TextArea.Caret.Offset = OpenOffset - 1;

		// The dismissal needs no answer of the provider, so it is done before the caret move returns.
		Assert.IsFalse(scenario.Presenter.IsOpen);
		Assert.IsFalse(scenario.Controller.CurrentPresentation.IsPresentationVisibleOrRequestScheduled);
		Assert.HasCount(1, scenario.Provider.Contexts);
	}

	[TestMethod]
	public void CaretMoveBackAfterARetrigger_KeepsTheOffsetThatOpenedThePopup()
	{
		using var scenario = new Scenario();
		scenario.Open();

		scenario.Editor.TextArea.Caret.Offset = 9;
		DispatcherTestHelpers.PumpUntil(() => scenario.Provider.Contexts.Count >= 2 && !scenario.Controller.CurrentPresentation.IsRequestInFlight);

		// The retrigger answered at offset 9, and the call still starts at the offset that opened the popup.
		scenario.Editor.TextArea.Caret.Offset = 7;

		Assert.IsTrue(scenario.Presenter.IsOpen);
		Assert.IsTrue(scenario.Controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void PopupClosingOnItsOwn_DismissesTheController()
	{
		using var scenario = new Scenario();
		scenario.Open();

		TestPopupEvents.PressEscape(scenario.Editor.TextArea);

		Assert.IsFalse(scenario.Presenter.IsOpen);
		Assert.IsFalse(scenario.Controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void RetriggerAnsweringAfterTheUserDismissed_ShowsNothing()
	{
		using var scenario = new Scenario();
		scenario.Open();

		var answer = new TaskCompletionSource<SignatureHelpInformation?>();
		scenario.Provider.RespondAsync = () => answer.Task;

		scenario.Editor.TextArea.Caret.Offset = 8;
		DispatcherTestHelpers.PumpUntil(() => scenario.Provider.Contexts.Count >= 2);

		// The dismissal invalidates the retrigger in flight, so its answer finds nothing to show.
		TestPopupEvents.PressEscape(scenario.Editor.TextArea);
		answer.SetResult(s_answer);
		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(100));

		Assert.IsFalse(scenario.Presenter.IsOpen);
		Assert.IsFalse(scenario.Controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void ShowRefusedByAnInactiveWindow_DismissesTheControllerOnTheNextTurn()
	{
		using var scenario = new Scenario(activate: false);

		Task<bool> request = scenario.Controller.RequestAsync(OpenOffset);
		DispatcherTestHelpers.PumpUntil(() => request.IsCompleted);

		Assert.IsFalse(scenario.Presenter.IsOpen);

		DispatcherTestHelpers.PumpUntil(() => !scenario.Controller.CurrentPresentation.IsVisible);

		Assert.IsFalse(scenario.Controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void PresenterHide_DismissesTheWiredController()
	{
		using var scenario = new Scenario();
		scenario.Open();

		scenario.Presenter.Hide();

		Assert.IsFalse(scenario.Presenter.IsOpen);
		Assert.IsFalse(scenario.Controller.CurrentPresentation.IsVisible);

		// The controller knows the popup is gone, so a caret move after the host's hide schedules nothing.
		scenario.Editor.TextArea.Caret.Offset = 8;

		Assert.IsFalse(scenario.Controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void ControllerRecreatedOverOnePresenter_LeavesTheDisposedOneUnreachable()
	{
		using var host = new PopupTestHost(Text);
		using var presenter = new SignatureHelpPresenter(host.Editor.TextArea, static _ => new TextBlock { Text = "content" });

		WeakReference first = CreateAndDisposeController(host.Editor.TextArea, presenter);
		using SignatureHelpController second = CreateController(host.Editor.TextArea, presenter, new ScriptedSignatureHelpProvider());

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		// The second wiring replaced the first, so nothing the presenter holds keeps the disposed controller alive.
		Assert.IsFalse(first.IsAlive);

		host.Editor.TextArea.Caret.Offset = OpenOffset;
		Task<bool> request = second.RequestAsync(OpenOffset);
		DispatcherTestHelpers.PumpUntil(() => request.IsCompleted);
		TestPopupEvents.PressEscape(host.Editor.TextArea);

		Assert.IsFalse(presenter.IsOpen);
		Assert.IsFalse(second.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void RefusedShowFollowedByANewerShow_KeepsTheNewerPopup()
	{
		var answer = new SignatureHelpInformation([new SignatureInformation("call(alpha)"), new SignatureInformation("call(alpha, beta)")]);
		using var scenario = new Scenario(activate: false);

		scenario.Provider.Answer = answer;

		scenario.Editor.TextArea.Caret.Offset = OpenOffset;
		Task<bool> request = scenario.Controller.RequestAsync(OpenOffset);

		// The inactive window refused the show, and the dismissal the refusal posted has not run yet.
		Assert.IsFalse(scenario.Presenter.IsOpen);
		Assert.IsTrue(scenario.Controller.CurrentPresentation.IsVisible);

		TestWindowActivation.Activate(scenario.Window);

		Assert.IsTrue(scenario.Controller.SelectNextSignature());
		Assert.IsTrue(scenario.Presenter.IsOpen);

		DispatcherTestHelpers.PumpUntil(() => request.IsCompleted);
		DispatcherTestHelpers.PumpUntilIdle();

		// The posted dismissal found the newer popup open and left it.
		Assert.IsTrue(scenario.Presenter.IsOpen);
		Assert.IsTrue(scenario.Controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void TextRemovedBeforeTheCall_MovesTheOffsetThatOpenedThePopup()
	{
		using var scenario = new Scenario();
		scenario.Open();

		// Two characters before the call go, so the call starts two characters earlier and the caret moved with the text.
		scenario.Editor.Document.Remove(0, 2);

		Assert.IsTrue(scenario.Presenter.IsOpen);
		Assert.IsTrue(scenario.Controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void TypedTriggerCharacter_OpensThePopup()
	{
		using var scenario = new Scenario();
		scenario.Provider.TriggerCharacters = ["("];
		scenario.Editor.TextArea.Caret.Offset = OpenOffset - 1;

		TypeThroughInputRoute(scenario.Editor, "(");
		DispatcherTestHelpers.PumpUntil(() => scenario.Presenter.IsOpen);

		SignatureHelpContext context = scenario.Provider.Contexts[^1];

		Assert.AreEqual(SignatureHelpTriggerKind.TriggerCharacter, context.TriggerKind);
		Assert.AreEqual("(", context.TriggerCharacter);
		Assert.IsFalse(context.IsRetrigger);
	}

	[TestMethod]
	public void TypedSpaceThatIsATriggerCharacter_ReachesTheProviderVerbatim()
	{
		using var scenario = new Scenario();
		scenario.Provider.TriggerCharacters = [" "];
		scenario.Editor.TextArea.Caret.Offset = OpenOffset;

		TypeThroughInputRoute(scenario.Editor, " ");
		DispatcherTestHelpers.PumpUntil(() => scenario.Provider.Contexts.Count > 0);

		SignatureHelpContext context = scenario.Provider.Contexts[^1];

		Assert.AreEqual(SignatureHelpTriggerKind.TriggerCharacter, context.TriggerKind);
		Assert.AreEqual(" ", context.TriggerCharacter);
	}

	[TestMethod]
	public void AutoClosedParenthesis_OpensThePopup()
	{
		var autoClosing = new AutoClosingService();
		AutoClosingOptions options = AutoClosingOptions.Default with { Pairs = [AutoClosingPair.Parentheses] };

		// The service subscribes before the controller, so it applies the pair and marks the input handled first.
		using var scenario = new Scenario(beforeController: editor =>
			editor.TextArea.TextEntering += (_, e) => autoClosing.ApplyTextEntering(editor, e, options)
		);

		scenario.Provider.TriggerCharacters = ["("];

		// The end of the text, where the service closes the pair it opens.
		scenario.Editor.TextArea.Caret.Offset = Text.Length;

		TypeThroughInputRoute(scenario.Editor, "(");
		DispatcherTestHelpers.PumpUntil(() => scenario.Presenter.IsOpen);

		Assert.AreEqual(Text + "()", scenario.Editor.Text);
		Assert.AreEqual("(", scenario.Provider.Contexts[^1].TriggerCharacter);
	}

	[TestMethod]
	public void TypedRetriggerCharacter_WhileThePopupIsOpen_RefreshesIt()
	{
		using var scenario = new Scenario();
		scenario.Provider.RetriggerCharacters = [","];
		scenario.Open();

		TypeThroughInputRoute(scenario.Editor, ",");
		DispatcherTestHelpers.PumpUntil(() => scenario.Provider.Contexts.Any(context => context.TriggerCharacter == ","));

		SignatureHelpContext retrigger = scenario.Provider.Contexts.Last(context => context.TriggerCharacter == ",");

		Assert.AreEqual(SignatureHelpTriggerKind.TriggerCharacter, retrigger.TriggerKind);
		Assert.IsTrue(retrigger.IsRetrigger);
		Assert.IsTrue(scenario.Presenter.IsOpen);
	}

	[TestMethod]
	public void TypedRetriggerCharacter_WhileThePopupIsClosed_RequestsNothing()
	{
		using var scenario = new Scenario();
		scenario.Provider.RetriggerCharacters = [","];
		scenario.Editor.TextArea.Caret.Offset = OpenOffset;

		TypeThroughInputRoute(scenario.Editor, ",");
		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(50));

		Assert.IsEmpty(scenario.Provider.Contexts);
		Assert.IsFalse(scenario.Presenter.IsOpen);
	}

	[TestMethod]
	public void ControllerDisposed_HidesThePopupWithoutReportingADismissal()
	{
		using var scenario = new Scenario();
		scenario.Open();

		int dismissed = 0;
		scenario.Presenter.Dismissed += (_, _) => dismissed++;

		scenario.Controller.Dispose();

		Assert.IsFalse(scenario.Presenter.IsOpen);
		Assert.AreEqual(0, dismissed);
	}

	/// <summary>A controller that the presenter overload created over a shown editor, with a provider that records its requests.</summary>
	private sealed class Scenario : IDisposable
	{
		private readonly PopupTestHost _host;

		public Scenario(bool activate = true, Action<TextEditor>? beforeController = null)
		{
			_host = new PopupTestHost(Text, activate);
			beforeController?.Invoke(_host.Editor);
			Presenter = new SignatureHelpPresenter(_host.Editor.TextArea, static _ => new TextBlock { Text = "content" });

			Controller = CreateController(_host.Editor.TextArea, Presenter, Provider);
		}

		public TextEditor Editor => _host.Editor;

		public HostWindow Window => _host.Window;

		public ScriptedSignatureHelpProvider Provider { get; } = new();

		public SignatureHelpPresenter Presenter { get; }

		public SignatureHelpController Controller { get; }

		/// <summary>Opens the popup with a request at the open offset, as a typed opening parenthesis does.</summary>
		public void Open()
		{
			_host.Editor.TextArea.Caret.Offset = OpenOffset;

			Task<bool> request = Controller.RequestAsync(OpenOffset, SignatureHelpTriggerKind.TriggerCharacter, "(");
			DispatcherTestHelpers.PumpUntil(() => request.IsCompleted);

			Assert.IsTrue(request.Result);
			Assert.IsTrue(Presenter.IsOpen);
		}

		public void Dispose()
		{
			Controller.Dispose();
			Presenter.Dispose();
			_host.Dispose();
		}
	}

	private static SignatureHelpController CreateController(TextArea textArea, SignatureHelpPresenter presenter, ISignatureHelpProvider provider)
	{
		return SignatureHelpControllerFactory.Create(
			textArea,
			provider,
			presenter,

			buildRequest: (offset, context) =>
				new SignatureHelpRequest(documentId: "scenario.lua", documentText: textArea.Document.Text, caretOffset: offset, context),

			options: SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			}
		);
	}

	// The controller is created and disposed in a frame of its own, so no local of the calling test keeps it reachable.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference CreateAndDisposeController(TextArea textArea, SignatureHelpPresenter presenter)
	{
		SignatureHelpController controller = CreateController(textArea, presenter, new ScriptedSignatureHelpProvider());
		controller.Dispose();

		return new WeakReference(controller);
	}

	private sealed class ScriptedSignatureHelpProvider : ISignatureHelpProvider, ISignatureHelpTriggerCharacters
	{
		public List<SignatureHelpContext> Contexts { get; } = [];

		public IReadOnlyList<string> TriggerCharacters { get; set; } = [];

		public IReadOnlyList<string> RetriggerCharacters { get; set; } = [];

		public List<int> Offsets { get; } = [];

		public SignatureHelpInformation? Answer { get; set; } = s_answer;

		public Func<Task<SignatureHelpInformation?>>? RespondAsync { get; set; }

		public Task<SignatureHelpInformation?> GetSignatureHelpAsync(SignatureHelpRequest request, CancellationToken cancellationToken = default)
		{
			Contexts.Add(request.Context!);
			Offsets.Add(request.CaretOffset);

			return RespondAsync?.Invoke() ?? Task.FromResult(Answer);
		}
	}
}
