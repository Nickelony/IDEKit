namespace Nickelony.IDEKit.LanguageFeatures.Diagnostics;

/// <summary>Describes a diagnostics request against an immutable document snapshot.</summary>
/// <remarks>
/// The request is framework-neutral: rule-set or profile selection is provider configuration captured at construction, so the request carries
/// only the document snapshot and its identifier. Record equality compares the identifier and the text.
/// </remarks>
public sealed record DiagnosticsRequest
{
	/// <param name="documentId">
	/// The identifier of the document the text belongs to. A language-server provider maps it to the document's file URI, and a local provider
	/// that resolves rules by text alone may ignore it.
	/// </param>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	public DiagnosticsRequest(string documentId, string documentText)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
		ArgumentNullException.ThrowIfNull(documentText);

		DocumentId = documentId;
		DocumentText = documentText;
	}

	/// <summary>The identifier of the document the text belongs to.</summary>
	public string DocumentId { get; }

	/// <summary>The current document snapshot text.</summary>
	public string DocumentText { get; }
}
