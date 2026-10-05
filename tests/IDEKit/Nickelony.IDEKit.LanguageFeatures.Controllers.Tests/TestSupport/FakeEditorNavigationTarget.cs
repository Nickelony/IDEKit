using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// An editor for a quick-pick mode that moves the caret: a document of lines with given lengths, a caret, and a log of the calls the mode made.
/// </summary>
/// <remarks>
/// A move puts the caret where it is told, and saving and restoring the view keep the caret of the moment of the save, so a test reads
/// where the caret is and whether the mode restored it.
/// </remarks>
internal sealed class FakeEditorNavigationTarget(params int[] lineLengths) : IEditorNavigationTarget
{
	private (int Line, int Column)? _saved;

	/// <summary>
	/// The calls in order: <c>move:</c> with the line and the column, <c>select:</c> with the range, <c>save</c>, or <c>restore</c>.
	/// </summary>
	public List<string> Log { get; } = [];

	public int LineCount => lineLengths.Length;

	public int CaretLineNumber { get; private set; } = 1;

	public int CaretColumn { get; private set; } = 1;

	public int GetLineLength(int lineNumber) => lineLengths[lineNumber - 1];

	public void MoveCaret(int lineNumber, int column)
	{
		Log.Add($"move:{lineNumber}:{column}");

		CaretLineNumber = lineNumber;
		CaretColumn = column;
	}

	public void SelectRange(TextPositionRange range)
	{
		Log.Add($"select:{range.Start.Line}:{range.Start.Character}-{range.End.Line}:{range.End.Character}");

		CaretLineNumber = range.End.Line + 1;
		CaretColumn = range.End.Character + 1;
	}

	public void SaveView()
	{
		Log.Add("save");
		_saved = (CaretLineNumber, CaretColumn);
	}

	public void RestoreView()
	{
		Log.Add("restore");

		if (_saved is { } saved)
			(CaretLineNumber, CaretColumn) = saved;
	}
}
