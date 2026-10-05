namespace Nickelony.IDEKit.LanguageFeatures.Editing;

/// <summary>Represents the text edits for one document.</summary>
/// <remarks>
/// The constructor copies the supplied edits into an immutable snapshot. Record equality compares the document identifier and the edits element-wise
/// rather than by collection reference, so two independently constructed snapshots with equal values are equal. Equality visits every edit, so a host
/// that compares a large edit set repeatedly should store the result instead of comparing again.
/// </remarks>
public sealed record TextDocumentEdit
{
	/// <param name="documentId">The identifier of the document to update.</param>
	/// <param name="textEdits">The edits to apply.</param>
	/// <exception cref="ArgumentNullException"><paramref name="documentId"/> or <paramref name="textEdits"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="textEdits"/> contains a <see langword="null"/> element.</exception>
	public TextDocumentEdit(string documentId, IReadOnlyList<TextEdit> textEdits)
	{
		ArgumentNullException.ThrowIfNull(documentId);
		ArgumentNullException.ThrowIfNull(textEdits);

		DocumentId = documentId;
		TextEdits = CollectionSnapshots.Capture(textEdits, "text edits", nameof(textEdits));
	}

	/// <summary>The identifier of the document to update.</summary>
	public string DocumentId { get; }

	/// <summary>The immutable snapshot of edits to apply to the document.</summary>
	public IReadOnlyList<TextEdit> TextEdits { get; }

	/// <summary>Determines whether the supplied document edit has the same document identifier and the same edits.</summary>
	/// <param name="other">The document edit to compare with.</param>
	/// <returns><see langword="true"/> when the values are equal; otherwise, <see langword="false"/>.</returns>
	public bool Equals(TextDocumentEdit? other)
	{
		if (other is null)
			return false;

		if (ReferenceEquals(this, other))
			return true;

		if (!string.Equals(DocumentId, other.DocumentId, StringComparison.Ordinal) || TextEdits.Count != other.TextEdits.Count)
			return false;

		for (int i = 0; i < TextEdits.Count; i++)
		{
			if (!TextEdits[i].Equals(other.TextEdits[i]))
				return false;
		}

		return true;
	}

	/// <summary>Serves as the hash function for <see cref="TextDocumentEdit"/>.</summary>
	/// <returns>A hash code over the document identifier and the edits.</returns>
	public override int GetHashCode()
	{
		var hashCode = new HashCode();
		hashCode.Add(DocumentId, StringComparer.Ordinal);

		for (int i = 0; i < TextEdits.Count; i++)
			hashCode.Add(TextEdits[i]);

		return hashCode.ToHashCode();
	}
}
