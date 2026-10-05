using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using AvaloniaEdit;
#else
using ICSharpCode.AvalonEdit;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Editing;

#else

namespace Nickelony.IDEKit.AvalonEdit.Editing;

#endif

/// <summary>Gives every line terminator of an editor's document one style.</summary>
public static class TextEditorLineTerminatorExtensions
{
	/// <summary>Replaces every line terminator of the editor's document that differs from <paramref name="style"/>.</summary>
	/// <param name="textEditor">The editor whose document is converted.</param>
	/// <param name="style">
	/// The style every line terminator takes: <see cref="LineTerminatorStyle.Lf"/>,
	/// <see cref="LineTerminatorStyle.CrLf"/> or <see cref="LineTerminatorStyle.Cr"/>.
	/// </param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly. A supplied target
	/// must hold the same content as the editor's document.
	/// </param>
	/// <returns><see langword="true"/> when any line terminator changed; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="style"/> is <see cref="LineTerminatorStyle.Mixed"/> or <see cref="LineTerminatorStyle.None"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The editor has no document assigned, or a supplied target's content does not match the editor's document.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The conversion replaces only the line terminators, as one batch in one undo step, so the editor's own anchors keep the caret,
	/// the selection (a rectangle too) and the scroll position on their text. A caret at the end of a line stays there, and a caret at the start
	/// of the next line stays on it.
	/// </para>
	/// <para>
	/// The engine inserts the terminator of the caret's line for a new line, or of the line before it at the end of the text, so a converted document
	/// gets the style for every new line. A document without any line terminator has nothing to convert and gets <see cref="Environment.NewLine"/>
	/// for its first new line, whichever style the host recorded for it. A conversion that changes nothing leaves the document and its undo
	/// stack untouched.
	/// </para>
	/// </remarks>
	public static bool ConvertLineTerminators(this TextEditor textEditor, LineTerminatorStyle style, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		return TextEditorEditApplier.ApplyPlan(textEditor, snapshot => LineTerminatorPlanner.PlanConversion(snapshot, style), editTarget);
	}
}
