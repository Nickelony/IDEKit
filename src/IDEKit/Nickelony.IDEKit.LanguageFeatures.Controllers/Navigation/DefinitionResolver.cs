using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;

/// <summary>Resolves a definition navigation target from the shared provider contracts.</summary>
/// <remarks>
/// The resolver owns the host-independent policy: when a lookup resolves to no navigation,
/// and whether the location applies to the document the request was resolved from, which a location names by no id or by the request's own id.
/// A callback is the host's call into its provider, typically <see cref="IDefinitionProvider.GetDefinitionAsync"/>. No method touches host state,
/// so all may be called from any thread. The host applies <see cref="DefinitionNavigationTarget.DocumentStart"/> and owns opening another document
/// for a cross-document target.
/// </remarks>
public static class DefinitionResolver
{
	/// <summary>Resolves the definition of the supplied symbol name through an asynchronous definition resolver.</summary>
	/// <param name="definitionResolverAsync">The callback that resolves definition locations asynchronously.</param>
	/// <param name="documentId">The identifier of the document the snapshot belongs to.</param>
	/// <param name="snapshot">The document snapshot that provides the provider's document context.</param>
	/// <param name="symbolName">
	/// The symbol name to resolve; a <see langword="null"/>, empty, or whitespace-only name reports no navigation before the resolver is called.
	/// </param>
	/// <param name="discriminator">The optional language-specific discriminator that disambiguates the symbol's definition.</param>
	/// <param name="cancellationToken">A token that can cancel the resolver request.</param>
	/// <returns>
	/// The resolved navigation target, or <see langword="null"/> when the name is blank or the resolver resolves no applicable location.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="definitionResolverAsync"/>, <paramref name="documentId"/>, or <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	/// <remarks>
	/// The request carries no symbol offset, because the caller names the symbol without a position in the document.
	/// </remarks>
	public static async Task<DefinitionNavigationTarget?> ResolveFromSymbolAsync(
		Func<DefinitionRequest, CancellationToken, Task<DefinitionLocation?>> definitionResolverAsync,
		string documentId,
		ITextSnapshot snapshot,
		string? symbolName,
		DefinitionDiscriminator? discriminator = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
		ArgumentNullException.ThrowIfNull(snapshot);

		cancellationToken.ThrowIfCancellationRequested();

		if (string.IsNullOrWhiteSpace(symbolName))
			return null;

		DefinitionLocation? location = await definitionResolverAsync(
				new DefinitionRequest(documentId, documentText: GetSnapshotText(snapshot), symbolName, discriminator),
				cancellationToken
			)
			.ConfigureAwait(false);

		return CreateTarget(snapshot, location, documentId);
	}

	/// <summary>Resolves the definition of the symbol at an offset through an asynchronous definition resolver.</summary>
	/// <param name="definitionResolverAsync">
	/// The callback that resolves definition locations asynchronously, typically <see cref="IDefinitionProvider.GetDefinitionAsync"/>.
	/// </param>
	/// <param name="documentId">The identifier of the document the snapshot belongs to.</param>
	/// <param name="snapshot">The document snapshot the offset addresses.</param>
	/// <param name="offset">
	/// The zero-based offset into <paramref name="snapshot"/> to resolve, including the offset at the end of the document.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the resolver request.</param>
	/// <returns>
	/// The resolved navigation target, or <see langword="null"/> when the offset is outside the snapshot or the resolver resolves
	/// no applicable location.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="definitionResolverAsync"/>, <paramref name="documentId"/>, or <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	/// <remarks>
	/// The request carries the identifier that contains the offset, or the one that ends at it, and the offset itself, so a provider that resolves
	/// by name and one that resolves by position, such as a language server, both answer it. The name is empty where no identifier is there.
	/// </remarks>
	public static async Task<DefinitionNavigationTarget?> ResolveAtOffsetAsync(
		Func<DefinitionRequest, CancellationToken, Task<DefinitionLocation?>> definitionResolverAsync,
		string documentId,
		ITextSnapshot snapshot,
		int offset,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
		ArgumentNullException.ThrowIfNull(snapshot);

		cancellationToken.ThrowIfCancellationRequested();

		if (!IsOffsetInSnapshot(snapshot, offset))
			return null;

		string documentText = GetSnapshotText(snapshot);

		TextRange? name = IdentifierOperations.FindTokenSpan(
			snapshot,
			offset,
			policy: IdentifierCharacterPolicy.Default,
			mode: IdentifierSpanMode.Containing
		);

		DefinitionLocation? location = await definitionResolverAsync(
				new DefinitionRequest(
					documentId,
					documentText,
					symbolName: name is { } span ? documentText.Substring(span.Offset, span.Length) : string.Empty,
					symbolOffset: offset
				),

				cancellationToken
			)
			.ConfigureAwait(false);

		return CreateTarget(snapshot, location, documentId);
	}

	// A location names the requesting document either by no id or by the request's own id, which a language server returns for a
	// definition in the same file; the comparison is ordinal because the provider names the document as the request did.
	internal static DefinitionNavigationTarget? CreateTarget(ITextSnapshot snapshot, DefinitionLocation? location, string? requestDocumentId)
	{
		if (location is null)
			return null;

		// A location in another document is the host's to open, so its coordinates are not validated
		// against a document they do not address.
		if (location.DocumentId is not null && !string.Equals(location.DocumentId, requestDocumentId, StringComparison.Ordinal))
			return new DefinitionNavigationTarget(location);

		TextPosition start = location.NavigationStart;

		// A negative line or character, or a line beyond the snapshot, cannot identify a location, so it is
		// rejected symmetrically with the offset bounds instead of being clamped.
		if (start.Line < 0 || start.Line >= snapshot.LineCount || start.Character < 0)
			return null;

		return new DefinitionNavigationTarget(location, start);
	}

	internal static bool IsOffsetInSnapshot(ITextSnapshot snapshot, int offset) => offset >= 0 && offset <= snapshot.TextLength;

	// The request contracts carry the snapshot text rather than the snapshot itself, so the text is
	// materialized once per resolution and shared by the hover probe and its definition request. A
	// string-backed snapshot returns its backing instance for the full range.
	internal static string GetSnapshotText(ITextSnapshot snapshot) => snapshot.GetText(0, snapshot.TextLength);
}
