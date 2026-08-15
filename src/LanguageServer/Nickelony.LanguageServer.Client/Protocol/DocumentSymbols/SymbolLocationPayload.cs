using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the location of a flat document-symbol entry.
/// </summary>
/// <remarks>
/// <see cref="Uri"/> is required and validated: parsing skips entries whose URI is blank or is not an absolute URI,
/// so a constructed location always names a document.
/// </remarks>
public sealed record SymbolLocationPayload
{
	/// <summary>
	/// Initializes a new instance of the <see cref="SymbolLocationPayload"/> class.
	/// </summary>
	/// <param name="Uri">The target document URI; parsed payloads always carry one.</param>
	/// <param name="Range">The range of the symbol in the target document.</param>
	/// <exception cref="ArgumentException"><paramref name="Uri"/> is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="Uri"/> is <see langword="null"/>.</exception>
	public SymbolLocationPayload(string Uri, ProtocolRangePayload Range)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(Uri);

		this.Uri = Uri;
		this.Range = Range;
	}

	/// <summary>Gets the target document URI.</summary>
	[JsonPropertyName("uri")]
	public string Uri { get; init; }

	/// <summary>Gets the range of the symbol in the target document.</summary>
	[JsonPropertyName("range")]
	public ProtocolRangePayload Range { get; init; }

	/// <summary>Deconstructs the location into its components.</summary>
	/// <param name="Uri">The target document URI.</param>
	/// <param name="Range">The range of the symbol in the target document.</param>
	public void Deconstruct(out string Uri, out ProtocolRangePayload Range)
	{
		Uri = this.Uri;
		Range = this.Range;
	}
}
