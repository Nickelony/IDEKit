#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls.Primitives;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Controls.Primitives;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Verifies that the factory builds a hover controller over a real text area: the pointer source, the engine dispatcher, and the creation rules.
/// The request coordination itself is covered by the controller tests, which run without an editor.
/// </summary>
/// <remarks>
/// The editors are never shown in a window, so the class also runs in a headless leg.
/// </remarks>
[STATestClass]
public sealed class HoverControllerFactoryTests
{
	[TestMethod]
	public void Create_NullTextAreaOrHooks_ThrowsArgumentNullException()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var provider = new NoHoverProvider();

		HoverControllerHooks hooks = CreateHooks([]);

		Assert.ThrowsExactly<ArgumentNullException>(() => HoverControllerFactory.Create(textArea: null!, provider, hooks));
		Assert.ThrowsExactly<ArgumentNullException>(() => HoverControllerFactory.Create(editor.TextArea, provider, hooks: null!));
	}

	[TestMethod]
	public void RequestAsync_WithoutAProvider_ShowsTheDiagnosticUnderThePointerAndAsksForNoHover()
	{
		TextEditor editor = TestHost.CreateEditor("sample");
		var displays = new List<(HoverInformation?, Diagnostic?)>();

		var diagnostic = new Diagnostic(
			Nickelony.IDEKit.Core.Diagnostics.DiagnosticSeverity.Information,
			"marker",
			new Nickelony.IDEKit.Core.Text.TextRange(0, 6)
		);

		using HoverController controller = HoverControllerFactory.Create(
			textArea: editor.TextArea,
			provider: null,

			hooks: new HoverControllerHooks
			{
				BuildDecision = offset => new HoverDecision
				{
					ShouldRequestHover = true,
					RequestOffset = offset,
					CanShowDiagnosticFallback = true,
					Diagnostic = diagnostic,
				},

				ShowHover = (hoverInfo, diagnosticInfo, _) => displays.Add((hoverInfo, diagnosticInfo)),
			},

			options: new HoverPointerOptions { RestDelay = null },
			logger: null,
			offsetFromPoint: static _ => 2
		);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);
		controller.RequestAsync().GetAwaiter().GetResult();

		Assert.HasCount(1, displays);
		Assert.IsNull(displays[0].Item1);
		Assert.AreSame(diagnostic, displays[0].Item2);
	}

	[TestMethod]
	public void Create_NullRequiredHook_ThrowsArgumentNullException()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var provider = new NoHoverProvider();

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			HoverControllerFactory.Create(
				editor.TextArea,
				provider,

				new HoverControllerHooks
				{
					BuildDecision = static _ => CreateState(),
					BuildRequest = null!,
					ShowHover = static (_, _, _) => { },
				}
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

		var provider = new NoHoverProvider();

		HoverControllerHooks hooks = CreateHooks([]);

		Exception? failure = null;

		// A dedicated thread is not the text area's thread, and the factory refuses to create a controller there.
		var thread = new Thread(() =>
		{
			try
			{
				HoverControllerFactory.Create(editor.TextArea, provider, hooks).Dispose();
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
	public void RequestAsync_BeforeAnyPointerMove_AsksTheHostToHideTheTooltip()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var displays = new List<(HoverInformation?, Diagnostic?)>();
		var provider = new NoHoverProvider();

		using HoverController controller = HoverControllerFactory.Create(editor.TextArea, provider, CreateHooks(displays));

		// The source tracks the pointer events of the text area and has seen none, so the hover addresses no target.
		controller.RequestAsync().GetAwaiter().GetResult();

		Assert.AreEqual(0, provider.RequestCount);
		Assert.HasCount(1, displays);
		Assert.IsNull(displays[0].Item1);
		Assert.IsNull(displays[0].Item2);
	}

	[TestMethod]
	public void RequestAsync_PointerMovedWhereThePositionMapsToNoOffset_AsksTheHostToHideTheTooltip()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var displays = new List<(HoverInformation?, Diagnostic?)>();
		var provider = new NoHoverProvider();

		using HoverController controller = HoverControllerFactory.Create(editor.TextArea, provider, CreateHooks(displays));

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);

		// An editor that was never shown cannot map a position to an offset, which the source reports as no offset after a move as well.
		controller.RequestAsync().GetAwaiter().GetResult();

		Assert.AreEqual(0, provider.RequestCount);
		Assert.HasCount(1, displays);
		Assert.IsNull(displays[0].Item1);
		Assert.IsNull(displays[0].Item2);
	}

	[TestMethod]
	public void PointerRests_RequestsAHoverWithoutAHostCall()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var displays = new List<(HoverInformation?, Diagnostic?)>();

		using HoverController controller = HoverControllerFactory.Create(
			textArea: editor.TextArea,
			provider: new NoHoverProvider(),
			hooks: CreateHooks(displays),
			options: new HoverPointerOptions { RestDelay = TimeSpan.FromMilliseconds(10) }
		);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);

		// The editor was never shown, so the evaluation finds no offset and asks the host to hide its tooltip, which shows that it ran.
		DispatcherTestHelpers.PumpUntil(() => displays.Count > 0);

		Assert.HasCount(1, displays);
	}

	[TestMethod]
	public void ViewScrollsUnderARestingPointer_EvaluatesAgain()
	{
		TextEditor editor = TestHost.CreateEditor(string.Join("\n", Enumerable.Repeat("sample", 50)));

		var displays = new List<(HoverInformation?, Diagnostic?)>();

		using HoverController controller = HoverControllerFactory.Create(
			textArea: editor.TextArea,
			provider: new NoHoverProvider(),
			hooks: CreateHooks(displays),
			options: new HoverPointerOptions { RestDelay = TimeSpan.FromMilliseconds(10) }
		);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);
		DispatcherTestHelpers.PumpUntil(() => displays.Count > 0);

		Assert.HasCount(1, displays);

		// The pointer addresses other text after the scroll, so the source asks again once the pointer rests.
		ScrollTextView(editor, 40.0);
		DispatcherTestHelpers.PumpUntil(() => displays.Count > 1);

		Assert.HasCount(2, displays);
	}

	[TestMethod]
	public void ViewScrollsAfterThePointerLeft_EvaluatesNothing()
	{
		TextEditor editor = TestHost.CreateEditor(string.Join("\n", Enumerable.Repeat("sample", 50)));

		var displays = new List<(HoverInformation?, Diagnostic?)>();

		using HoverController controller = HoverControllerFactory.Create(
			textArea: editor.TextArea,
			provider: new NoHoverProvider(),
			hooks: CreateHooks(displays),
			options: new HoverPointerOptions { RestDelay = TimeSpan.FromMilliseconds(10) }
		);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);
		TestHoverPointerEvents.RaiseLeft(editor.TextArea);

		Assert.HasCount(1, displays);

		ScrollTextView(editor, 40.0);
		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(100));

		Assert.HasCount(1, displays);
	}

	[TestMethod]
	public void PointerLeaves_EvaluatesAtOnce_AndEndsTheWait()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var displays = new List<(HoverInformation?, Diagnostic?)>();

		using HoverController controller = HoverControllerFactory.Create(
			textArea: editor.TextArea,
			provider: new NoHoverProvider(),
			hooks: CreateHooks(displays),
			options: new HoverPointerOptions { RestDelay = TimeSpan.FromMilliseconds(30) }
		);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);
		TestHoverPointerEvents.RaiseLeft(editor.TextArea);

		Assert.HasCount(1, displays);
		Assert.IsNull(displays[0].Item1);

		// The wait the move started ended with the leave, so no second evaluation follows.
		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(120));

		Assert.HasCount(1, displays);
	}

	[TestMethod]
	public void WithoutARestDelay_ThePointerRequestsNothing()
	{
		TextEditor editor = TestHost.CreateEditor("sample");

		var displays = new List<(HoverInformation?, Diagnostic?)>();

		using HoverController controller = HoverControllerFactory.Create(
			textArea: editor.TextArea,
			provider: new NoHoverProvider(),
			hooks: CreateHooks(displays),
			options: new HoverPointerOptions { RestDelay = null }
		);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);
		TestHoverPointerEvents.RaiseLeft(editor.TextArea);
		DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(100));

		Assert.IsEmpty(displays);
	}

	[TestMethod]
	public void RequestAsync_TextUnchangedWhileTheHoverIsInFlight_ShowsTheHover()
	{
		using var scenario = new InFlightHover();

		scenario.Answer.SetResult(new HoverInformation("hover"));
		scenario.Complete();

		Assert.HasCount(1, scenario.Displays);
		Assert.IsNotNull(scenario.Displays[0].Item1);
	}

	[TestMethod]
	public void RequestAsync_TextChangedWhileTheHoverIsInFlight_ShowsNothing()
	{
		using var scenario = new InFlightHover();

		// The hover describes text that the edit moved, so the factory's document counter discards it.
		scenario.Editor.Document.Insert(0, "x");
		scenario.Answer.SetResult(new HoverInformation("hover"));
		scenario.Complete();

		Assert.IsEmpty(scenario.Displays);
	}

	[TestMethod]
	public void RequestAsync_DocumentSwappedWhileTheHoverIsInFlight_ShowsNothing()
	{
		using var scenario = new InFlightHover();

		scenario.Editor.Document = new TextDocument("sample");
		scenario.Answer.SetResult(new HoverInformation("hover"));
		scenario.Complete();

		Assert.IsEmpty(scenario.Displays);
	}

	[TestMethod]
	public void RequestAsync_WithoutAHostContextVersion_KeepsTheRequestOffsetHook()
	{
		TextEditor editor = TestHost.CreateEditor("sample");
		var displays = new List<(HoverInformation?, Diagnostic?)>();

		// The pointer rests over offset 2 and the host remaps the request to offset 3, which only the remapping hook reconciles.
		using HoverController controller = HoverControllerFactory.Create(
			textArea: editor.TextArea,
			provider: new AnsweringHoverProvider(),

			hooks: new HoverControllerHooks
			{
				BuildDecision = static _ => CreateState() with { RequestOffset = 3 },
				BuildRequest = static offset => new HoverRequest("factory-test.lua", "sample", offset),
				ResolveRequestOffset = static _ => 3,
				ShowHover = (hoverInfo, diagnosticInfo, _) => displays.Add((hoverInfo, diagnosticInfo)),
			},

			options: new HoverPointerOptions { RestDelay = null },
			logger: null,
			offsetFromPoint: static _ => 2
		);

		TestHoverPointerEvents.RaiseMoved(editor.TextArea);
		Task request = controller.RequestAsync();
		DispatcherTestHelpers.PumpUntil(() => request.IsCompleted);

		// The factory rebuilt the hooks to add its document counter, and the remapping hook came along, so the result matched the pointer.
		Assert.HasCount(1, displays);
		Assert.IsNotNull(displays[0].Item1);
	}

	[TestMethod]
	public void RequestAsync_HostContextVersion_TakesThePlaceOfTheDocumentCounter()
	{
		using var scenario = new InFlightHover(contextVersionProvider: static () => 0);

		// The host's version did not change, and the host owns the decision once it supplies one.
		scenario.Editor.Document.Insert(0, "x");
		scenario.Answer.SetResult(new HoverInformation("hover"));
		scenario.Complete();

		Assert.HasCount(1, scenario.Displays);
	}

	// The text view raises ScrollOffsetChanged when its offset changes, which a hidden editor allows through the engine's scroll interface.
	private static void ScrollTextView(TextEditor editor, double offset)
	{
#if AVALONIAEDIT
		((IScrollable)editor.TextArea.TextView).Offset = new Vector(0.0, offset);
#else
		// A text view outside a scroll viewer keeps its vertical offset at zero until it is allowed to scroll.
		var scrollInfo = (IScrollInfo)editor.TextArea.TextView;

		scrollInfo.CanVerticallyScroll = true;
		scrollInfo.SetVerticalOffset(offset);
#endif
	}

	private static HoverControllerHooks CreateHooks(List<(HoverInformation?, Diagnostic?)> displays)
	{
		return new HoverControllerHooks
		{
			BuildDecision = static _ => CreateState(),
			BuildRequest = static offset => new HoverRequest("factory-test.lua", "sample", offset),
			ShowHover = (hoverInfo, diagnosticInfo, _) => displays.Add((hoverInfo, diagnosticInfo)),
		};
	}

	private static HoverDecision CreateState()
	{
		return new HoverDecision
		{
			ShouldRequestHover = true,
			RequestOffset = 2,
			CanShowHoverContent = true,
			CanShowDiagnosticFallback = false,
			Diagnostic = null,
		};
	}

	private sealed class NoHoverProvider : IHoverProvider
	{
		public int RequestCount { get; private set; }

		public Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default)
		{
			RequestCount++;
			return Task.FromResult<HoverInformation?>(null);
		}
	}

	/// <summary>
	/// A factory-built controller whose pointer rests over offset 2 and whose provider answers when the test completes <see cref="Answer"/>,
	/// so the test changes the editor while the hover is in flight.
	/// </summary>
	private sealed class InFlightHover : IDisposable
	{
		private readonly HoverController _controller;
		private readonly Task _request;

		public InFlightHover(Func<int>? contextVersionProvider = null)
		{
			Editor = TestHost.CreateEditor("sample");

			HoverControllerHooks hooks = CreateHooks(Displays);

			_controller = HoverControllerFactory.Create(
				textArea: Editor.TextArea,
				provider: new GatedHoverProvider(Answer.Task),

				hooks: new HoverControllerHooks
				{
					BuildDecision = hooks.BuildDecision,
					BuildRequest = hooks.BuildRequest,
					ShowHover = hooks.ShowHover,
					ContextVersionProvider = contextVersionProvider,
				},

				options: new HoverPointerOptions { RestDelay = null },
				logger: null,
				offsetFromPoint: static _ => 2
			);

			TestHoverPointerEvents.RaiseMoved(Editor.TextArea);
			_request = _controller.RequestAsync();
		}

		public TextEditor Editor { get; }

		public TaskCompletionSource<HoverInformation?> Answer { get; } = new();

		public List<(HoverInformation?, Diagnostic?)> Displays { get; } = [];

		/// <summary>Runs the dispatcher until the hover evaluation concluded.</summary>
		public void Complete() => DispatcherTestHelpers.PumpUntil(() => _request.IsCompleted);

		public void Dispose() => _controller.Dispose();
	}

	private sealed class AnsweringHoverProvider : IHoverProvider
	{
		public Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult<HoverInformation?>(new HoverInformation("hover"));
	}

	private sealed class GatedHoverProvider(Task<HoverInformation?> answer) : IHoverProvider
	{
		public Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default) => answer;
	}
}
