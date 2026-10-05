using TextMateSharp.Model;
#if AVALONIAEDIT
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
#else
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows.Media;
using System.Windows.Threading;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;

#else

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextMateColorizerTests
{
	[TestMethod]
	public void ColorizeLine_WithRealModelAndGrammar_AppliesResolvedStyle()
	{
		using var host = TextMateColorizerTestHost.Create(
			grammarLanguageId: "lua",

			rules:
			[
				// Matches the root Lua scope present on every token, so the assertion does not
				// depend on grammar details.
				new TextMateTokenThemeRule { Scope = "source.lua", Foreground = "#FF0000" },
			],

			hostInWindow: true
		);

		// The paint path must not drive TextMateSharp's tokenizer (the colorizer never forces
		// tokenization), so wait for the model's background pass before painting.
		WaitForTokenization(host.Model, lineIndex: 0);

		VisualLine visualLine = GetPaintedVisualLine(host.Editor);

		Assert.IsTrue(HasThemedElement(visualLine, Color.FromRgb(0xFF, 0x00, 0x00)));
	}

	[TestMethod]
	public void ColorizeLine_AfterTheLineIsReTokenizedWithNewScopes_RefreshesTheCachedStyles()
	{
		using var host = TextMateColorizerTestHost.Create(
			grammarLanguageId: "lua",

			rules:
			[
				new TextMateTokenThemeRule { Scope = "source.lua", Foreground = "#FF0000" },
				new TextMateTokenThemeRule { Scope = "comment", Foreground = "#00FF00" },
			],

			hostInWindow: true
		);

		WaitForTokenization(host.Model, lineIndex: 0);

		VisualLine codeLine = GetPaintedVisualLine(host.Editor);

		// Before the edit the line is code (matched by the root scope), so no comment style is painted.
		Assert.IsFalse(HasThemedElement(codeLine, Color.FromRgb(0x00, 0xFF, 0x00)));

		// Re-tokenize the line into a comment, which resolves to a different style. The colorizer caches the
		// resolved style per line keyed on the model's token list, so this pins the assumption that the model
		// hands out a replaced list after re-tokenizing (a mutated, identity-stable list would keep serving the
		// stale pre-edit styles here).
		host.Document.Replace(0, host.Document.TextLength, "-- local value = 1\n");
		WaitForTokenization(host.Model, lineIndex: 0);

		VisualLine commentLine = GetPaintedVisualLine(host.Editor);

		Assert.IsTrue(HasThemedElement(commentLine, Color.FromRgb(0x00, 0xFF, 0x00)));
	}

	[TestMethod]
	public void ColorizeLine_WithoutTokenizedLines_LeavesBaseStyle()
	{
		// Without a grammar the model never produces tokens, so the paint path must leave every element
		// with the base style instead of forcing tokenization.
		using var host = TextMateColorizerTestHost.Create(
			rules: [new TextMateTokenThemeRule { Scope = "source.lua", Foreground = "#FF0000" }],
			hostInWindow: true
		);

		VisualLine visualLine = GetPaintedVisualLine(host.Editor);

		Assert.IsFalse(HasThemedElement(visualLine, Color.FromRgb(0xFF, 0x00, 0x00)));
	}

	[TestMethod]
	public void Dispose_DetachesModelListener()
	{
		using var host = TextMateColorizerTestHost.Create(grammarLanguageId: "lua");

		// The colorizer is the model's only listener, so the tokenizer thread stays alive while
		// the colorizer is attached.
		Assert.IsFalse(host.Model.IsStopped);

		host.Colorizer.Dispose();

		// Removing the last listener stops the model, which is the observable effect of the
		// detach contract TextMateSharp exposes.
		Assert.IsTrue(host.Model.IsStopped);
	}

	[TestMethod]
	public void Dispose_CalledTwice_IsIdempotent()
	{
		using var host = TextMateColorizerTestHost.Create(grammarLanguageId: "lua");

		host.Colorizer.Dispose();
		host.Colorizer.Dispose();

		Assert.IsTrue(host.Model.IsStopped);
	}

	[TestMethod]
	public void ModelTokensChanged_CoalescesBurstsIntoOneQueuedRedrawAndSkipsItAfterDispose()
	{
		List<Action> queuedRedraws = [];
		int redrawCount = 0;

		using var host = TextMateColorizerTestHost.Create(
			queueRedraw: action =>
			{
				queuedRedraws.Add(action);
				return PendingNoOpOperation();
			},

			redrawRange: (_, _) => redrawCount++
		);

		var listener = (IModelTokensChangedListener)host.Colorizer;
		var tokenChange = new ModelTokensChangedEvent([], host.Model);

		// A burst of notifications coalesces: only the first one of an unqueued burst dispatches.
		listener.ModelTokensChanged(tokenChange);
		listener.ModelTokensChanged(tokenChange);
		listener.ModelTokensChanged(tokenChange);

		Assert.HasCount(1, queuedRedraws);

		// Running the queued dispatch redraws once and opens the gate for the next burst.
		queuedRedraws[0]();
		listener.ModelTokensChanged(tokenChange);

		Assert.AreEqual(1, redrawCount);
		Assert.HasCount(2, queuedRedraws);

		// A disposed colorizer neither dispatches nor redraws an already queued dispatch.
		host.Colorizer.Dispose();
		queuedRedraws[1]();
		listener.ModelTokensChanged(tokenChange);

		Assert.AreEqual(1, redrawCount);
		Assert.HasCount(2, queuedRedraws);
	}

	[TestMethod]
	public void ModelTokensChanged_QueueRedrawThrows_ResetsGateAndAllowsNextDispatch()
	{
		List<Action> queuedRedraws = [];
		int queueAttempts = 0;

		using var host = TextMateColorizerTestHost.Create(
			queueRedraw: action =>
			{
				// Simulate a text view whose dispatcher has already shut down.
				if (queueAttempts++ == 0)
					throw new InvalidOperationException("The dispatcher has shut down.");

				queuedRedraws.Add(action);
				return null;
			},

			redrawRange: (_, _) => { }
		);

		var listener = (IModelTokensChangedListener)host.Colorizer;
		var tokenChange = new ModelTokensChangedEvent([], host.Model);

		// The failed dispatch must not escape and must not leave the coalescing gate closed.
		listener.ModelTokensChanged(tokenChange);

		Assert.IsEmpty(queuedRedraws);

		listener.ModelTokensChanged(tokenChange);

		Assert.HasCount(1, queuedRedraws);
	}

	[TestMethod]
	public void ModelTokensChanged_UntrackableQueue_ReopensGateForLaterNotifications()
	{
		int queueAttempts = 0;

		using var host = TextMateColorizerTestHost.Create(
			queueRedraw: _ =>
			{
				// A queue that cannot hand back a trackable operation must not leave the coalescing gate
				// closed, or no later token-change notification could ever queue a redraw and the view
				// would freeze on its last paint.
				queueAttempts++;
				return null;
			},

			redrawRange: (_, _) => { }
		);

		var listener = (IModelTokensChangedListener)host.Colorizer;
		var tokenChange = new ModelTokensChangedEvent([], host.Model);

		listener.ModelTokensChanged(tokenChange);
		listener.ModelTokensChanged(tokenChange);

		Assert.AreEqual(2, queueAttempts);
	}

	[TestMethod]
	public void ModelTokensChanged_AbortedQueuedRedraw_ReopensGateForLaterNotifications()
	{
		List<Action> queuedRedraws = [];
		DispatcherOperation? pendingOperation = null;

		int queueAttempts = 0;
		int redrawCount = 0;

		using var host = TextMateColorizerTestHost.Create(
			queueRedraw: action =>
			{
				queueAttempts++;

				// The first queue attempt hands out a real pending dispatcher operation that the
				// test aborts before the dispatcher can run it, which is what a dispatcher
				// shutdown after acceptance produces; later attempts are merely recorded.
				if (pendingOperation is null)
				{
#if AVALONIAEDIT
					pendingOperation = Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Background, TestContext.CancellationToken);
#else
					pendingOperation = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, action);
#endif
					return pendingOperation;
				}

				queuedRedraws.Add(action);
				return null;
			},

			redrawRange: (_, _) => redrawCount++
		);

		var listener = (IModelTokensChangedListener)host.Colorizer;
		var tokenChange = new ModelTokensChangedEvent([], host.Model);

		// The queued redraw is aborted while still pending, so its callback never runs.
		listener.ModelTokensChanged(tokenChange);
		pendingOperation!.Abort();

		// The abort reopened the coalescing gate, so the next notification queues a redraw again.
		listener.ModelTokensChanged(tokenChange);

		Assert.AreEqual(2, queueAttempts);
		Assert.HasCount(1, queuedRedraws);
		Assert.AreEqual(0, redrawCount);

		// Running the requeued dispatch redraws once and reopens the gate for the next burst.
		queuedRedraws[0]();

		Assert.AreEqual(1, redrawCount);

		listener.ModelTokensChanged(tokenChange);

		Assert.AreEqual(3, queueAttempts);
	}

	[TestMethod]
	public void ModelTokensChanged_QueuedRedraw_CoversChangedLineRangeUnion()
	{
		List<Action> queuedRedraws = [];
		List<(int FirstLineIndex, int LastLineIndex)> redrawRanges = [];

		using var host = TextMateColorizerTestHost.Create(
			queueRedraw: action =>
			{
				queuedRedraws.Add(action);
				return PendingNoOpOperation();
			},

			redrawRange: (firstLineIndex, lastLineIndex) => redrawRanges.Add((firstLineIndex, lastLineIndex))
		);

		var listener = (IModelTokensChangedListener)host.Colorizer;

		// An unqueued burst widens its pending range to the union of the reported changed lines;
		// model ranges carry one-based line numbers, so line 3 becomes index 2.
		listener.ModelTokensChanged(new ModelTokensChangedEvent([new TextMateSharp.Model.Range(3, 5)], host.Model));
		listener.ModelTokensChanged(new ModelTokensChangedEvent([new TextMateSharp.Model.Range(7, 9)], host.Model));

		Assert.HasCount(1, queuedRedraws);

		queuedRedraws[0]();

		Assert.HasCount(1, redrawRanges);
		Assert.AreEqual((2, 8), redrawRanges[0]);

		// A notification without ranges requests a whole-document redraw.
		listener.ModelTokensChanged(new ModelTokensChangedEvent([], host.Model));
		queuedRedraws[1]();

		Assert.AreEqual((0, int.MaxValue), redrawRanges[1]);
	}

	[TestMethod]
	public void ModelTokensChanged_ZeroBasedRange_WidensTheRedrawToTheWholeDocument()
	{
		List<Action> queuedRedraws = [];
		List<(int FirstLineIndex, int LastLineIndex)> redrawRanges = [];

		using var host = TextMateColorizerTestHost.Create(
			queueRedraw: action =>
			{
				queuedRedraws.Add(action);
				return PendingNoOpOperation();
			},

			redrawRange: (firstLineIndex, lastLineIndex) => redrawRanges.Add((firstLineIndex, lastLineIndex))
		);

		var listener = (IModelTokensChangedListener)host.Colorizer;

		// TextMateSharp's SetGrammar emits zero-based model ranges, so a zero FromLineNumber cannot be read
		// as one-based; it must widen the redraw to the whole document instead of becoming a negative index
		// that under-covers it.
		listener.ModelTokensChanged(new ModelTokensChangedEvent([new TextMateSharp.Model.Range(0, 2)], host.Model));
		queuedRedraws[0]();

		Assert.HasCount(1, redrawRanges);
		Assert.AreEqual((0, int.MaxValue), redrawRanges[0]);
	}

	[TestMethod]
	public void ColorizeLine_WithTraitRule_AppliesTypefaceAndDecorations()
	{
		using var host = TextMateColorizerTestHost.Create(
			grammarLanguageId: "lua",

			rules:
			[
				// Matches the root Lua scope present on every token, so the assertions do not depend
				// on grammar details.
				new TextMateTokenThemeRule
				{
					Scope = "source.lua",
					Foreground = "#FF0000",
					FontStyle = "bold italic underline strikethrough",
				},
			],

			hostInWindow: true
		);

		WaitForTokenization(host.Model, lineIndex: 0);
		VisualLine visualLine = GetPaintedVisualLine(host.Editor);

		bool sawTraits = false;

		foreach (VisualLineElement element in visualLine.Elements)
		{
			VisualLineElementTextRunProperties properties = element.TextRunProperties;

			if (
				properties.Typeface.Weight == FontWeights.Bold
				&& properties.Typeface.Style == FontStyles.Italic
				&& properties.TextDecorations is { Count: 2 }
			)
			{
				sawTraits = true;
				break;
			}
		}

		Assert.IsTrue(sawTraits, "Expected at least one element to carry the bold italic typeface and both decorations.");
	}

	[TestMethod]
	public void ColorizeLine_InvalidatedLine_LeavesBaseStyle()
	{
		using var host = TextMateColorizerTestHost.Create(
			grammarLanguageId: "lua",
			rules: [new TextMateTokenThemeRule { Scope = "source.lua", Foreground = "#FF0000" }],
			hostInWindow: true
		);

		WaitForTokenization(host.Model, lineIndex: 0);

		Assert.IsTrue(HasThemedElement(GetPaintedVisualLine(host.Editor), Color.FromRgb(0xFF, 0x00, 0x00)));

		// A line whose tokenization state was invalidated must not paint its stale tokens while
		// the model's background pass has not re-tokenized it. Setting the flag directly leaves
		// the line out of the model's queue, so the state stays invalid for the repaint.
		host.LineList.Get(0).IsInvalid = true;

		Assert.IsTrue(host.Model.IsLineInvalid(0));
		Assert.IsFalse(HasThemedElement(GetPaintedVisualLine(host.Editor), Color.FromRgb(0xFF, 0x00, 0x00)));
	}

	[TestMethod]
	public void ColorizeLine_AfterDispose_LeavesBaseStyle()
	{
		using var host = TextMateColorizerTestHost.Create(
			grammarLanguageId: "lua",
			rules: [new TextMateTokenThemeRule { Scope = "source.lua", Foreground = "#FF0000" }],
			hostInWindow: true
		);

		WaitForTokenization(host.Model, lineIndex: 0);

		Assert.IsTrue(HasThemedElement(GetPaintedVisualLine(host.Editor), Color.FromRgb(0xFF, 0x00, 0x00)));

		host.Colorizer.Dispose();

		// A disposed colorizer never applies styles again, even while the tokens are still
		// available; the paint early-returns for a disposed colorizer.
		Assert.IsFalse(HasThemedElement(GetPaintedVisualLine(host.Editor), Color.FromRgb(0xFF, 0x00, 0x00)));
	}

	/// <summary>
	/// Hands the colorizer a real, still-pending dispatcher operation so the coalescing gate stays closed until the test drives the captured
	/// action itself.
	/// </summary>
	/// <remarks>
	/// A <see langword="null"/> return reopens the gate immediately, so coalescing is observable only through a trackable operation.
	/// The operation carries a no-op: the captured action is what the test runs, and running it here as well would double-count redraws.
	/// </remarks>
	private static DispatcherOperation PendingNoOpOperation()
#if AVALONIAEDIT
		=> Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

	public TestContext? TestContext { get; set; }
#else
		=>
		Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { }));
#endif
}
