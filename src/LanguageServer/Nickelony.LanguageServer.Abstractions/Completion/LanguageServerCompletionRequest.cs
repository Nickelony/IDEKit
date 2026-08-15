using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a completion request against a document position.
/// </summary>
/// <remarks>
/// The request carries the completion trigger state; the supplied text is authoritative for the
/// call and is synchronized to the provider's tracked document before the request is issued. See
/// the remarks on <see cref="ILanguageServerIntelliSenseProvider"/> for the request-shape rule and
/// the document path contract.
/// </remarks>
public sealed record LanguageServerCompletionRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerCompletionRequest"/> record.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="position">The zero-based line and character position. Negative values are changed to zero.</param>
	/// <param name="triggerCharacter">
	/// The character that triggered completion, or <see langword="null"/> when the host invoked completion by
	/// position. A blank value is treated as absent and surrounding whitespace is trimmed.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	public LanguageServerCompletionRequest(string filePath, string documentText,
		TextPosition position, string? triggerCharacter = null)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);

		FilePath = filePath;
		DocumentText = documentText;
		Position = position.ClampNegativeToZero();
		TriggerCharacter = OptionalText.Normalize(triggerCharacter);
	}

	/// <summary>
	/// Gets the local file path of the document.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the current document content.
	/// </summary>
	public string DocumentText { get; }

	/// <summary>
	/// Gets the zero-based position of the completion request.
	/// </summary>
	public TextPosition Position { get; }

	/// <summary>
	/// Gets the trigger character, or <see langword="null"/> when completion was invoked by position.
	/// </summary>
	public string? TriggerCharacter { get; }
}
