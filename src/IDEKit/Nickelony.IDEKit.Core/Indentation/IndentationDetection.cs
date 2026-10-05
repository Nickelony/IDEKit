namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>The indentation a text uses, with a flag for each half that says whether the text showed it or a default supplied it.</summary>
/// <param name="InsertSpaces"><see langword="true"/> when one level is spaces; otherwise, <see langword="false"/> when one level is a tab.</param>
/// <param name="Size">The columns of one level: the number of spaces it inserts, or the width of a tab.</param>
/// <param name="InsertSpacesDetected">
/// <see langword="true"/> when the text showed <paramref name="InsertSpaces"/>; otherwise, the default supplied it.
/// </param>
/// <param name="SizeDetected"><see langword="true"/> when the text showed <paramref name="Size"/>; otherwise, the default supplied it.</param>
/// <remarks>
/// The two halves are detected independently, so a text can show its size and leave the style to the default,
/// as <see cref="IndentationDetector"/> describes. A host shows a default differently from a detected value.
/// </remarks>
public readonly record struct IndentationDetection(bool InsertSpaces, int Size, bool InsertSpacesDetected, bool SizeDetected);
