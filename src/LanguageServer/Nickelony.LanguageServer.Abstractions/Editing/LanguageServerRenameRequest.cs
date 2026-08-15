using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a request to rename the symbol at a document position.
/// </summary>
public sealed record LanguageServerRenameRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerRenameRequest"/> record.
	/// </summary>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="position">The zero-based symbol position. Negative values are changed to zero.</param>
	/// <param name="newName">The requested replacement name.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/>, <paramref name="documentText"/>, or <paramref name="newName"/> is
	/// <see langword="null"/>.
	/// </exception>
	public LanguageServerRenameRequest(string filePath, string documentText, TextPosition position, string newName)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(newName);

		FilePath = filePath;
		DocumentText = documentText;
		Position = position.ClampNegativeToZero();
		NewName = newName;
	}

	/// <summary>
	/// Gets the current document file path.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the current document content.
	/// </summary>
	public string DocumentText { get; }

	/// <summary>
	/// Gets the zero-based symbol position.
	/// </summary>
	public TextPosition Position { get; }

	/// <summary>
	/// Gets the requested replacement name.
	/// </summary>
	public string NewName { get; }
}
