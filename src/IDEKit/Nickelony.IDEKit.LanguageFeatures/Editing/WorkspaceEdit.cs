namespace Nickelony.IDEKit.LanguageFeatures.Editing;

/// <summary>Represents text edits for one or more documents.</summary>
/// <remarks>
/// The constructor copies the document edits into an immutable snapshot, and record equality compares them element-wise,
/// including each document's edits. The model represents text edits only: file create, rename, or delete operations cannot be represented,
/// so a provider must fail such a result closed - for example by returning <see langword="null"/> from the rename request - rather than applying
/// a partial edit.
/// </remarks>
public sealed record WorkspaceEdit
{
	/// <param name="documentEdits">The edits grouped by document.</param>
	/// <exception cref="ArgumentNullException"><paramref name="documentEdits"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="documentEdits"/> contains a <see langword="null"/> element.</exception>
	public WorkspaceEdit(IReadOnlyList<TextDocumentEdit> documentEdits)
	{
		ArgumentNullException.ThrowIfNull(documentEdits);
		DocumentEdits = CollectionSnapshots.Capture(documentEdits, "document edits", nameof(documentEdits));
	}

	/// <summary>The immutable snapshot of edits grouped by document.</summary>
	public IReadOnlyList<TextDocumentEdit> DocumentEdits { get; }

	/// <summary>Indicates whether applying this edit set can change a document.</summary>
	/// <remarks>
	/// <see langword="true"/> when at least one edit inserts replacement text or covers a non-empty range; a no-op edit (empty replacement text
	/// over an empty range) does not count, but a reversed range without replacement text still counts. The check reports edit presence,
	/// not the resulting document state, and recomputes on each read rather than caching.
	/// </remarks>
	public bool HasChanges
	{
		get
		{
			for (int i = 0; i < DocumentEdits.Count; i++)
			{
				IReadOnlyList<TextEdit> textEdits = DocumentEdits[i].TextEdits;

				for (int j = 0; j < textEdits.Count; j++)
				{
					if (ChangesDocument(textEdits[j]))
						return true;
				}
			}

			return false;
		}
	}

	/// <summary>Determines whether the supplied workspace edit contains the same documents and edits.</summary>
	/// <param name="other">The workspace edit to compare with.</param>
	/// <returns><see langword="true"/> when the values are equal; otherwise, <see langword="false"/>.</returns>
	public bool Equals(WorkspaceEdit? other)
	{
		if (other is null)
			return false;

		if (ReferenceEquals(this, other))
			return true;

		if (DocumentEdits.Count != other.DocumentEdits.Count)
			return false;

		for (int i = 0; i < DocumentEdits.Count; i++)
		{
			if (!DocumentEdits[i].Equals(other.DocumentEdits[i]))
				return false;
		}

		return true;
	}

	/// <summary>Serves as the hash function for <see cref="WorkspaceEdit"/>.</summary>
	/// <returns>A hash code over the document edits.</returns>
	public override int GetHashCode()
	{
		var hashCode = new HashCode();

		for (int i = 0; i < DocumentEdits.Count; i++)
			hashCode.Add(DocumentEdits[i]);

		return hashCode.ToHashCode();
	}

	private static bool ChangesDocument(TextEdit textEdit) => textEdit.NewText.Length > 0 || !textEdit.Range.IsEmpty;
}
