#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.AutoClosing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Editing;
using Nickelony.IDEKit.Core.AutoClosing;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionInputTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.AutoClosing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Editing;
using Nickelony.IDEKit.Core.AutoClosing;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionInputTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <remarks>
/// The editors are never shown in a window, so the class also runs in a headless leg.
/// </remarks>
[STATestClass]
public sealed class TypedTextSubscriptionTests
{
	private static readonly AutoClosingOptions s_parentheses = AutoClosingOptions.Default with { Pairs = [AutoClosingPair.Parentheses] };

	[TestMethod]
	public void TypedCharacter_IsReportedAfterTheInputApplied_NotDuringIt()
	{
		TextEditor editor = TestHost.CreateEditor("pr");
		editor.CaretOffset = 2;
		List<(char Character, int CaretOffset, string Text)> reports = [];

		using var subscription = new TypedTextSubscription(editor.TextArea, (typed, caret) => reports.Add((typed, caret, editor.Text)));

		TypeThroughInputRoute(editor, "i");

		// The report is posted, so nothing is reported while the input runs.
		Assert.IsEmpty(reports);

		DispatcherTestHelpers.PumpUntilIdle();

		Assert.HasCount(1, reports);
		Assert.AreEqual(('i', 3, "pri"), reports[0]);
	}

	[TestMethod]
	public void ProgrammaticInput_IsReported()
	{
		TextEditor editor = TestHost.CreateEditor(string.Empty);
		List<char> reports = [];

		using var subscription = new TypedTextSubscription(editor.TextArea, (typed, _) => reports.Add(typed));

		editor.TextArea.PerformTextInput(".");
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.AreSequenceEqual(['.'], reports);
	}

	[TestMethod]
	public void AutoClosedBracket_ThatAHandlerAppliedAndHandled_IsReported()
	{
		TextEditor editor = TestHost.CreateEditor("print");
		editor.CaretOffset = 5;
		var autoClosing = new AutoClosingService();
		List<(char Character, int CaretOffset)> reports = [];

		// The service subscribes first, so it applies the pair and marks the input handled before the subscription's own TextEntering stage.
		editor.TextArea.TextEntering += (_, e) => autoClosing.ApplyTextEntering(editor, e, s_parentheses);

		using var subscription = new TypedTextSubscription(editor.TextArea, (typed, caret) => reports.Add((typed, caret)));

		TypeThroughInputRoute(editor, "(");
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.AreEqual("print()", editor.Text);
		Assert.AreSequenceEqual([('(', 6)], reports);
	}

	[TestMethod]
	public void ProgrammaticInput_ThatAnEarlierTextEnteringHandlerApplied_IsNotReported()
	{
		TextEditor editor = TestHost.CreateEditor("print");
		editor.CaretOffset = 5;
		var autoClosing = new AutoClosingService();
		List<char> reports = [];

		// Programmatic input reaches no preview stage, so the subscription's baseline is taken on TextEntering, after the service changed the
		// document: the documented limit of programmatic input.
		editor.TextArea.TextEntering += (_, e) => autoClosing.ApplyTextEntering(editor, e, s_parentheses);

		using var subscription = new TypedTextSubscription(editor.TextArea, (typed, _) => reports.Add(typed));

		editor.TextArea.PerformTextInput("(");
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.AreEqual("print()", editor.Text);
		Assert.IsEmpty(reports);
	}

	[TestMethod]
	public void OvertypeSkip_ThatOnlyMovesTheCaret_IsReported()
	{
		TextEditor editor = TestHost.CreateEditor("print");
		editor.CaretOffset = 5;
		var autoClosing = new AutoClosingService();
		editor.TextArea.TextEntering += (_, e) => autoClosing.ApplyTextEntering(editor, e, s_parentheses);
		List<(char Character, int CaretOffset)> reports = [];

		using var subscription = new TypedTextSubscription(editor.TextArea, (typed, caret) => reports.Add((typed, caret)));

		TypeThroughInputRoute(editor, "(");
		DispatcherTestHelpers.PumpUntilIdle();
		TypeThroughInputRoute(editor, ")");
		DispatcherTestHelpers.PumpUntilIdle();

		// The closing parenthesis skipped the one the pair inserted, so the text is unchanged and only the caret moved.
		Assert.AreEqual("print()", editor.Text);
		Assert.AreSequenceEqual([('(', 6), (')', 7)], reports);
	}

	[TestMethod]
	public void InputThatAHandlerConsumedWithoutApplying_IsNotReported()
	{
		// The character before the caret is the typed one, so only the unchanged document and caret tell that nothing applied.
		TextEditor editor = TestHost.CreateEditor("a");
		editor.CaretOffset = 1;
		editor.TextArea.TextEntering += static (_, e) => e.Handled = true;
		List<char> reports = [];

		using var subscription = new TypedTextSubscription(editor.TextArea, (typed, _) => reports.Add(typed));

		TypeThroughInputRoute(editor, "a");
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.AreEqual("a", editor.Text);
		Assert.IsEmpty(reports);
	}

	[TestMethod]
	public void WrapOfASelection_IsNotReported()
	{
		TextEditor editor = TestHost.CreateEditor("name");
		var autoClosing = new AutoClosingService();
		editor.TextArea.TextEntering += (_, e) => autoClosing.ApplyTextEntering(editor, e, s_parentheses);
		editor.Select(0, 4);
		List<char> reports = [];

		using var subscription = new TypedTextSubscription(editor.TextArea, (typed, _) => reports.Add(typed));

		TypeThroughInputRoute(editor, "(");
		DispatcherTestHelpers.PumpUntilIdle();

		// The wrap leaves the caret after the wrapped text, so the character before it is not the typed one.
		Assert.AreEqual("(name)", editor.Text);
		Assert.IsEmpty(reports);
	}

	[TestMethod]
	public void MultiCharacterInput_IsNotReported()
	{
		TextEditor editor = TestHost.CreateEditor(string.Empty);
		List<char> reports = [];

		using var subscription = new TypedTextSubscription(editor.TextArea, (typed, _) => reports.Add(typed));

		TypeThroughInputRoute(editor, "ab");
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.AreEqual("ab", editor.Text);
		Assert.IsEmpty(reports);
	}

	[TestMethod]
	public void InputsBeforeThePostRuns_AreReportedOnceForTheLastCharacter()
	{
		TextEditor editor = TestHost.CreateEditor(string.Empty);
		List<(char Character, int CaretOffset)> reports = [];

		using var subscription = new TypedTextSubscription(editor.TextArea, (typed, caret) => reports.Add((typed, caret)));

		TypeThroughInputRoute(editor, "a");
		TypeThroughInputRoute(editor, ".");

		DispatcherTestHelpers.PumpUntilIdle();

		Assert.AreSequenceEqual([('.', 2)], reports);
	}

	[TestMethod]
	public void Dispose_BeforeThePostRuns_ReportsNothing()
	{
		TextEditor editor = TestHost.CreateEditor(string.Empty);
		List<char> reports = [];

		var subscription = new TypedTextSubscription(editor.TextArea, (typed, _) => reports.Add(typed));

		TypeThroughInputRoute(editor, "a");
		subscription.Dispose();
		DispatcherTestHelpers.PumpUntilIdle();

		TypeThroughInputRoute(editor, "b");
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.AreEqual("ab", editor.Text);
		Assert.IsEmpty(reports);
	}
}
