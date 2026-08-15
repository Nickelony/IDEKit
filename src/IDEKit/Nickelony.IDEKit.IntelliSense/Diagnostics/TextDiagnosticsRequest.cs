using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.IDEKit.IntelliSense.Diagnostics;

/// <summary>
/// Describes a diagnostics request against an immutable document snapshot.
/// </summary>
/// <remarks>
/// <para>
/// The request is framework-neutral: rule-set or profile selection is provider configuration, not
/// request data. A provider that evaluates versioned or profile-specific rules captures that
/// context when it is constructed, so the request carries only the document snapshot and its
/// optional document identifier; record equality compares both text and identifier.
/// </para>
/// <para>
/// <see cref="DocumentId"/> is an optional opaque provider-defined identifier (for example a path,
/// a URI, or a logical key) for providers that apply per-document rule sets; a blank value is
/// treated as absent and other values are trimmed. Providers that resolve rules by text alone may
/// ignore it.
/// </para>
/// </remarks>
public sealed record TextDiagnosticsRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextDiagnosticsRequest"/> record.
	/// </summary>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="documentId">
	/// The optional opaque identifier of the document the text belongs to, or <see langword="null"/>
	/// when the provider resolves rules by text alone. A blank value is treated as absent and other
	/// values are trimmed.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	public TextDiagnosticsRequest(string documentText, string? documentId = null)
	{
		ArgumentNullException.ThrowIfNull(documentText);

		DocumentText = documentText;
		DocumentId = OptionalText.Normalize(documentId);
	}

	/// <summary>
	/// Gets the current document snapshot text.
	/// </summary>
	public string DocumentText { get; }

	/// <summary>
	/// Gets the optional opaque identifier of the document the text belongs to, or
	/// <see langword="null"/> when the request does not name one.
	/// </summary>
	public string? DocumentId { get; }
}
