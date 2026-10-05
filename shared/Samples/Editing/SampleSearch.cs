using System.Text.RegularExpressions;
using Nickelony.IDEKit.Core.FindReplace;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Editing;
#endif

namespace Nickelony.Samples;

/// <summary>Finds and replaces text in one editor with the engine-free search operations of Core.</summary>
internal sealed class SampleSearch
{
	private readonly TextEditor _editor;

	/// <param name="editor">The editor whose text is searched.</param>
	internal SampleSearch(TextEditor editor) => _editor = editor;

	/// <summary>Selects the next match after the selection, wrapping to the start of the text, and reports whether a match exists.</summary>
	internal bool FindNext(SampleSearchOptions options)
	{
		SearchQuery query = options.ToQuery();
		string text = _editor.Text;
		int start = _editor.SelectionStart + _editor.SelectionLength;

		Match? match = FindReplaceOperations.FindNextMatch(text, start, query) ?? FindReplaceOperations.FindNextMatch(text, 0, query);

		return Select(match);
	}

	/// <summary>Selects the match before the selection, wrapping to the end of the text, and reports whether a match exists.</summary>
	internal bool FindPrevious(SampleSearchOptions options)
	{
		SearchQuery query = options.ToQuery();
		string text = _editor.Text;

		Match? match =
			FindReplaceOperations.FindPreviousMatch(text, _editor.SelectionStart, query)
			?? FindReplaceOperations.FindPreviousMatch(text, text.Length, query);

		return Select(match);
	}

	/// <summary>Returns the range of every match, in text order.</summary>
	internal IReadOnlyList<TextRange> FindAll(SampleSearchOptions options) => FindAll(_editor.Text, options);

	/// <summary>Returns the range of every match in a text, such as a file that no tab shows, in text order.</summary>
	internal static IReadOnlyList<TextRange> FindAll(string text, SampleSearchOptions options) =>
		[.. FindReplaceOperations.FindAllMatches(text, options.ToQuery()).Select(match => new TextRange(match.Index, match.Length))];

	/// <summary>Replaces every match as one undo step and returns how many matches it replaced.</summary>
	internal int ReplaceAll(SampleSearchOptions options, string replacement)
	{
		SearchQuery query = options.ToQuery();
		int count = FindReplaceOperations.CountMatches(_editor.Text, query);

		if (count > 0)
			_editor.ReplaceContent(FindReplaceOperations.ReplaceAll(_editor.Text, query, replacement));

		return count;
	}

	private bool Select(Match? match)
	{
		if (match is null)
			return false;

		_editor.Select(match.Index, match.Length);
		_editor.ScrollToLine(_editor.Document.GetLineByOffset(match.Index).LineNumber);

		return true;
	}
}
