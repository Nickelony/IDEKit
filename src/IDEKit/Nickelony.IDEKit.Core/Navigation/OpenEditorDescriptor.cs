namespace Nickelony.IDEKit.Core.Navigation;

/// <summary>Describes one open editor tab of a host to the quick-pick modes that list files and tabs.</summary>
/// <remarks>
/// Two tabs can show one document, such as the views of a split, and then they share the <see cref="DocumentId"/> and differ in the title
/// and the <see cref="Tag"/>.
/// </remarks>
public sealed record OpenEditorDescriptor
{
	/// <summary>The icon key of an item whose document has unsaved changes, which a host maps to an image, such as a dot.</summary>
	public const string UnsavedIconKey = "unsaved";

	/// <param name="title">The text of the tab, which the open editors mode shows as the label.</param>
	/// <param name="documentId">
	/// The identity of the document the tab shows: the local path of its file, or, for a document without a file, an id of the host that is no path.
	/// </param>
	/// <param name="isUnsaved">Whether the document has changes that are not saved.</param>
	/// <exception cref="ArgumentNullException"><paramref name="title"/> or <paramref name="documentId"/> is <see langword="null"/>.</exception>
	public OpenEditorDescriptor(string title, string documentId, bool isUnsaved)
	{
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(documentId);

		Title = title;
		DocumentId = documentId;
		IsUnsaved = isUnsaved;
	}

	/// <summary>The text of the tab, which the open editors mode shows as the label.</summary>
	public string Title { get; }

	/// <summary>The identity of the document the tab shows: the local path of its file, or an id of the host that is no path.</summary>
	/// <remarks>
	/// The folder that a list shows comes from it, so a document without a file shows none.
	/// </remarks>
	public string DocumentId { get; }

	/// <summary>Whether the document has changes that are not saved.</summary>
	public bool IsUnsaved { get; }

	/// <summary>An object the host attaches to find the tab again when the user picks it; the quick pick never reads it.</summary>
	public object? Tag { get; init; }
}
