using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the location of a flat document-symbol entry.</summary>
/// <param name="Uri">The target document URI; parsed payloads always carry one.</param>
/// <param name="Range">The range of the symbol in the target document.</param>
/// <exception cref="ArgumentException"><paramref name="Uri"/> is empty or whitespace-only.</exception>
/// <exception cref="ArgumentNullException"><paramref name="Uri"/> is <see langword="null"/>.</exception>
/// <remarks>
/// <see cref="Uri"/> is required: parsing skips entries whose URI is blank or is not an absolute URI. The constructor and the <see cref="Uri"/>
/// initializer reject only a blank URI, so a constructed location can carry a relative URI. The type is a reference type
/// so that no <see langword="default"/> instance can carry a missing URI.
/// </remarks>
public sealed record SymbolLocationPayload(string Uri, [property: JsonPropertyName("range")] RangePayload Range)
{
	private readonly string _uri = DocumentUriValidation.RequireText(Uri);

	/// <summary>The target document URI.</summary>
	/// <exception cref="ArgumentException">The value is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	[JsonPropertyName("uri")]
	public string Uri
	{
		get => _uri;
		init => _uri = DocumentUriValidation.RequireText(value);
	}
}
