using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;

/// <summary>Resolves the definition of the symbol under the pointer from a hover provider and a definition provider.</summary>
/// <remarks>
/// The resolver owns the host-independent policy of the hover-first probe: an offset outside the snapshot, a hover without a symbol name,
/// and a location that does not apply to the document each resolve to no navigation. A callback is the host's call into its provider,
/// typically <see cref="IHoverProvider.GetHoverAsync"/> or <see cref="IDefinitionProvider.GetDefinitionAsync"/>. No method touches host state,
/// so it may be called from any thread.
/// </remarks>
public static class HoverDefinitionResolver
{
	/// <summary>Resolves the definition of the symbol hovered at the specified offset.</summary>
	/// <param name="definitionResolverAsync">The callback that resolves definition locations asynchronously.</param>
	/// <param name="hoverResolverAsync">The callback that resolves the hovered symbol asynchronously.</param>
	/// <param name="documentId">The identifier of the document the snapshot belongs to.</param>
	/// <param name="snapshot">The document snapshot the offset addresses.</param>
	/// <param name="offset">
	/// The zero-based offset into <paramref name="snapshot"/> to resolve, including the offset at the end of the document.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the resolver requests.</param>
	/// <returns>
	/// The resolved navigation target, or <see langword="null"/> when the offset is outside the snapshot, the hover resolver resolves no symbol,
	/// or the symbol resolves to no applicable location.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="definitionResolverAsync"/>, <paramref name="hoverResolverAsync"/>, <paramref name="documentId"/>,
	/// or <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	/// <remarks>
	/// The document text is materialized once, so both resolvers evaluate the same snapshot. The definition request carries the hovered offset
	/// beside the symbol name, so a provider that resolves by position can use it.
	/// </remarks>
	public static async Task<DefinitionNavigationTarget?> ResolveAsync(
		Func<DefinitionRequest, CancellationToken, Task<DefinitionLocation?>> definitionResolverAsync,
		Func<HoverRequest, CancellationToken, Task<HoverInformation?>> hoverResolverAsync,
		string documentId,
		ITextSnapshot snapshot,
		int offset,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);
		ArgumentNullException.ThrowIfNull(hoverResolverAsync);

		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
		ArgumentNullException.ThrowIfNull(snapshot);

		cancellationToken.ThrowIfCancellationRequested();

		if (!DefinitionResolver.IsOffsetInSnapshot(snapshot, offset))
			return null;

		string documentText = DefinitionResolver.GetSnapshotText(snapshot);

		HoverInformation? hoverInfo = await hoverResolverAsync(new HoverRequest(documentId, documentText, offset), cancellationToken)
			.ConfigureAwait(false);

		if (hoverInfo is null || string.IsNullOrWhiteSpace(hoverInfo.SymbolName))
			return null;

		DefinitionLocation? location = await definitionResolverAsync(
				new DefinitionRequest(
					documentId,
					documentText,
					symbolName: hoverInfo.SymbolName,
					discriminator: hoverInfo.DefinitionDiscriminator,
					symbolOffset: offset
				),

				cancellationToken
			)
			.ConfigureAwait(false);

		return DefinitionResolver.CreateTarget(snapshot, location, documentId);
	}
}
