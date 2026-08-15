namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Represents the text edits for one document.
/// </summary>
/// <remarks>
/// The constructor copies the supplied edits into an immutable snapshot. Record equality compares the file
/// path and the edits element-wise rather than by collection reference, so two independently constructed
/// snapshots with equal values are equal; compare a large edit set once and reuse the result when equality is
/// checked repeatedly.
/// </remarks>
public sealed record TextDocumentEdit
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextDocumentEdit"/> record.
	/// </summary>
	/// <param name="filePath">The path of the document to update.</param>
	/// <param name="textEdits">The edits to apply.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="textEdits"/> is <see langword="null"/>.
	/// </exception>
	public TextDocumentEdit(string filePath, IReadOnlyList<TextEdit> textEdits)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(textEdits);

		FilePath = filePath;
		TextEdits = Array.AsReadOnly([.. textEdits]);
	}

	/// <summary>
	/// Gets the path of the document to update.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the immutable snapshot of edits to apply to the document.
	/// </summary>
	public IReadOnlyList<TextEdit> TextEdits { get; }

	/// <summary>
	/// Determines whether the supplied document edit has the same file path and the same edits.
	/// </summary>
	/// <param name="other">The document edit to compare with.</param>
	/// <returns><see langword="true"/> when the values are equal; otherwise, <see langword="false"/>.</returns>
	public bool Equals(TextDocumentEdit? other)
	{
		if (other is null)
			return false;

		if (ReferenceEquals(this, other))
			return true;

		if (!string.Equals(FilePath, other.FilePath, StringComparison.Ordinal) ||
			TextEdits.Count != other.TextEdits.Count)
		{
			return false;
		}

		for (int i = 0; i < TextEdits.Count; i++)
		{
			if (!TextEdits[i].Equals(other.TextEdits[i]))
				return false;
		}

		return true;
	}

	/// <summary>
	/// Serves as the hash function for <see cref="TextDocumentEdit"/>.
	/// </summary>
	/// <returns>A hash code over the file path and the edits.</returns>
	public override int GetHashCode()
	{
		var hashCode = new HashCode();
		hashCode.Add(FilePath, StringComparer.Ordinal);

		for (int i = 0; i < TextEdits.Count; i++)
			hashCode.Add(TextEdits[i]);

		return hashCode.ToHashCode();
	}
}
