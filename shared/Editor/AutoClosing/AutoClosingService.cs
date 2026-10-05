using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Text;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using ModifierKeys = Avalonia.Input.KeyModifiers;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Text;
using Nickelony.IDEKit.AvalonEdit.Editing;
using System.Windows.Input;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using TextInputEventArgs = System.Windows.Input.TextCompositionEventArgs;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.AutoClosing;

#else

namespace Nickelony.IDEKit.AvalonEdit.AutoClosing;

#endif

/// <summary>
/// Applies auto-closing (including overtype), pair deletion, and selection wrapping to a <see cref="TextEditor"/> for the pairs configured
/// through <see cref="AutoClosingOptions"/>.
/// </summary>
/// <remarks>
/// Resolution is delegated
/// to the editor-neutral <see cref="AutoClosingPlanner"/>: this service snapshots the document, hands the snapshot and its insertion-tracking
/// callback to the resolver, and applies the resolved action as document edits; the pairs from <see cref="AutoClosingOptions.Pairs"/>
/// are evaluated in order, and the resolution gates are documented on <see cref="IAutoClosingService.TryPlanAction"/>.
/// The applying members accept the same optional <see cref="ITextEditTarget"/> as the other editing helpers.
/// They apply the typed text and the closing text, or the deleted pair, through the supplied target as one batch, so a single
/// undo removes or restores the whole pair. The caret and selection are applied to the editor's document. Typing existing closing text and Backspace
/// pair deletion follow <see cref="AutoClosingOptions.ClosingTextSkipTracking"/> and <see cref="AutoClosingOptions.PairDeletionTracking"/>.
/// Each defaults to <see cref="AutoClosingTrackingMode.TrackedOnly"/>, so they apply only to closing text this service
/// inserted and tracks per document. Text written through any other path is not tracked, so typing over it inserts normally and Backspace is left
/// to the editor.
/// </remarks>
public sealed class AutoClosingService : IAutoClosingService
{
	// The tracking state of the closing texts this service inserted, held per document; anchors keep the
	// offsets valid across edits, and the state (with the resolver callback bound to it) is released
	// with its document.
	private readonly AutoClosingInsertionTracker _tracking = new();

	/// <inheritdoc/>
	public bool TryPlanAction(ITextSnapshot snapshot, int caretOffset, string inputText, AutoClosingOptions options, out AutoClosingAction action)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(inputText);
		ArgumentNullException.ThrowIfNull(options);

		// Tracking is keyed by the source document; a snapshot captured from this service's document
		// exposes it, while any other snapshot has no tracked closing text.
		TextDocument? document = (snapshot as TextDocumentSnapshot)?.SourceDocument;

		return AutoClosingPlanner.TryPlanAction(
			new AutoClosingRequest(snapshot, caretOffset, inputText, options) { IsTrackedClosingText = _tracking.GetResolverCallback(document) },
			out action
		);
	}

	/// <inheritdoc/>
	public AutoClosingResult ApplyTextEntering(
		TextEditor editor,
		TextInputEventArgs e,
		AutoClosingOptions options,
		ITextEditTarget? editTarget = null
	)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(e);
		ArgumentNullException.ThrowIfNull(options);

		// Another TextEntering subscriber (for example a completion list) already handled the input.
		if (e.Handled)
			return AutoClosingResult.None;

		// An editor without a document has nothing to resolve against or to insert into, so the input
		// stays unhandled and normal text input applies.
		if (editor.Document is not { } document)
			return AutoClosingResult.None;

		// The editor carries the entered text on a nullable property, so a null value resolves no action.
#if AVALONIAEDIT
		if (e.Text is not { } inputText)
			return AutoClosingResult.None;
#else
		string inputText = e.Text;
#endif

		bool wrappingSelection = editor.SelectionLength > 0;

		if (
			!AutoClosingPlanner.TryPlanAction(
				new AutoClosingRequest(Snapshot: new TextDocumentSnapshot(document), CaretOffset: editor.CaretOffset, inputText, options)
				{
					IsWrappingSelection = wrappingSelection,
					IsTrackedClosingText = _tracking.GetResolverCallback(document),
				},

				out AutoClosingAction action
			)
		)
		{
			return AutoClosingResult.None;
		}

		return wrappingSelection
			? ApplySelectionAction(editor, e, inputText, action, editTarget)
			: ApplyAction(editor, e, inputText, action, editTarget);
	}

	/// <inheritdoc/>
	public bool TryDeletePair(TextEditor editor, KeyEventArgs e, AutoClosingOptions options, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(e);
		ArgumentNullException.ThrowIfNull(options);

		if (e.Handled || e.Key != Key.Back
#if AVALONIAEDIT
			|| (e.KeyModifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
#else
			|| (e.KeyboardDevice.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
#endif
		{
			return false;
		}

		// An active selection is deleted by the editor's own Backspace handling; resolving a pair around
		// the selection's end would delete text outside the selection.
		if (editor.SelectionLength > 0)
			return false;

		// An editor without a document has no pair to delete; the editor's own Backspace applies.
		if (editor.Document is not { } document)
			return false;

		int caretOffset = document.ClampOffset(editor.CaretOffset);

		// PairDeletionTracking decides which pairs qualify: under TrackedOnly, only a closing text this service
		// inserted is removed together with its opening token.
		if (
			!AutoClosingPlanner.TryPlanPairDeletion(
				new AutoClosingDeletionRequest(
					Snapshot: new TextDocumentSnapshot(document),
					caretOffset,
					options,
					IsTrackedClosingText: _tracking.GetResolverCallback(document)
				),

				out AutoClosingPair? pair
			)
		)
		{
			return false;
		}

		int deleteStart = caretOffset - pair.Open.Length;
		int deleteLength = pair.Open.Length + pair.Close.Length;

		if (!IsRangeEditable(editor, deleteStart, deleteLength))
			return false;

		// The whole pair travels as one edit, so a single undo restores it.
		TextEditorEditApplier.ApplySingleOperation(
			editor,
			new TextEditOperation(range: new TextRange(deleteStart, deleteLength), newText: string.Empty, editIndex: 0),
			editTarget
		);

		editor.Select(deleteStart, 0);

		e.Handled = true;
		return true;
	}

	/// <summary>Dispatches a resolved action to the matching applier, ignoring actions that carry no closing text.</summary>
	/// <param name="editor">The editor receiving the change.</param>
	/// <param name="e">The text-input event being handled.</param>
	/// <param name="inputText">The text the user is entering, already known to be non-<see langword="null"/>.</param>
	/// <param name="action">The action to apply.</param>
	/// <param name="editTarget">
	/// The host-owned target to which an applied pair is routed, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <returns>The applied result; <see cref="AutoClosingResult.None"/> when the action cannot be applied.</returns>
	private AutoClosingResult ApplyAction(
		TextEditor editor,
		TextInputEventArgs e,
		string inputText,
		AutoClosingAction action,
		ITextEditTarget? editTarget
	)
	{
		// Actions without closing text are ignored: None matches no case, and a malformed action
		// carries no text to apply.
		switch (action.Kind)
		{
			case AutoClosingActionKind.InsertClosingText:
				if (action.ClosingText is string closingText)
					return ApplyInsertAction(editor, e, inputText, closingText, editTarget);

				break;

			case AutoClosingActionKind.SkipExistingClosingText:
				if (action.ClosingText is string skippedText)
				{
					ApplySkipAction(editor, e, skippedText);
					return new AutoClosingResult(action, DidWrapSelection: false);
				}

				break;
		}

		return AutoClosingResult.None;
	}

	/// <summary>
	/// Inserts the typed text and the closing text as one edit (through the supplied target when one is provided),
	/// keeping the caret between them; with the built-in target the pair travels as one undo step, so a single undo removes or restores
	/// the whole pair.
	/// </summary>
	private AutoClosingResult ApplyInsertAction(
		TextEditor editor,
		TextInputEventArgs e,
		string inputText,
		string closingText,
		ITextEditTarget? editTarget
	)
	{
		// The caret offset is clamped to the document, matching the pair-deletion path: an offset left
		// outside the document must not reach the read-only check or the pending edit.
		int caretOffset = editor.Document.ClampOffset(editor.CaretOffset);

		// The editor refuses edits inside a read-only section; let normal text input apply the same
		// policy instead of inserting the pair through a direct document edit.
		if (!editor.TextArea.ReadOnlySectionProvider.CanInsert(caretOffset))
			return AutoClosingResult.None;

		// The typed text and the closing text are applied as one edit, so a single undo removes the pair.
		TextEditorEditApplier.ApplySingleOperation(
			editor,
			new TextEditOperation(range: new TextRange(caretOffset, 0), newText: inputText + closingText, editIndex: 0),
			editTarget
		);

		int documentLength = editor.Document.TextLength;
		int caretAfterPair = Math.Min(caretOffset + inputText.Length, documentLength);

		editor.Select(caretAfterPair, 0);

		// The inserted closing text is tracked so the default tracking mode can recognize it later,
		// but only when it is actually present: a supplied target that did not publish the edit into the
		// editor's document would otherwise register an anchor over text that is not there.
		if (DocumentTextProbe.MatchesAt(editor.Document, caretAfterPair, closingText))
			_tracking.Track(editor.Document, caretAfterPair, closingText);

		e.Handled = true;

		return new AutoClosingResult(AutoClosingAction.CreateInsert(closingText), DidWrapSelection: false);
	}

	/// <summary>Moves the caret past existing closing text and marks the event handled.</summary>
	/// <param name="editor">The editor receiving the skip.</param>
	/// <param name="e">The text-input event being handled.</param>
	/// <param name="skippedText">The existing closing text to move past.</param>
	private static void ApplySkipAction(TextEditor editor, TextInputEventArgs e, string skippedText)
	{
		editor.CaretOffset += skippedText.Length;
		e.Handled = true;
	}

	/// <summary>
	/// Applies an insert action to a non-empty selection by wrapping the selection in the typed opening token and the closing text,
	/// keeping the enclosed text selected.
	/// </summary>
	/// <remarks>
	/// A whitespace-only selection, or one that only repeats the typed quote, is replaced through normal text input
	/// instead of being wrapped, and a skip action over a selection is ignored so normal text input applies. A selection of other quote characters
	/// is still wrapped; a host that follows an editor disabling wrapping for cross-quote cases must apply that policy in its own selection handling.
	/// Wrapping replaces the whole selection, so it applies only when the editor's read-only section provider allows replacing the whole range,
	/// and the wrap travels as one edit through the supplied target, so a single undo removes it.
	/// </remarks>
	private AutoClosingResult ApplySelectionAction(
		TextEditor editor,
		TextInputEventArgs e,
		string inputText,
		AutoClosingAction action,
		ITextEditTarget? editTarget
	)
	{
		if (action.Kind != AutoClosingActionKind.InsertClosingText || action.ClosingText is not string closingText)
			return AutoClosingResult.None;

		string selectedText = editor.SelectedText;

		if (
			string.IsNullOrWhiteSpace(selectedText)
			|| (string.Equals(selectedText, inputText, StringComparison.Ordinal) && string.Equals(closingText, inputText, StringComparison.Ordinal))
		)
		{
			return AutoClosingResult.None;
		}

		int selectionStart = editor.SelectionStart;
		int selectionLength = editor.SelectionLength;

		if (!IsRangeEditable(editor, selectionStart, selectionLength))
			return AutoClosingResult.None;

		// The enclosed text stays selected, so consecutive wraps nest around it.
		TextEditorEditApplier.ApplySingleOperation(
			editor,

			new TextEditOperation(
				range: new TextRange(selectionStart, selectionLength),
				newText: inputText + selectedText + closingText,
				editIndex: 0
			),

			editTarget
		);

		int documentLength = editor.Document.TextLength;
		int wrappedStart = Math.Min(selectionStart + inputText.Length, documentLength);
		int wrappedLength = Math.Min(selectedText.Length, documentLength - wrappedStart);

		editor.Select(wrappedStart, wrappedLength);

		// The inserted closing text of the wrap is tracked the same way as a typed pair, and only when the
		// wrap is present in the editor's document (see the typed-pair path).
		if (DocumentTextProbe.MatchesAt(editor.Document, wrappedStart + wrappedLength, closingText))
			_tracking.Track(editor.Document, wrappedStart + wrappedLength, closingText);

		e.Handled = true;

		return new AutoClosingResult(AutoClosingAction.CreateInsert(closingText), DidWrapSelection: true);
	}

	/// <summary>Returns the number of closing texts currently tracked for <paramref name="document"/>.</summary>
	/// <param name="document">The document to inspect.</param>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// Stale entries are retired before the count is read, so the value reflects the closing texts that are still present.
	/// </remarks>
	internal int GetTrackedClosingTextCount(TextDocument document) => _tracking.GetTrackedCount(document);

	/// <summary>
	/// Determines whether the read-only section provider allows replacing the whole range, so pair deletion and selection wrapping never apply
	/// to a partially or fully read-only range.
	/// </summary>
	private static bool IsRangeEditable(TextEditor editor, int startOffset, int length)
	{
		ISegment rangeSegment = new TextSegment { StartOffset = startOffset, EndOffset = startOffset + length };

		foreach (ISegment deletable in editor.TextArea.ReadOnlySectionProvider.GetDeletableSegments(rangeSegment))
		{
			if (deletable.Offset == startOffset && deletable.EndOffset == startOffset + length)
				return true;
		}

		return false;
	}
}
