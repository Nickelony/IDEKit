using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.LanguageServer.Testing;

/// <summary>
/// Test-only conveniences that forward the concise <c>(filePath, documentText, position)</c> call shape to the request-record members of the provider
/// feature contracts.
/// </summary>
/// <remarks>
/// Keeps the provider suites readable. A line and character position becomes the document offset the request carries, so a test states the position
/// the language-server wire carries.
/// </remarks>
internal static class ProviderRequestTestExtensions
{
	/// <summary>Forwards to <see cref="IHoverProvider.GetHoverAsync(HoverRequest, CancellationToken)"/>.</summary>
	/// <returns>The hover information, or <see langword="null"/> when unavailable.</returns>
	internal static Task<HoverInformation?> GetHoverAsync(
		this IHoverProvider provider,
		string filePath,
		string documentText,
		TextPosition position,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(provider);
		return provider.GetHoverAsync(new HoverRequest(filePath, documentText, documentText.OffsetAt(position)), cancellationToken);
	}

	/// <summary>Forwards to <see cref="IDefinitionProvider.GetDefinitionAsync(DefinitionRequest, CancellationToken)"/>.</summary>
	/// <returns>The resolved definition location, or <see langword="null"/> when unavailable.</returns>
	internal static Task<DefinitionLocation?> GetDefinitionAsync(
		this IDefinitionProvider provider,
		string filePath,
		string documentText,
		TextPosition position,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(provider);

		return provider.GetDefinitionAsync(
			new DefinitionRequest(documentId: filePath, documentText, symbolName: string.Empty, symbolOffset: documentText.OffsetAt(position)),
			cancellationToken
		);
	}

	/// <summary>
	/// Forwards to <see cref="IDocumentSymbolProvider.GetDocumentSymbolsAsync(DocumentSymbolRequest, CancellationToken)"/> without a filter.
	/// </summary>
	/// <returns>The document symbols, or an empty list when unavailable.</returns>
	internal static Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(
		this IDocumentSymbolProvider provider,
		string filePath,
		string documentText,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(provider);
		return provider.GetDocumentSymbolsAsync(new DocumentSymbolRequest(filePath, documentText, filterText: string.Empty), cancellationToken);
	}

	/// <summary>Maps a line and character position to the offset of the document text, clamping it to the document like the wire does.</summary>
	/// <param name="documentText">The document text the position refers to.</param>
	/// <param name="position">The position to map.</param>
	/// <returns>The zero-based UTF-16 offset of <paramref name="position"/>.</returns>
	internal static int OffsetAt(this string documentText, TextPosition position) => TextLineMap.Build(documentText).GetOffset(position);

	/// <summary>Maps a position range to the offset range of the document text, clamping it to the document like the wire does.</summary>
	/// <param name="documentText">The document text the range refers to.</param>
	/// <param name="range">The range to map.</param>
	/// <returns>The offset range of <paramref name="range"/>.</returns>
	/// <exception cref="ArgumentException"><paramref name="range"/> ends before it starts.</exception>
	internal static TextRange RangeAt(this string documentText, TextPositionRange range)
	{
		return TextLineMap.Build(documentText).TryGetOffsets(range, out TextRange offsetRange)
			? offsetRange
			: throw new ArgumentException("The range ends before it starts.", nameof(range));
	}
}
