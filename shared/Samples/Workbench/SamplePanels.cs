using System.IO;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif

namespace Nickelony.Samples;

/// <summary>The rows of the lists of a window: the outline and the problems of the active pane, and what the last searches found.</summary>
/// <remarks>
/// Each window shows the rows in list controls of its toolkit and hands a chosen row to <see cref="SampleWorkbench.NavigateToAsync"/>,
/// which opens the file of a row that no tab shows.
/// </remarks>
internal sealed class SamplePanels
{
	private readonly string _root;
	private readonly Func<string, string?> _readText;

	private SamplePane? _active;
	private string _outlineFilter = string.Empty;

	/// <param name="root">The sample folder, which the rows name their files relative to.</param>
	/// <param name="readText">
	/// Returns the text of a document: the editor text of an open tab, the file on disk otherwise, or <see langword="null"/>.
	/// </param>
	internal SamplePanels(string root, Func<string, string?> readText)
	{
		_root = root;
		_readText = readText;
	}

	/// <summary>Raised after <see cref="Outline"/> changed.</summary>
	internal event EventHandler? OutlineChanged;

	/// <summary>Raised after <see cref="Problems"/> changed.</summary>
	internal event EventHandler? ProblemsChanged;

	/// <summary>Raised after <see cref="References"/> changed.</summary>
	internal event EventHandler? ReferencesChanged;

	/// <summary>Raised after <see cref="SearchResults"/> changed.</summary>
	internal event EventHandler? SearchResultsChanged;

	/// <summary>The symbols of the active pane, a child indented below its parent.</summary>
	internal IReadOnlyList<SampleLocation> Outline { get; private set; } = [];

	/// <summary>The diagnostics of the active pane, in text order.</summary>
	internal IReadOnlyList<SampleLocation> Problems { get; private set; } = [];

	/// <summary>The references the last search found, in open tabs and in files on disk.</summary>
	internal IReadOnlyList<SampleLocation> References { get; private set; } = [];

	/// <summary>The matches the last find-all or find-in-files search found.</summary>
	internal IReadOnlyList<SampleLocation> SearchResults { get; private set; } = [];

	/// <summary>The text a symbol name must contain, ignoring case, to stay in <see cref="Outline"/>.</summary>
	internal string OutlineFilter
	{
		get => _outlineFilter;
		set
		{
			_outlineFilter = value;

			if (_active is not null)
				_active.Session.Language.Outline.Filter = value;
		}
	}

	/// <summary>Follows the outline and the problems of a new pane, and those of each language session it gets later.</summary>
	internal void Watch(SamplePane pane)
	{
		WatchLanguage(pane);

		// An edit moves the underlines, so the rows follow them to where their text is now.
		pane.Editor.TextChanged += (_, _) => RefreshProblemsOf(pane);

		// A new language session brings a new outline and new diagnostics, which the lists follow from then on.
		pane.Session.LanguageReplaced += (_, _) =>
		{
			WatchLanguage(pane);
			pane.Session.Language.Outline.Filter = _outlineFilter;
			RefreshProblemsOf(pane);
		};
	}

	/// <summary>Makes the lists show the outline and the problems of another pane, or nothing when no tab is open.</summary>
	internal void Follow(SamplePane? pane)
	{
		_active = pane;

		if (pane is null)
		{
			Outline = [];
			Problems = [];

			OutlineChanged?.Invoke(this, EventArgs.Empty);
			ProblemsChanged?.Invoke(this, EventArgs.Empty);

			return;
		}

		// The filter applies to whichever outline is shown, and setting it raises the outline's change.
		pane.Session.Language.Outline.Filter = _outlineFilter;
		RefreshOutlineOf(pane);
		RefreshProblemsOf(pane);
	}

	/// <summary>Shows the references a search found and returns how many rows it shows.</summary>
	internal int ShowReferences(IReadOnlyList<ReferenceLocation> references)
	{
		List<SampleLocation> rows = [];

		foreach (IGrouping<string, ReferenceLocation> document in references.GroupBy(reference => reference.DocumentId, SampleDocuments.PathComparer))
		{
			if (_readText(document.Key) is not { } text)
				continue;

			var lineMap = TextLineMap.Build(text);

			foreach (ReferenceLocation reference in document)
			{
				if (lineMap.TryGetOffsets(reference.Range, out TextRange range))
					rows.Add(CreateLineRow(document.Key, lineMap, range));
			}
		}

		References = rows;
		ReferencesChanged?.Invoke(this, EventArgs.Empty);

		return rows.Count;
	}

	/// <summary>Shows the matches of a search, grouped by the file they are in.</summary>
	internal void ShowSearchResults(IReadOnlyList<(string Path, string Text, IReadOnlyList<TextRange> Matches)> files)
	{
		List<SampleLocation> rows = [];

		foreach ((string path, string text, IReadOnlyList<TextRange> matches) in files)
		{
			var lineMap = TextLineMap.Build(text);
			rows.AddRange(matches.Select(match => CreateLineRow(path, lineMap, match)));
		}

		SearchResults = rows;
		SearchResultsChanged?.Invoke(this, EventArgs.Empty);
	}

	// A row that points into a line shows where it lies and the text of that line, the way a references list does.
	private SampleLocation CreateLineRow(string path, TextLineMap lineMap, TextRange range)
	{
		TextPosition start = lineMap.GetPosition(range.Offset);
		string line = lineMap.GetLineText(start.Line).Trim();

		return new SampleLocation($"{DisplayName(path)}:{start.Line + 1}:{start.Character + 1}  {line}", path, range);
	}

	private string DisplayName(string path) => Path.GetRelativePath(_root, path).Replace('\\', '/');

	private void WatchLanguage(SamplePane pane)
	{
		pane.Session.Language.Outline.Changed += (_, _) => RefreshOutlineOf(pane);

		if (pane.Session.Language.Diagnostics is { } diagnostics)
			diagnostics.Changed += (_, _) => RefreshProblemsOf(pane);
	}

	private void RefreshOutlineOf(SamplePane pane)
	{
		if (pane != _active)
			return;

		List<SampleLocation> rows = [];
		AddSymbols(rows, pane, symbols: pane.Session.Language.Outline.Symbols, depth: 0);

		Outline = rows;
		OutlineChanged?.Invoke(this, EventArgs.Empty);
	}

	private void RefreshProblemsOf(SamplePane pane)
	{
		if (pane != _active)
			return;

		List<SampleLocation> rows = [];

		if (pane.Session.Language.Diagnostics is { } diagnostics)
		{
			// Each row takes the range its underline shows now, and a diagnostic whose text was deleted has no row until the next set.
			for (int index = 0; index < diagnostics.Current.Count; index++)
			{
				if (diagnostics.TryGetShownRange(index, out TextRange range))
					rows.Add(new SampleLocation(Describe(pane, diagnostics.Current[index], range), pane.FilePath, range));
			}
		}

		Problems = [.. rows.OrderBy(row => row.Range.Offset)];

		ProblemsChanged?.Invoke(this, EventArgs.Empty);
	}

	private static void AddSymbols(List<SampleLocation> rows, SamplePane pane, IReadOnlyList<DocumentSymbol> symbols, int depth)
	{
		foreach (DocumentSymbol symbol in symbols)
		{
			// The selection range is the name, which is where a click on the row puts the caret.
			TextRange range = symbol.SelectionRange ?? symbol.Range ?? default;

			// A server such as Roslyn repeats the name as the detail, which then adds nothing to the row.
			string detail =
				symbol.Detail is { Length: > 0 } text && !symbol.Name.Contains(text, StringComparison.Ordinal) ? $" {text}" : string.Empty;

			rows.Add(new SampleLocation($"{new string(' ', depth * 3)}{symbol.Name}{detail}  [{symbol.Kind}]", pane.FilePath, range));
			AddSymbols(rows, pane, symbols: symbol.Children, depth: depth + 1);
		}
	}

	private string Describe(SamplePane pane, Diagnostic diagnostic, TextRange range)
	{
		int offset = Math.Clamp(range.Offset, 0, pane.Editor.Document.TextLength);
		TextLocation location = pane.Editor.Document.GetLocation(offset);

		return $"{diagnostic.Severity}  {DisplayName(pane.FilePath)}:{location.Line}:{location.Column}  {diagnostic.Message}";
	}
}
