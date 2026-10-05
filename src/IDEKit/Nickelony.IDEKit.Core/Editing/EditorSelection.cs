using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>The caret and the selection of an editor, as zero-based UTF-16 offsets into its text.</summary>
/// <param name="CaretOffset">The offset of the caret.</param>
/// <param name="Selection">The selected range, which is empty when nothing is selected.</param>
/// <remarks>
/// The caret does not have to sit at an end of the selection, so a host that keeps the two apart reads and writes both.
/// </remarks>
public readonly record struct EditorSelection(int CaretOffset, TextRange Selection);
