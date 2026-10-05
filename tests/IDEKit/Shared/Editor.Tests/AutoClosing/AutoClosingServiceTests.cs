#if AVALONIAEDIT
using Avalonia;
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.AutoClosing;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Text;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Editing;
using static Nickelony.IDEKit.AvaloniaEdit.Tests.EditingTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.AutoClosing;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Text;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Editing;
using static Nickelony.IDEKit.AvalonEdit.Tests.EditingTestHelpers;
using KeyModifiers = System.Windows.Input.ModifierKeys;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>Verifies the editor application half of auto-closing above the Core resolver tests.</summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class AutoClosingServiceTests
{
	private static readonly AutoClosingOptions s_options = AutoClosingOptions.Default;

	private static readonly AutoClosingOptions s_alwaysOptions = AutoClosingOptions.Default with
	{
		ClosingTextSkipTracking = AutoClosingTrackingMode.Always,
		PairDeletionTracking = AutoClosingTrackingMode.Always,
	};

	private readonly AutoClosingService _service = new();

	[TestMethod]
	public void TryPlanAction_ClosingTextAfterInsertedPair_AutoOvertype_ReturnsSkipAction()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		// The pair is inserted through the service, so its closing text is tracked.
		AutoClosingResult insertResult = _service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);

		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, insertResult.Action.Kind);
		Assert.AreEqual("ab()", editor.Text);

		bool resolved = _service.TryPlanAction(
			snapshot: new TextDocumentSnapshot(editor.Document),
			caretOffset: 3,
			inputText: ")",
			options: s_options,
			out AutoClosingAction action
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, action.Kind);
	}

	[TestMethod]
	public void TryPlanAction_DoubleQuoteAfterInsertedQuotePair_SkipsTheTrackedClosingText()
	{
		var editor = new TextEditor { Document = new TextDocument("x = "), CaretOffset = 4 };

		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "\""), s_options);

		Assert.AreEqual("x = \"\"", editor.Text);

		// Typing the quote again over the tracked closing text skips it instead of doubling it.
		bool resolved = _service.TryPlanAction(
			snapshot: new TextDocumentSnapshot(editor.Document),
			caretOffset: 5,
			inputText: "\"",
			options: s_options,
			out AutoClosingAction action
		);

		Assert.IsTrue(resolved);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, action.Kind);
	}

	[TestMethod]
	public void ApplyTextEntering_InsertAction_InsertsTheWholePairAndHandlesTheEvent()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		var e = CreateTextArgs(editor, "(");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, result.Action.Kind);
		Assert.AreEqual(")", result.Action.ClosingText);
		Assert.IsFalse(result.DidWrapSelection);
		Assert.IsTrue(e.Handled);
		Assert.AreEqual("ab()", editor.Text);
		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyTextEntering_InsertAction_IsOneUndoUnit()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		var e = CreateTextArgs(editor, "(");

		_service.ApplyTextEntering(editor, e, s_options);

		Assert.AreEqual("ab()", editor.Text);

		// The typed text and the closing text were applied as one change, so a single undo removes
		// the whole pair.
		editor.Document.UndoStack.Undo();

		Assert.AreEqual("ab", editor.Text);
	}

	[TestMethod]
	public void ApplyTextEntering_WithSelection_WrapIsOneUndoUnit()
	{
		var editor = new TextEditor { Document = new TextDocument("abcd") };

		editor.Select(1, 2);

		var e = CreateTextArgs(editor, "(");

		_service.ApplyTextEntering(editor, e, s_options);

		Assert.AreEqual("a(bc)d", editor.Text);

		// The opening token, the wrapped text, and the closing text were applied as one change, so a
		// single undo removes the whole wrap.
		editor.Document.UndoStack.Undo();

		Assert.AreEqual("abcd", editor.Text);
	}

	[TestMethod]
	public void ApplyTextEntering_SkipAction_AdvancesCaretPastExistingClosingText()
	{
		var editor = new TextEditor { Document = new TextDocument("a)b"), CaretOffset = 1 };

		var e = CreateTextArgs(editor, ")");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_alwaysOptions);

		Assert.AreEqual(2, editor.CaretOffset);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, result.Action.Kind);
		Assert.AreEqual(")", result.Action.ClosingText);
		Assert.IsFalse(result.DidWrapSelection);
		Assert.AreEqual("a)b", editor.Text);
		Assert.IsTrue(e.Handled);
	}

	[TestMethod]
	public void ApplyTextEntering_NoAction_LeavesDocumentUnchanged()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 1 };

		var e = CreateTextArgs(editor, "x");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		Assert.AreEqual(AutoClosingResult.None, result);
		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(1, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyTextEntering_SkipMultiCharacterClosingText_AdvancesPastTheWholeClosingText()
	{
		var editor = new TextEditor { Document = new TextDocument("{},"), CaretOffset = 1 };

		var e = CreateTextArgs(editor, "}");

		AutoClosingOptions options = CreateOptions(new AutoClosingPair("{", "},")) with { ClosingTextSkipTracking = AutoClosingTrackingMode.Always };

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, options);

		Assert.AreEqual("{},", editor.Text);
		Assert.AreEqual(3, editor.CaretOffset);
		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, result.Action.Kind);
		Assert.AreEqual("},", result.Action.ClosingText);
		Assert.IsTrue(e.Handled);
	}

	[TestMethod]
	[DataRow("(", ")", "a(bc)d", DisplayName = "Parenthesis")]
	[DataRow("\"", "\"", "a\"bc\"d", DisplayName = "DoubleQuote")]
	[DataRow("'", "'", "a'bc'd", DisplayName = "SingleQuote")]
	public void ApplyTextEntering_WithSelection_OpeningToken_WrapsSelection(string input, string closingText, string expectedText)
	{
		var editor = new TextEditor { Document = new TextDocument("abcd") };

		editor.Select(1, 2);

		var e = CreateTextArgs(editor, input);

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		Assert.AreEqual(expectedText, editor.Text);
		Assert.AreEqual(2, editor.SelectionStart);
		Assert.AreEqual(2, editor.SelectionLength);
		Assert.AreEqual(4, editor.CaretOffset);
		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, result.Action.Kind);
		Assert.AreEqual(closingText, result.Action.ClosingText);
		Assert.IsTrue(result.DidWrapSelection);
		Assert.IsTrue(e.Handled);
	}

	[TestMethod]
	public void ApplyTextEntering_WithSelection_WrapPreservesLineBreaks()
	{
		var editor = new TextEditor { Document = new TextDocument("a\r\nb") };

		editor.Select(3, 1);

		var e = CreateTextArgs(editor, "(");

		_service.ApplyTextEntering(editor, e, s_options);

		Assert.AreEqual("a\r\n(b)", editor.Text);
		Assert.AreEqual(4, editor.SelectionStart);
		Assert.AreEqual(1, editor.SelectionLength);
		Assert.AreEqual(5, editor.CaretOffset);
		Assert.IsTrue(e.Handled);
	}

	[TestMethod]
	public void ApplyTextEntering_WithSelection_MultiCharacterClosingText_WrapsWithTheWholeClosingText()
	{
		var editor = new TextEditor { Document = new TextDocument("ab") };

		editor.Select(0, 2);

		var e = CreateTextArgs(editor, "{");
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("{", "},"));

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, options);

		Assert.AreEqual("{ab},", editor.Text);
		Assert.AreEqual(1, editor.SelectionStart);
		Assert.AreEqual(2, editor.SelectionLength);
		Assert.AreEqual(3, editor.CaretOffset);
		Assert.AreEqual("},", result.Action.ClosingText);
		Assert.IsTrue(result.DidWrapSelection);
		Assert.IsTrue(e.Handled);
	}

	[TestMethod]
	public void ApplyTextEntering_WithSelection_WrapDisabledPair_LeavesInputToNormalHandling()
	{
		var editor = new TextEditor { Document = new TextDocument("abcd") };

		editor.Select(1, 2);

		var e = CreateTextArgs(editor, "(");
		AutoClosingOptions options = CreateOptions(new AutoClosingPair("(", ")") { WrapSelection = false });

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, options);

		// The action is not resolved, so normal text input replaces the selection.
		Assert.AreEqual(AutoClosingResult.None, result);
		Assert.AreEqual("abcd", editor.Text);
		Assert.AreEqual(1, editor.SelectionStart);
		Assert.AreEqual(2, editor.SelectionLength);
		Assert.IsFalse(e.Handled);
	}

	[TestMethod]
	public void ApplyTextEntering_WithSelection_ClosingParenthesis_DoesNotSkipOrHandle()
	{
		var editor = new TextEditor { Document = new TextDocument("a)b") };

		editor.Select(0, 1);

		var e = CreateTextArgs(editor, ")");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		// Typing over a selection replaces it, so the existing closing text must not be skipped.
		Assert.AreEqual(AutoClosingResult.None, result);
		Assert.AreEqual("a)b", editor.Text);
		Assert.AreEqual(0, editor.SelectionStart);
		Assert.AreEqual(1, editor.SelectionLength);
		Assert.AreEqual(1, editor.CaretOffset);
		Assert.IsFalse(e.Handled);
	}

	[TestMethod]
	public void ApplyTextEntering_WithSelectionInsideReadOnlySection_LeavesInputToNormalHandling()
	{
		var editor = new TextEditor { Document = new TextDocument("abcd") };

		editor.Select(1, 2);

		var provider = new TextSegmentReadOnlySectionProvider<TextSegment>(editor.Document);
		provider.Segments.Add(new TextSegment { StartOffset = 0, EndOffset = 4 });
		editor.TextArea.ReadOnlySectionProvider = provider;

		var e = CreateTextArgs(editor, "(");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		// Wrapping replaces the whole selection, so a read-only selection is left to normal text input,
		// which applies the editor's read-only policy itself.
		Assert.AreEqual(AutoClosingResult.None, result);
		Assert.AreEqual("abcd", editor.Text);
		Assert.AreEqual(1, editor.SelectionStart);
		Assert.AreEqual(2, editor.SelectionLength);
		Assert.IsFalse(e.Handled);
	}

	[TestMethod]
	public void ApplyTextEntering_WithSelectionPartiallyReadOnly_LeavesInputToNormalHandling()
	{
		var editor = new TextEditor { Document = new TextDocument("abcd") };

		editor.Select(1, 2);

		// Only 'cd' is read-only, so the replacement would silently edit a part of the selection.
		var provider = new TextSegmentReadOnlySectionProvider<TextSegment>(editor.Document);
		provider.Segments.Add(new TextSegment { StartOffset = 2, EndOffset = 4 });
		editor.TextArea.ReadOnlySectionProvider = provider;

		var e = CreateTextArgs(editor, "(");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		Assert.AreEqual(AutoClosingResult.None, result);
		Assert.AreEqual("abcd", editor.Text);
		Assert.AreEqual(1, editor.SelectionStart);
		Assert.AreEqual(2, editor.SelectionLength);
		Assert.IsFalse(e.Handled);
	}

	[TestMethod]
	public void ApplyTextEntering_WithEditableSelectionOutsideReadOnlySection_WrapsSelection()
	{
		var editor = new TextEditor { Document = new TextDocument("abcdef") };

		editor.Select(1, 2);

		// The read-only section sits outside the selection, so the wrap applies as usual.
		var provider = new TextSegmentReadOnlySectionProvider<TextSegment>(editor.Document);
		provider.Segments.Add(new TextSegment { StartOffset = 4, EndOffset = 6 });
		editor.TextArea.ReadOnlySectionProvider = provider;

		var e = CreateTextArgs(editor, "(");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		Assert.AreEqual("a(bc)def", editor.Text);
		Assert.AreEqual(2, editor.SelectionStart);
		Assert.AreEqual(2, editor.SelectionLength);
		Assert.AreEqual(4, editor.CaretOffset);
		Assert.IsTrue(result.DidWrapSelection);
		Assert.IsTrue(e.Handled);
	}

	[TestMethod]
	public void TryDeletePair_BetweenAnInsertedPair_RemovesTheWholePair()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The pair is inserted through the service, so its closing text is tracked; the default Auto delete
		// mode removes exactly that pair as one change.
		AutoClosingResult insertResult = _service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);

		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, insertResult.Action.Kind);
		Assert.AreEqual("ab()", editor.Text);
		Assert.AreEqual(3, editor.CaretOffset);

		var e = CreateBackspaceArgs(hostWindow);

		bool handled = _service.TryDeletePair(editor, e, s_options);

		Assert.IsTrue(handled);
		Assert.IsTrue(e.Handled);
		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void TryDeletePair_BetweenALoadedPair_AutoDelete_LeavesTheDeletionToTheEditor()
	{
		// The pair was loaded, not inserted by the service, so the default TrackedOnly pair-deletion tracking mode declines and
		// the editor's own Backspace deletes a single character.
		var editor = new TextEditor { Document = new TextDocument("()"), CaretOffset = 1 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var e = CreateBackspaceArgs(hostWindow);

		bool handled = _service.TryDeletePair(editor, e, s_options);

		Assert.IsFalse(handled);
		Assert.IsFalse(e.Handled);
		Assert.AreEqual("()", editor.Text);
	}

	[TestMethod]
	public void TryDeletePair_BetweenALoadedPair_WithAlwaysDelete_RemovesTheWholePair()
	{
		var editor = new TextEditor { Document = new TextDocument("()"), CaretOffset = 1 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var e = CreateBackspaceArgs(hostWindow);

		bool handled = _service.TryDeletePair(editor, e, s_alwaysOptions);

		Assert.IsTrue(handled);
		Assert.IsTrue(e.Handled);
		Assert.AreEqual(string.Empty, editor.Text);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void TryDeletePair_NotBetweenAPair_ReturnsFalse()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 1 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var e = CreateBackspaceArgs(hostWindow);

		bool handled = _service.TryDeletePair(editor, e, s_options);

		Assert.IsFalse(handled);
		Assert.IsFalse(e.Handled);
		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(1, editor.CaretOffset);
	}

	[TestMethod]
	public void TryDeletePair_BetweenAPairWithMultiCharacterClosingText_RemovesTheWholePair()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		AutoClosingOptions options = CreateOptions(new AutoClosingPair("(", "},"));

		// The multi-character closing text is tracked because the pair was inserted through the service.
		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), options);

		Assert.AreEqual("ab(},", editor.Text);
		Assert.AreEqual(3, editor.CaretOffset);

		var e = CreateBackspaceArgs(hostWindow);

		bool handled = _service.TryDeletePair(editor, e, options);

		Assert.IsTrue(handled);
		Assert.IsTrue(e.Handled);
		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);

		// The whole multi-character pair is one change, so a single undo restores it.
		editor.Undo();

		Assert.AreEqual("ab(},", editor.Text);
	}

	[TestMethod]
	public void TryDeletePair_WithForwardSelection_ReturnsFalseWithoutTouchingTheDocument()
	{
		// The selection's end is the caret, so '(' to ')' resolves around the caret; the pair must not be
		// deleted because that would remove the unselected closing parenthesis as well. The editor's own
		// Backspace handling deletes the selection.
		var editor = new TextEditor { Document = new TextDocument("()"), CaretOffset = 0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.Select(0, 1);

		var e = CreateBackspaceArgs(hostWindow);

		bool handled = _service.TryDeletePair(editor, e, s_options);

		Assert.IsFalse(handled);
		Assert.IsFalse(e.Handled);
		Assert.AreEqual("()", editor.Text);
	}

	[TestMethod]
	public void TryDeletePair_WithSelectionCoveringThePair_ReturnsFalseWithoutTouchingTheDocument()
	{
		var editor = new TextEditor { Document = new TextDocument("x()y"), CaretOffset = 1 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.Select(1, 2);

		var e = CreateBackspaceArgs(hostWindow);

		bool handled = _service.TryDeletePair(editor, e, s_options);

		Assert.IsFalse(handled);
		Assert.IsFalse(e.Handled);
		Assert.AreEqual("x()y", editor.Text);
	}

	[TestMethod]
	public void ApplyTextEntering_PreHandledEvent_VetoesWithoutTouchingTheDocument()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		var e = CreateTextArgs(editor, "(");
		e.Handled = true;

		// Another subscriber (for example a completion list) already handled the input.
		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		Assert.AreEqual(AutoClosingResult.None, result);
		Assert.AreEqual("ab", editor.Text);
		Assert.IsTrue(e.Handled);
	}

	[TestMethod]
	public void ApplyTextEntering_ReadOnlySectionAtCaret_LeavesInputToNormalHandling()
	{
		var editor = new TextEditor { Document = new TextDocument("abc"), CaretOffset = 2 };

		var provider = new TextSegmentReadOnlySectionProvider<TextSegment>(editor.Document);
		provider.Segments.Add(new TextSegment { StartOffset = 0, EndOffset = 3 });
		editor.TextArea.ReadOnlySectionProvider = provider;

		var e = CreateTextArgs(editor, "(");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		// The editor's own read-only policy applies to the normal text input instead of the pair
		// being inserted through a direct document edit.
		Assert.AreEqual(AutoClosingResult.None, result);
		Assert.AreEqual("abc", editor.Text);
		Assert.IsFalse(e.Handled);
	}

	[TestMethod]
	public void ApplyTextEntering_WithWhitespaceOnlySelection_LeavesInputToNormalHandling()
	{
		var editor = new TextEditor { Document = new TextDocument("a b") };

		editor.Select(1, 1);

		var e = CreateTextArgs(editor, "(");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		// Wrapping whitespace would only add noise, so normal text input replaces the selection.
		Assert.AreEqual(AutoClosingResult.None, result);
		Assert.AreEqual("a b", editor.Text);
		Assert.AreEqual(1, editor.SelectionStart);
		Assert.AreEqual(1, editor.SelectionLength);
		Assert.IsFalse(e.Handled);
	}

	[TestMethod]
	public void ApplyTextEntering_WithSelectionEqualToTypedQuote_LeavesInputToNormalHandling()
	{
		var editor = new TextEditor { Document = new TextDocument("x\"y") };

		editor.Select(1, 1);

		var e = CreateTextArgs(editor, "\"");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		// Replacing a lone quote with the same quote must not wrap it into a doubled quote pair.
		Assert.AreEqual(AutoClosingResult.None, result);
		Assert.AreEqual("x\"y", editor.Text);
		Assert.IsFalse(e.Handled);

		// Control: the same geometry wraps when the selection is a different character, so the guard
		// above is what declined the equal-quote case.
		editor.Select(1, 1);
		editor.Document.Replace(1, 1, "q");

		AutoClosingResult wrapped = _service.ApplyTextEntering(editor, CreateTextArgs(editor, "\""), s_options);

		Assert.AreEqual("x\"q\"y", editor.Text);
		Assert.IsTrue(wrapped.DidWrapSelection);
	}

	[TestMethod]
	public void ApplyTextEntering_ClosingTextAfterDidWrapSelection_SkipsTheTrackedClosingText()
	{
		var editor = new TextEditor { Document = new TextDocument("ab") };

		editor.Select(0, 2);
		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);

		Assert.AreEqual("(ab)", editor.Text);

		// The closing text inserted by the wrap is tracked, so typing it again at the caret skips it.
		editor.Select(3, 0);

		var e = CreateTextArgs(editor, ")");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, result.Action.Kind);
		Assert.AreEqual("(ab)", editor.Text);
		Assert.AreEqual(4, editor.CaretOffset);
		Assert.IsTrue(e.Handled);
	}

	[TestMethod]
	public void TryDeletePair_AfterDidWrapSelection_RemovesTheTrackedPair()
	{
		var editor = new TextEditor { Document = new TextDocument("x") };

		editor.Select(0, 1);
		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);

		Assert.AreEqual("(x)", editor.Text);

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The host removes the wrapped content; the tracked closing text follows the edit, so the
		// default TrackedOnly pair-deletion tracking mode still recognizes the pair.
		editor.Document.Remove(1, 1);
		editor.CaretOffset = 1;

		var e = CreateBackspaceArgs(hostWindow);

		bool handled = _service.TryDeletePair(editor, e, s_options);

		Assert.IsTrue(handled);
		Assert.IsTrue(e.Handled);
		Assert.IsEmpty(editor.Text);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void TryDeletePair_PreHandledEventOrNonBackKey_ReturnsFalseWithoutTouchingTheDocument()
	{
		var editor = new TextEditor { Document = new TextDocument("()"), CaretOffset = 1 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var preHandled = CreateBackspaceArgs(hostWindow);
		preHandled.Handled = true;

		Assert.IsFalse(_service.TryDeletePair(editor, preHandled, s_alwaysOptions));

		var deleteKey = CreateKeyArgs(hostWindow, Key.Delete);

		Assert.IsFalse(deleteKey.Handled);
		Assert.AreEqual("()", editor.Text);
	}

	[TestMethod]
	public void TryDeletePair_WithControlOrAltModifier_ReturnsFalseWithoutTouchingTheDocument()
	{
		var editor = new TextEditor { Document = new TextDocument("()"), CaretOffset = 1 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The Always pair-deletion tracking mode would remove the pair for an unmodified Backspace, so the modifier is
		// the only reason the deletion is left to the editor.
		KeyEventArgs controlBackspace = CreateBackspaceArgs(hostWindow, KeyModifiers.Control);
		KeyEventArgs altBackspace = CreateBackspaceArgs(hostWindow, KeyModifiers.Alt);

		Assert.IsFalse(_service.TryDeletePair(editor, controlBackspace, s_alwaysOptions));
		Assert.IsFalse(controlBackspace.Handled);
		Assert.IsFalse(_service.TryDeletePair(editor, altBackspace, s_alwaysOptions));
		Assert.IsFalse(altBackspace.Handled);
		Assert.AreEqual("()", editor.Text);
	}

	[TestMethod]
	public void TryDeletePair_InsideReadOnlySection_ReturnsFalseWithoutTouchingTheDocument()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The pair is inserted through the service, so its closing text is tracked; the read-only
		// section covering it must still refuse the deletion.
		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);

		Assert.AreEqual("ab()", editor.Text);

		var provider = new TextSegmentReadOnlySectionProvider<TextSegment>(editor.Document);
		provider.Segments.Add(new TextSegment { StartOffset = 2, EndOffset = 4 });
		editor.TextArea.ReadOnlySectionProvider = provider;

		var e = CreateBackspaceArgs(hostWindow);

		bool handled = _service.TryDeletePair(editor, e, s_options);

		Assert.IsFalse(handled);
		Assert.IsFalse(e.Handled);
		Assert.AreEqual("ab()", editor.Text);
	}

	[TestMethod]
	public void ApplyTextEntering_WithEditTarget_AppliesThePairThroughTheTarget()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		var target = new RecordingEditTarget("ab");
		var e = CreateTextArgs(editor, "(");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, options: s_options, editTarget: target);

		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, result.Action.Kind);
		Assert.IsTrue(e.Handled);
		Assert.AreEqual(1, target.ApplyCalls);
		Assert.HasCount(1, target.Operations);

		TextEditOperation operation = target.Operations[0];

		Assert.AreEqual(2, operation.Range.Offset);
		Assert.AreEqual(2, operation.Range.EndOffset);
		Assert.AreEqual("()", operation.NewText);

		// The target deliberately does not update the editor document, so the pair is not in the
		// document and the caret is clamped against the stale document length.
		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyTextEntering_WithFakeEditTarget_IsOneUndoUnit()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		var target = new FakeEditTarget(editor);
		var e = CreateTextArgs(editor, "(");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, options: s_options, editTarget: target);

		Assert.AreEqual(AutoClosingActionKind.InsertClosingText, result.Action.Kind);
		Assert.AreEqual("ab()", editor.Text);
		Assert.AreEqual("ab()", target.Text);
		Assert.AreEqual(3, editor.CaretOffset);

		// The contract-honoring target publishes the pair before returning, so a single undo removes it.
		editor.Document.UndoStack.Undo();

		Assert.AreEqual("ab", editor.Text);
	}

	[TestMethod]
	public void ApplyTextEntering_WithSelection_WithFakeEditTarget_WrapsThroughTheTarget()
	{
		var editor = new TextEditor { Document = new TextDocument("ab") };

		editor.Select(0, 2);

		var target = new FakeEditTarget(editor);
		var e = CreateTextArgs(editor, "(");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, options: s_options, editTarget: target);

		Assert.IsTrue(result.DidWrapSelection);
		Assert.HasCount(1, target.Operations);

		TextEditOperation operation = target.Operations[0];

		Assert.AreEqual(0, operation.Range.Offset);
		Assert.AreEqual(2, operation.Range.EndOffset);
		Assert.AreEqual("(ab)", operation.NewText);

		Assert.AreEqual("(ab)", editor.Text);
		Assert.AreEqual(1, editor.SelectionStart);
		Assert.AreEqual(2, editor.SelectionLength);
		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyTextEntering_WithEditTarget_SkipAction_DoesNotApplyThroughTheTarget()
	{
		var editor = new TextEditor { Document = new TextDocument("a)b"), CaretOffset = 1 };

		var target = new RecordingEditTarget("a)b");
		var e = CreateTextArgs(editor, ")");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, options: s_alwaysOptions, editTarget: target);

		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, result.Action.Kind);
		Assert.AreEqual(2, editor.CaretOffset);
		Assert.IsTrue(e.Handled);

		// A skip changes no text, so the edit target is not involved.
		Assert.AreEqual(0, target.ApplyCalls);
	}

	[TestMethod]
	public void TryDeletePair_WithFakeEditTarget_RemovesThePairThroughTheTarget()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The pair is inserted through the service (no target), so its closing text is tracked; the
		// deletion is then routed through a contract target.
		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);

		Assert.AreEqual("ab()", editor.Text);

		var target = new FakeEditTarget(editor);
		var e = CreateBackspaceArgs(hostWindow);

		bool handled = _service.TryDeletePair(editor, e, options: s_options, editTarget: target);

		Assert.IsTrue(handled);
		Assert.IsTrue(e.Handled);
		Assert.HasCount(1, target.Operations);

		TextEditOperation operation = target.Operations[0];

		Assert.AreEqual(2, operation.Range.Offset);
		Assert.AreEqual(4, operation.Range.EndOffset);
		Assert.AreEqual(string.Empty, operation.NewText);

		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyTextEntering_TypedInsideTrackedPair_ThenClosing_SkipsExistingClosingText()
	{
		var editor = new TextEditor { Document = new TextDocument(), CaretOffset = 0 };

		// The pair is inserted through the service, so its closing text is tracked.
		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);

		// Typing inside the pair goes through normal text input: the service declines the input, and the
		// editor inserts the character at the caret with default anchor movement.
		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "x"), s_options);
		editor.Document.Insert(editor.CaretOffset, "x");
		editor.CaretOffset = 2;

		Assert.AreEqual("(x)", editor.Text);

		// The tracked closing text moved with the insertion; typing it must still skip it instead of
		// inserting a second closing text.
		var e = CreateTextArgs(editor, ")");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, result.Action.Kind);
		Assert.IsTrue(e.Handled);
		Assert.AreEqual("(x)", editor.Text);
		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	public void TryDeletePair_AfterTypingAndDeletingInsideTrackedPair_RemovesTheWholePair()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);

		// Typing inside the pair moves the tracked closing text; deleting the typed character moves the
		// tracking back with it, so the pair is still recognized for the pair deletion.
		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "x"), s_options);
		editor.Document.Insert(editor.CaretOffset, "x");
		editor.CaretOffset = 4;

		Assert.AreEqual("ab(x)", editor.Text);

		editor.Document.Remove(3, 1);
		editor.CaretOffset = 3;

		Assert.AreEqual("ab()", editor.Text);

		var e = CreateBackspaceArgs(hostWindow);

		bool handled = _service.TryDeletePair(editor, e, s_options);

		Assert.IsTrue(handled);
		Assert.IsTrue(e.Handled);
		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyTextEntering_TypedAndUndoneInsideTrackedPair_StillSkips()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);
		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "x"), s_options);

		editor.Document.Insert(editor.CaretOffset, "x");
		editor.CaretOffset = 4;

		Assert.AreEqual("ab(x)", editor.Text);

		// Undoing the typed character moves the tracked closing text back with the restored text.
		editor.Document.UndoStack.Undo();
		editor.CaretOffset = 3;

		Assert.AreEqual("ab()", editor.Text);

		var e = CreateTextArgs(editor, ")");

		AutoClosingResult result = _service.ApplyTextEntering(editor, e, s_options);

		Assert.AreEqual(AutoClosingActionKind.SkipExistingClosingText, result.Action.Kind);
		Assert.IsTrue(e.Handled);
		Assert.AreEqual("ab()", editor.Text);
		Assert.AreEqual(4, editor.CaretOffset);
	}

	[TestMethod]
	public void TryPlanAction_AfterUndoAndRedoOfTrackedPair_DoesNotSkip()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);

		Assert.AreEqual("ab()", editor.Text);

		// Undo removes the inserted pair and ends the tracking of its closing text.
		editor.Document.UndoStack.Undo();
		editor.Document.UndoStack.Redo();

		Assert.AreEqual("ab()", editor.Text);

		// The redo re-inserted the closing text, but the tracking state was released with the undo,
		// so the documented behavior is a normal insert instead of a skip.
		bool resolved = _service.TryPlanAction(
			snapshot: new TextDocumentSnapshot(editor.Document),
			caretOffset: 3,
			inputText: ")",
			options: s_options,
			out AutoClosingAction action
		);

		Assert.IsFalse(resolved);
		Assert.AreEqual(AutoClosingActionKind.None, action.Kind);
	}

	[TestMethod]
	public void Tracking_ConsumedClosingText_IsRetiredInsteadOfAccumulating()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		TextDocument document = editor.Document;

		// Each inserted pair registers one tracked closing text.
		for (int i = 0; i < 3; i++)
		{
			editor.CaretOffset = document.TextLength;
			_service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);
			Assert.AreEqual(i + 1, _service.GetTrackedClosingTextCount(document));
		}

		Assert.AreEqual("ab()()()", editor.Text);

		// Consuming the inserted closing texts retires their tracking instead of letting the list grow for the
		// whole document session.
		document.Text = string.Empty;

		Assert.AreEqual(0, _service.GetTrackedClosingTextCount(document));
	}

	[TestMethod]
	public void Tracking_InPlaceReplacedClosingText_IsRetired()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		TextDocument document = editor.Document;
		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);

		Assert.AreEqual("ab()", editor.Text);
		Assert.AreEqual(1, _service.GetTrackedClosingTextCount(document));

		// Replacing the closing text in place - the same offset now holds a different character - means the
		// tracked text is gone, so the entry is retired instead of lingering and being reported later.
		document.Replace(3, 1, "]");

		Assert.AreEqual("ab(]", editor.Text);
		Assert.AreEqual(0, _service.GetTrackedClosingTextCount(document));
	}

	[TestMethod]
	public void Tracking_BackspaceRemovedPair_IsRetired()
	{
		var editor = new TextEditor { Document = new TextDocument("ab"), CaretOffset = 2 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		TextDocument document = editor.Document;
		_service.ApplyTextEntering(editor, CreateTextArgs(editor, "("), s_options);

		Assert.AreEqual("ab()", editor.Text);
		Assert.AreEqual(1, _service.GetTrackedClosingTextCount(document));

		var e = CreateBackspaceArgs(hostWindow);

		Assert.IsTrue(_service.TryDeletePair(editor, e, s_options));
		Assert.AreEqual("ab", editor.Text);

		// Backspacing the whole pair removes the tracked closing text, so the entry is retired.
		Assert.AreEqual(0, _service.GetTrackedClosingTextCount(document));
	}

	private static AutoClosingOptions CreateOptions(params AutoClosingPair[] pairs) => new() { Pairs = pairs };
}
