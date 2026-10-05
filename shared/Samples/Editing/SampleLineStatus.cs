using Nickelony.IDEKit.Core.Bookmarks;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Bookmarks;
using Nickelony.IDEKit.AvaloniaEdit.LineStatus;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Bookmarks;
using Nickelony.IDEKit.AvalonEdit.LineStatus;
#endif

namespace Nickelony.Samples;

/// <summary>
/// Shows the bookmarks and the unsaved-change markers of one editor in its left margins, and persists the bookmarks beside the file.
/// </summary>
internal sealed class SampleLineStatus : IDisposable
{
	private readonly BookmarkTracker _bookmarks;
	private readonly UnsavedChangesTracker _unsavedChanges;
	private readonly IBookmarkStore _store = new BookmarkSidecarStore(".bookmarks");

	private string? _filePath;

	/// <param name="editor">The editor control whose margins show the markers.</param>
	internal SampleLineStatus(TextEditor editor)
	{
		_bookmarks = new BookmarkTracker(() => editor.Document);
		_unsavedChanges = new UnsavedChangesTracker(() => editor.Document);

		editor.TextArea.LeftMargins.Add(new BookmarkMargin(_bookmarks));
		editor.TextArea.LeftMargins.Add(new ChangeMarkerMargin(_unsavedChanges));

		_bookmarks.Changed += (_, _) => SaveBookmarks();
	}

	/// <summary>The one-based numbers of the bookmarked lines.</summary>
	/// <summary>The text of the file as it was last loaded or saved, which unsaved edits are measured against.</summary>
	/// <summary>The one-based numbers of the lines that differ from the saved baseline.</summary>
	internal IReadOnlyList<int> ChangedLines => _unsavedChanges.GetMarkedLineNumbers();

	/// <summary>
	/// Binds the trackers to a file that has been loaded or saved: its text becomes the baseline and its sidecar restores the bookmarks.
	/// </summary>
	internal void Bind(string filePath, string savedText)
	{
		_filePath = filePath;
		_unsavedChanges.SetBaseline(savedText);
		_bookmarks.RestoreFrom(_store, filePath);
	}

	/// <summary>Makes the text the new baseline after a save.</summary>
	internal void MarkSaved(string savedText) => _unsavedChanges.SetBaseline(savedText);

	/// <summary>Toggles the bookmark of the line at the offset.</summary>
	internal void ToggleBookmark(int offset) => _bookmarks.ToggleBookmark(offset);

	public void Dispose() => _unsavedChanges.Dispose();

	private void SaveBookmarks()
	{
		// While the bookmarks are not bound to the current document, SaveTo throws instead of deleting the sidecar.
		if (_filePath is not null && _bookmarks.IsBoundToCurrentDocument)
			_bookmarks.SaveTo(_store, _filePath);
	}
}
