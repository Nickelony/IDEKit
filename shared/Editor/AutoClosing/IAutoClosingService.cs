using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using Avalonia.Input;
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Text;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Text;
using System.Windows.Input;
using TextInputEventArgs = System.Windows.Input.TextCompositionEventArgs;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.AutoClosing;

#else

namespace Nickelony.IDEKit.AvalonEdit.AutoClosing;

#endif

/// <summary>Provides the auto-closing operations for an individual editor.</summary>
/// <remarks>
/// <see cref="AutoClosingService"/> is the default implementation; compose one instance per editor, and insertion tracking is kept per document,
/// so different documents never share tracking state. The applying members accept the same optional <see cref="ITextEditTarget"/>
/// as the other editing helpers and apply the typed pair (or the deleted pair) through it as one batch, so a single undo removes the pair;
/// a skip action only moves the caret.
/// </remarks>
public interface IAutoClosingService
{
	/// <summary>Tries to resolve an auto-closing action for <paramref name="inputText"/> at the caret.</summary>
	/// <param name="snapshot">The document snapshot containing the caret.</param>
	/// <param name="caretOffset">The zero-based caret offset. Offsets outside the snapshot are clamped to its bounds.</param>
	/// <param name="inputText">The text being entered.</param>
	/// <param name="options">The auto-closing configuration.</param>
	/// <param name="action">
	/// The resolved action when the method returns <see langword="true"/>; otherwise, the <see langword="default"/> action,
	/// whose kind is <see cref="AutoClosingActionKind.None"/>.
	/// </param>
	/// <returns><see langword="true"/> when an action applies; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="snapshot"/>, <paramref name="inputText"/>, or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// Auto-closing text counts only when it was tracked: tracking is consulted only when <paramref name="snapshot"/>
	/// is a <see cref="TextDocumentSnapshot"/> of a document this service has recorded insertions for. Any other snapshot
	/// has no tracked closing text, so no action qualifies as <see cref="AutoClosingTrackingMode.TrackedOnly"/>. The method takes these values
	/// instead of an <see cref="AutoClosingRequest"/>, because the request carries the tracking callback that this service owns.
	/// </remarks>
	bool TryPlanAction(ITextSnapshot snapshot, int caretOffset, string inputText, AutoClosingOptions options, out AutoClosingAction action);

	/// <summary>Applies the auto-closing action (if any) for text entering the editor.</summary>
	/// <param name="editor">The editor receiving the text.</param>
	/// <param name="e">The text-input event being handled.</param>
	/// <param name="options">The auto-closing configuration.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the applied pair is routed, or <see langword="null"/> to edit the editor's document directly. A supplied target
	/// must satisfy the edit-target contract described by <see cref="ITextEditTarget"/>.
	/// </param>
	/// <returns>The applied action and whether it wrapped the selection; <see cref="AutoClosingResult.None"/> when no action applied.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="editor"/>, <paramref name="e"/>, or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// An event that is already handled, or an editor without a document, is ignored and reports <see cref="AutoClosingResult.None"/>.
	/// An insert action applies the typed text and the closing text as one edit through <paramref name="editTarget"/>
	/// when one is supplied, and marks the event handled. A refused insertion at the caret, which the editor's read-only section provider decides,
	/// leaves the event unhandled. Over a non-empty selection, the action wraps the selected text and keeps it selected. A whitespace-only selection,
	/// a selection that only repeats the typed quote text, or a selection the provider will not replace wholesale falls through to normal text input.
	/// A skip action moves the caret past existing closing text and marks the event handled without changing text or involving a target,
	/// and it is ignored when a selection is active. Because a handled event raises no <c>TextEntered</c>, a host that reacts to typed characters,
	/// such as a completion or signature-help trigger, also reacts to a result whose action is not <see cref="AutoClosingActionKind.None"/>.
	/// </remarks>
	AutoClosingResult ApplyTextEntering(TextEditor editor, TextInputEventArgs e, AutoClosingOptions options, ITextEditTarget? editTarget = null);

	/// <summary>Deletes the auto-closing pair around the caret for a plain Backspace press.</summary>
	/// <param name="editor">The editor receiving the deletion.</param>
	/// <param name="e">The keyboard event of the Backspace press.</param>
	/// <param name="options">The auto-closing configuration.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the pair deletion is routed, or <see langword="null"/> to edit the editor's document directly.
	/// A supplied target must satisfy the edit-target contract described by <see cref="ITextEditTarget"/>.
	/// </param>
	/// <returns><see langword="true"/> when the pair was removed and <paramref name="e"/> was handled; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="editor"/>, <paramref name="e"/>, or <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// Call this from a tunneling key-down handler (<c>PreviewKeyDown</c> on WPF, a <c>KeyDown</c> handler on the tunneling route on Avalonia),
	/// because the text area marks Backspace handled in its own bubbling handling and the method then does nothing. When the opening
	/// token ends at the caret and its closing text starts there, the whole pair is removed as one edit through <paramref name="editTarget"/>
	/// when one is supplied and the event is marked handled. <see cref="AutoClosingOptions.PairDeletionTracking"/> decides whether a pair qualifies,
	/// and <see cref="AutoClosingPlanner"/> resolves the pair matching and the tracking check against a document snapshot.
	/// The method does nothing when the event is already handled, the pressed key is not Backspace, a control or alt modifier is held,
	/// the editor has no document, the editor has an active selection, or the editor's read-only section provider refuses the deletion.
	/// </remarks>
	bool TryDeletePair(TextEditor editor, KeyEventArgs e, AutoClosingOptions options, ITextEditTarget? editTarget = null);
}
