#if AVALONIAEDIT
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;
using Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateThemeTestHelpers;
#else
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using System.Windows.Media;
using System.Windows.Threading;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateThemeTestHelpers;
#endif
using TextMateSharp.Model;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;
#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextMateColorizingTransformerTests
{
	[TestMethod]
	public void ColorizeLine_WithRealModelAndGrammar_AppliesResolvedStyle()
	{
		using var host = TextMateColorizingTransformerHost.Create(
			grammarLanguageId: "lua",
			rules:
			[
				// Matches the root Lua scope present on every token, so the assertion does not
				// depend on grammar details.
				new TextMateTokenThemeRule { Scope = "source.lua", Foreground = "#FF0000" }
			],
			hostInWindow: true);

		// The paint path must not drive TextMateSharp's tokenizer (the transformer never forces
		// tokenization), so wait for the model's background pass before painting.
		WaitForTokenization(host.Model, lineIndex: 0);

		VisualLine visualLine = GetPaintedVisualLine(host.Editor);

		Assert.IsTrue(HasThemedElement(visualLine, Color.FromRgb(0xFF, 0x00, 0x00)));
	}

	[TestMethod]
	public void ColorizeLine_WithoutTokenizedLines_LeavesBaseStyle()
	{
		// Without a grammar the model never produces tokens, so the paint path must leave every element
		// with the base style instead of forcing tokenization.
		using var host = TextMateColorizingTransformerHost.Create(
			rules: [new TextMateTokenThemeRule { Scope = "source.lua", Foreground = "#FF0000" }],
			hostInWindow: true);

		VisualLine visualLine = GetPaintedVisualLine(host.Editor);

		Assert.IsFalse(HasThemedElement(visualLine, Color.FromRgb(0xFF, 0x00, 0x00)));
	}

	[TestMethod]
	public void Dispose_DetachesModelListener()
	{
		using var host = TextMateColorizingTransformerHost.Create(grammarLanguageId: "lua");

		// The transformer is the model's only listener, so the tokenizer thread stays alive while
		// the transformer is attached.
		Assert.IsFalse(host.Model.IsStopped);

		host.Transformer.Dispose();

		// Removing the last listener stops the model, which is the observable effect of the
		// detach contract TextMateSharp exposes.
		Assert.IsTrue(host.Model.IsStopped);
	}

	[TestMethod]
	public void Dispose_CalledTwice_IsIdempotent()
	{
		using var host = TextMateColorizingTransformerHost.Create(grammarLanguageId: "lua");

		host.Transformer.Dispose();
		host.Transformer.Dispose();

		Assert.IsTrue(host.Model.IsStopped);
	}

	[TestMethod]
	public void ModelTokensChanged_CoalescesBurstsIntoOneQueuedRedrawAndSkipsItAfterDispose()
	{
		List<Action> queuedRedraws = [];
		int redrawCount = 0;

		using var host = TextMateColorizingTransformerHost.Create(
			queueRedraw: action =>
			{
				queuedRedraws.Add(action);
				return PendingNoOpOperation();
			},
			redrawRange: (_, _) => redrawCount++);

		var listener = (IModelTokensChangedListener)host.Transformer;
		var tokenChange = new ModelTokensChangedEvent([], host.Model);

		// A burst of notifications coalesces: only the first one of an unqueued burst dispatches.
		listener.ModelTokensChanged(tokenChange);
		listener.ModelTokensChanged(tokenChange);
		listener.ModelTokensChanged(tokenChange);

		Assert.AreEqual(1, queuedRedraws.Count);

		// Running the queued dispatch redraws once and opens the gate for the next burst.
		queuedRedraws[0]();
		listener.ModelTokensChanged(tokenChange);

		Assert.AreEqual(1, redrawCount);
		Assert.AreEqual(2, queuedRedraws.Count);

		// A disposed transformer neither dispatches nor redraws an already queued dispatch.
		host.Transformer.Dispose();
		queuedRedraws[1]();
		listener.ModelTokensChanged(tokenChange);

		Assert.AreEqual(1, redrawCount);
		Assert.AreEqual(2, queuedRedraws.Count);
	}

	[TestMethod]
	public void ModelTokensChanged_QueueRedrawThrows_ResetsGateAndAllowsNextDispatch()
	{
		List<Action> queuedRedraws = [];
		int queueAttempts = 0;

		using var host = TextMateColorizingTransformerHost.Create(
			queueRedraw: action =>
			{
				// Simulate a text view whose dispatcher has already shut down.
				if (queueAttempts++ == 0)
					throw new InvalidOperationException("The dispatcher has shut down.");

				queuedRedraws.Add(action);
				return null;
			},
			redrawRange: (_, _) => { });

		var listener = (IModelTokensChangedListener)host.Transformer;
		var tokenChange = new ModelTokensChangedEvent([], host.Model);

		// The failed dispatch must not escape and must not leave the coalescing gate closed.
		listener.ModelTokensChanged(tokenChange);

		Assert.AreEqual(0, queuedRedraws.Count);

		listener.ModelTokensChanged(tokenChange);

		Assert.AreEqual(1, queuedRedraws.Count);
	}

	[TestMethod]
	public void ModelTokensChanged_UntrackableQueue_ReopensGateForLaterNotifications()
	{
		int queueAttempts = 0;

		using var host = TextMateColorizingTransformerHost.Create(
			queueRedraw: _ =>
			{
				// A queue that cannot hand back a trackable operation must not leave the coalescing gate
				// closed, or no later token-change notification could ever queue a redraw and the view
				// would freeze on its last paint.
				queueAttempts++;
				return null;
			},
			redrawRange: (_, _) => { });

		var listener = (IModelTokensChangedListener)host.Transformer;
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

		using var host = TextMateColorizingTransformerHost.Create(
			queueRedraw: action =>
			{
				queueAttempts++;

				// The first queue attempt hands out a real pending dispatcher operation that the
				// test aborts before the dispatcher can run it, which is what a dispatcher
				// shutdown after acceptance produces; later attempts are merely recorded.
				if (pendingOperation is null)
				{
#if AVALONIAEDIT
					pendingOperation = Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Background);
#else
					pendingOperation = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, action);
#endif
					return pendingOperation;
				}

				queuedRedraws.Add(action);
				return null;
			},
			redrawRange: (_, _) => redrawCount++);

		var listener = (IModelTokensChangedListener)host.Transformer;
		var tokenChange = new ModelTokensChangedEvent([], host.Model);

		// The queued redraw is aborted while still pending, so its callback never runs.
		listener.ModelTokensChanged(tokenChange);
		pendingOperation!.Abort();

		// The abort reopened the coalescing gate, so the next notification queues a redraw again.
		listener.ModelTokensChanged(tokenChange);

		Assert.AreEqual(2, queueAttempts);
		Assert.AreEqual(1, queuedRedraws.Count);
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

		using var host = TextMateColorizingTransformerHost.Create(
			queueRedraw: action =>
			{
				queuedRedraws.Add(action);
				return PendingNoOpOperation();
			},
			redrawRange: (firstLineIndex, lastLineIndex) => redrawRanges.Add((firstLineIndex, lastLineIndex)));

		var listener = (IModelTokensChangedListener)host.Transformer;

		// An unqueued burst widens its pending range to the union of the reported changed lines;
		// model ranges carry one-based line numbers, so line 3 becomes index 2.
		listener.ModelTokensChanged(new ModelTokensChangedEvent([new TextMateSharp.Model.Range(3, 5)], host.Model));
		listener.ModelTokensChanged(new ModelTokensChangedEvent([new TextMateSharp.Model.Range(7, 9)], host.Model));

		Assert.AreEqual(1, queuedRedraws.Count);

		queuedRedraws[0]();

		Assert.AreEqual(1, redrawRanges.Count);
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

		using var host = TextMateColorizingTransformerHost.Create(
			queueRedraw: action =>
			{
				queuedRedraws.Add(action);
				return PendingNoOpOperation();
			},
			redrawRange: (firstLineIndex, lastLineIndex) => redrawRanges.Add((firstLineIndex, lastLineIndex)));

		var listener = (IModelTokensChangedListener)host.Transformer;

		// TextMateSharp's SetGrammar emits zero-based model ranges, so a zero FromLineNumber is a valid
		// first line; it must not become a negative index that under-covers the redraw.
		listener.ModelTokensChanged(new ModelTokensChangedEvent([new TextMateSharp.Model.Range(0, 2)], host.Model));
		queuedRedraws[0]();

		Assert.AreEqual(1, redrawRanges.Count);
		Assert.AreEqual((0, int.MaxValue), redrawRanges[0]);
	}

	[TestMethod]
	public void ColorizeLine_WithTraitRule_AppliesTypefaceAndDecorations()
	{
		using var host = TextMateColorizingTransformerHost.Create(
			grammarLanguageId: "lua",
			rules:
			[
				// Matches the root Lua scope present on every token, so the assertions do not depend
				// on grammar details.
				new TextMateTokenThemeRule
				{
					Scope = "source.lua",
					Foreground = "#FF0000",
					FontStyle = "bold italic underline strikethrough"
				}
			],
			hostInWindow: true);

		WaitForTokenization(host.Model, lineIndex: 0);
		VisualLine visualLine = GetPaintedVisualLine(host.Editor);

		bool sawTraits = false;

		foreach (VisualLineElement element in visualLine.Elements)
		{
			VisualLineElementTextRunProperties properties = element.TextRunProperties;

			if (properties.Typeface.Weight == FontWeights.Bold
				&& properties.Typeface.Style == FontStyles.Italic
				&& properties.TextDecorations is { Count: 2 })
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
		using var host = TextMateColorizingTransformerHost.Create(
			grammarLanguageId: "lua",
			rules: [new TextMateTokenThemeRule { Scope = "source.lua", Foreground = "#FF0000" }],
			hostInWindow: true);

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
		using var host = TextMateColorizingTransformerHost.Create(
			grammarLanguageId: "lua",
			rules: [new TextMateTokenThemeRule { Scope = "source.lua", Foreground = "#FF0000" }],
			hostInWindow: true);

		WaitForTokenization(host.Model, lineIndex: 0);

		Assert.IsTrue(HasThemedElement(GetPaintedVisualLine(host.Editor), Color.FromRgb(0xFF, 0x00, 0x00)));

		host.Transformer.Dispose();

		// A disposed transformer never applies styles again, even while the tokens are still
		// available; the paint early-returns for a disposed transformer.
		Assert.IsFalse(HasThemedElement(GetPaintedVisualLine(host.Editor), Color.FromRgb(0xFF, 0x00, 0x00)));
	}

	/// <summary>
	/// Hands the transformer a real, still-pending dispatcher operation so the coalescing gate stays
	/// closed until the test drives the captured action itself. A <see langword="null"/> return now
	/// reopens the gate immediately, so coalescing is observable only through a trackable operation.
	/// The operation carries a no-op: the captured action is what the test runs, and running it here
	/// as well would double-count redraws.
	/// </summary>
	private static DispatcherOperation PendingNoOpOperation()
#if AVALONIAEDIT
		=> Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
#else
		=> Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { }));
#endif
}
