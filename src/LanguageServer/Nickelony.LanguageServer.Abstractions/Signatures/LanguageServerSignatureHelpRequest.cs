using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Signatures;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a signature-help request against a document position.
/// </summary>
/// <remarks>
/// The request carries the optional trigger context; the supplied text is authoritative for the
/// call and is synchronized to the provider's tracked document before the request is issued. See
/// the remarks on <see cref="ILanguageServerIntelliSenseProvider"/> for the request-shape rule and
/// the document path contract.
/// </remarks>
public sealed record LanguageServerSignatureHelpRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerSignatureHelpRequest"/> record.
	/// </summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="position">The zero-based line and character position. Negative values are changed to zero.</param>
	/// <param name="context">
	/// The trigger context, or <see langword="null"/> to issue a position-only request without one.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	public LanguageServerSignatureHelpRequest(string filePath, string documentText,
		TextPosition position, TextSignatureHelpContext? context = null)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);

		FilePath = filePath;
		DocumentText = documentText;
		Position = position.ClampNegativeToZero();
		Context = context;
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
	/// Gets the zero-based position of the signature-help request.
	/// </summary>
	public TextPosition Position { get; }

	/// <summary>
	/// Gets the trigger context, or <see langword="null"/> when the caller supplied none.
	/// </summary>
	public TextSignatureHelpContext? Context { get; }
}
