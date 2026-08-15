using Nickelony.IDEKit.IntelliSense.DocumentSymbols;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;

namespace Nickelony.Testing;

/// <summary>
/// Test-only conveniences that forward the concise <c>(filePath, documentText, position)</c> call shape to the
/// provider contract's request-record members.
/// </summary>
/// <remarks>
/// These exist so the large provider test suites stay readable now that the provider surface standardized on request
/// records; production callers construct the request records directly. This file is linked into the language-server
/// test projects only, so it adds no shipping surface and is not a library compatibility shim.
/// </remarks>
internal static class ProviderRequestTestExtensions
{
	/// <summary>
	/// Forwards to
	/// <see cref="ILanguageServerIntelliSenseProvider.GetHoverAsync(LanguageServerHoverRequest, CancellationToken)"/>.
	/// </summary>
	/// <param name="provider">The provider under test.</param>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="position">The zero-based position to inspect.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The hover information, or <see langword="null"/> when unavailable.</returns>
	internal static Task<TextHoverInfo?> GetHoverAsync(this ILanguageServerIntelliSenseProvider provider,
		string filePath, string documentText, TextPosition position, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(provider);

		return provider.GetHoverAsync(new LanguageServerHoverRequest(filePath, documentText, position), cancellationToken);
	}

	/// <summary>
	/// Forwards to
	/// <see cref="ILanguageServerIntelliSenseProvider.GetDefinitionAsync(LanguageServerDefinitionRequest, CancellationToken)"/>.
	/// </summary>
	/// <param name="provider">The provider under test.</param>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="position">The zero-based symbol position.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The resolved definition location, or <see langword="null"/> when unavailable.</returns>
	internal static Task<TextDefinitionLocation?> GetDefinitionAsync(this ILanguageServerIntelliSenseProvider provider,
		string filePath, string documentText, TextPosition position, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(provider);

		return provider.GetDefinitionAsync(new LanguageServerDefinitionRequest(filePath, documentText, position), cancellationToken);
	}

	/// <summary>
	/// Forwards to
	/// <see cref="ILanguageServerDocumentSymbolProvider.GetDocumentSymbolsAsync(LanguageServerDocumentSymbolRequest, CancellationToken)"/>.
	/// </summary>
	/// <param name="provider">The provider under test.</param>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The document symbols, or an empty list when unavailable.</returns>
	internal static Task<IReadOnlyList<TextDocumentSymbol>> GetDocumentSymbolsAsync(
		this ILanguageServerIntelliSenseProvider provider,
		string filePath, string documentText, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(provider);

		return provider.GetDocumentSymbolsAsync(new LanguageServerDocumentSymbolRequest(filePath, documentText), cancellationToken);
	}
}
