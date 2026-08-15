namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a single definition target parsed from an LSP location or location-link payload in protocol coordinates.
/// </summary>
/// <remarks>
/// <para>
/// The payload stays in protocol coordinates; hosts convert it to their editor range model through
/// <c>ProtocolRangeConversion.TryGetTextPositionRange</c>.
/// </para>
/// <para>
/// <see cref="Uri"/> is required and validated: parsing skips targets whose URI is blank or is not an absolute URI,
/// so a constructed target always names a document.
/// </para>
/// </remarks>
public sealed record DefinitionTargetPayload
{
	/// <summary>
	/// Initializes a new instance of the <see cref="DefinitionTargetPayload"/> class.
	/// </summary>
	/// <param name="Uri">
	/// The target document URI. Definition parsing skips targets whose URI is blank or is not an absolute URI, so
	/// parsed targets always carry one that names a document.
	/// </param>
	/// <param name="TargetRange">The zero-based protocol range covering the whole definition.</param>
	/// <param name="SelectionRange">
	/// The optional zero-based protocol range identifying the definition's name, or <see langword="null"/> when
	/// the payload identified the target with a single range.
	/// </param>
	/// <param name="OriginSelectionRange">
	/// The optional zero-based protocol range in the requesting document that the link refers to, or
	/// <see langword="null"/> when the server sent none.
	/// </param>
	/// <exception cref="ArgumentException"><paramref name="Uri"/> is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="Uri"/> is <see langword="null"/>.</exception>
	public DefinitionTargetPayload(string Uri, ProtocolRangePayload TargetRange,
		ProtocolRangePayload? SelectionRange = null, ProtocolRangePayload? OriginSelectionRange = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(Uri);

		this.Uri = Uri;
		this.TargetRange = TargetRange;
		this.SelectionRange = SelectionRange;
		this.OriginSelectionRange = OriginSelectionRange;
	}

	/// <summary>Gets the target document URI.</summary>
	public string Uri { get; init; }

	/// <summary>Gets the zero-based protocol range covering the whole definition.</summary>
	public ProtocolRangePayload TargetRange { get; init; }

	/// <summary>Gets the optional zero-based protocol range identifying the definition's name, or <see langword="null"/>.</summary>
	public ProtocolRangePayload? SelectionRange { get; init; }

	/// <summary>Gets the optional zero-based protocol range in the requesting document that the link refers to, or <see langword="null"/>.</summary>
	public ProtocolRangePayload? OriginSelectionRange { get; init; }

	/// <summary>Deconstructs the target into its components.</summary>
	/// <param name="Uri">The target document URI.</param>
	/// <param name="TargetRange">The zero-based protocol range covering the whole definition.</param>
	/// <param name="SelectionRange">The optional name range, or <see langword="null"/>.</param>
	/// <param name="OriginSelectionRange">The optional origin range, or <see langword="null"/>.</param>
	public void Deconstruct(out string Uri, out ProtocolRangePayload TargetRange,
		out ProtocolRangePayload? SelectionRange, out ProtocolRangePayload? OriginSelectionRange)
	{
		Uri = this.Uri;
		TargetRange = this.TargetRange;
		SelectionRange = this.SelectionRange;
		OriginSelectionRange = this.OriginSelectionRange;
	}
}
