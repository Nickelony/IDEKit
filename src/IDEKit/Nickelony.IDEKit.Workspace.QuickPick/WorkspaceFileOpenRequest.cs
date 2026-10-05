namespace Nickelony.IDEKit.Workspace.QuickPick;

/// <summary>The input of one open of a file from the files mode: the file, and the line and column the user typed after its name.</summary>
/// <remarks>
/// The line and the column are what the user typed, one-based and not checked against the file, because the provider has not read the file.
/// A host clamps them to the document it opens, as <c>IEditorNavigationTarget.MoveCaret</c> does.
/// </remarks>
public sealed record WorkspaceFileOpenRequest
{
	/// <param name="path">The full path of the file, or the id of an open document that has no file.</param>
	/// <param name="line">The line typed after the file name, or <see langword="null"/> when the user typed none.</param>
	/// <param name="column">The column typed after the line, or <see langword="null"/> when the user typed none.</param>
	/// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="column"/> is set without <paramref name="line"/>.</exception>
	public WorkspaceFileOpenRequest(string path, int? line = null, int? column = null)
	{
		ArgumentNullException.ThrowIfNull(path);

		if (column is not null && line is null)
			throw new ArgumentException("A column needs a line.", nameof(column));

		Path = path;
		Line = line;
		Column = column;
	}

	/// <summary>The full path of the file, or the id of an open document that has no file.</summary>
	public string Path { get; }

	/// <summary>The line the user typed after the file name, or <see langword="null"/> when they typed none.</summary>
	public int? Line { get; }

	/// <summary>The column the user typed after the line, or <see langword="null"/> when they typed none.</summary>
	public int? Column { get; }
}
