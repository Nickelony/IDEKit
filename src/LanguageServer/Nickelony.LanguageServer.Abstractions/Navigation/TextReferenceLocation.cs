using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Navigation;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Identifies a symbol reference in a source file.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Range"/> uses the line/character unit model documented on
/// <see cref="TextPosition"/> - zero-based positions whose line counts line breaks and whose character
/// counts UTF-16 code units within the line. Range values are stored as supplied.
/// </para>
/// <para>
/// This type names its document with <see cref="FilePath"/>, the local file path identity the package
/// uses everywhere. That is deliberately narrower than the sibling navigation result
/// <see cref="TextDefinitionLocation"/>: a definition can name a document the provider has not
/// materialized, so that type identifies its document with a nullable opaque
/// <see cref="TextDefinitionLocation.DocumentId"/> and separates the whole-definition range from the
/// name range, while this family reports a reference only for a URI that resolves to a local path and
/// never needs more than the one range. A host converting both results maps <see cref="FilePath"/> and
/// the definition's document identifier to the same value.
/// </para>
/// </remarks>
public sealed record TextReferenceLocation
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextReferenceLocation"/> record.
	/// </summary>
	/// <param name="filePath">The file containing the reference.</param>
	/// <param name="range">The zero-based range of the reference.</param>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	public TextReferenceLocation(string filePath, TextPositionRange range)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		FilePath = filePath;
		Range = range;
	}

	/// <summary>
	/// Gets the file containing the reference.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the zero-based range of the reference.
	/// </summary>
	public TextPositionRange Range { get; }
}
