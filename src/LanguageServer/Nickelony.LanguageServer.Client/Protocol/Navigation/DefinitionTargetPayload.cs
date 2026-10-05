namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a single definition target parsed from an LSP location or location-link payload in protocol coordinates.</summary>
/// <param name="Uri">The target document URI.</param>
/// <param name="TargetRange">The zero-based protocol range covering the whole definition.</param>
/// <param name="SelectionRange">
/// The optional zero-based protocol range identifying the definition's name, or <see langword="null"/> when the payload identified the target
/// with a single range.
/// </param>
/// <param name="OriginSelectionRange">
/// The optional zero-based protocol range in the requesting document that the link refers to, or <see langword="null"/> when the server sent none.
/// </param>
/// <exception cref="ArgumentException"><paramref name="Uri"/> is empty or whitespace-only.</exception>
/// <exception cref="ArgumentNullException"><paramref name="Uri"/> is <see langword="null"/>.</exception>
/// <remarks>
/// <para>
/// The payload stays in zero-based protocol coordinates, so a host maps it into its own range model.
/// </para>
/// <para>
/// <see cref="Uri"/> is required: parsing skips targets whose URI is blank or is not an absolute URI. The constructor and the <see cref="Uri"/>
/// initializer reject only a blank URI, so a constructed target can carry a relative URI.
/// </para>
/// </remarks>
public sealed record DefinitionTargetPayload(
	string Uri,
	RangePayload TargetRange,
	RangePayload? SelectionRange = null,
	RangePayload? OriginSelectionRange = null
)
{
	private readonly string _uri = DocumentUriValidation.RequireText(Uri);

	/// <summary>The target document URI.</summary>
	/// <exception cref="ArgumentException">The value is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public string Uri
	{
		get => _uri;
		init => _uri = DocumentUriValidation.RequireText(value);
	}
}
