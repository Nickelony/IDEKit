namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Represents text edits for one or more documents.
/// </summary>
/// <remarks>
/// <para>
/// The constructor copies the supplied document edits into an immutable snapshot. Record equality compares
/// the document edits element-wise (including each document's edits), so two independently constructed
/// snapshots with equal values are equal.
/// </para>
/// <para>
/// This model represents text edits only. Workspace edits that also contain file create, rename, or delete
/// operations cannot be represented; providers must fail such results closed (for example by returning
/// <see langword="null"/> from the rename request) instead of applying a partial edit.
/// </para>
/// </remarks>
public sealed record TextWorkspaceEdit
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextWorkspaceEdit"/> record.
	/// </summary>
	/// <param name="documentEdits">The edits grouped by document.</param>
	/// <exception cref="ArgumentNullException"><paramref name="documentEdits"/> is <see langword="null"/>.</exception>
	public TextWorkspaceEdit(IReadOnlyList<TextDocumentEdit> documentEdits)
	{
		ArgumentNullException.ThrowIfNull(documentEdits);
		DocumentEdits = Array.AsReadOnly([.. documentEdits]);
	}

	/// <summary>
	/// Gets the immutable snapshot of edits grouped by document.
	/// </summary>
	public IReadOnlyList<TextDocumentEdit> DocumentEdits { get; }

	/// <summary>
	/// Gets a value indicating whether applying this edit set can change a document.
	/// </summary>
	/// <remarks>
	/// The value is <see langword="true"/> when at least one edit inserts replacement text or covers a non-empty
	/// range; a document whose edits are all no-ops (empty replacement text over an empty range) does not count.
	/// The check reports edit presence, not the resulting document state.
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

	/// <summary>
	/// Determines whether the supplied workspace edit contains the same documents and edits.
	/// </summary>
	/// <param name="other">The workspace edit to compare with.</param>
	/// <returns><see langword="true"/> when the values are equal; otherwise, <see langword="false"/>.</returns>
	public bool Equals(TextWorkspaceEdit? other)
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

	/// <summary>
	/// Serves as the hash function for <see cref="TextWorkspaceEdit"/>.
	/// </summary>
	/// <returns>A hash code over the document edits.</returns>
	public override int GetHashCode()
	{
		var hashCode = new HashCode();

		for (int i = 0; i < DocumentEdits.Count; i++)
			hashCode.Add(DocumentEdits[i]);

		return hashCode.ToHashCode();
	}

	private static bool ChangesDocument(TextEdit textEdit)
		=> textEdit.NewText.Length > 0 || !textEdit.Range.IsEmpty;
}
