using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Hover;

namespace Nickelony.IDEKit.IntelliSense.Navigation;

/// <summary>
/// Resolves a definition navigation target from the shared provider contracts.
/// </summary>
/// <remarks>
/// <para>
/// The navigator owns the definition-navigation policy that is independent of any editor host: which
/// provider is consulted for which probe, when a probe resolves to no navigation at all, and whether the
/// resolved location is applicable to the document the request was resolved from. A host supplies the
/// document snapshot and the providers, applies <see cref="TextDefinitionNavigationTarget.DocumentStart"/>,
/// and owns opening another document for a cross-document target. No method touches host state, so all of
/// them may be called from any thread; the asynchronous forms take task-returning resolver delegates, which
/// the navigator awaits as supplied.
/// </para>
/// <para>
/// Three probes are supported: the hover probe resolves the hovered symbol through an
/// <see cref="ITextHoverProvider"/> and then resolves that symbol through an
/// <see cref="ITextDefinitionProvider"/>; the symbol probe resolves a known symbol name directly; and the
/// offset probe delegates to a position resolver for providers that answer a definition request for a
/// document position instead of a symbol name. No probe navigates on a partially valid request: an offset
/// outside the snapshot, a hover result without a symbol, a <see langword="null"/>, empty, or whitespace
/// symbol name, a provider that returns no location, and an in-document location whose navigation start
/// falls outside the snapshot all resolve to <see langword="null"/>. The provider contract uses zero-based
/// positions, so an out-of-document position is treated as absent rather than moved.
/// </para>
/// </remarks>
public static class TextDefinitionNavigator
{
	/// <summary>
	/// Resolves the definition of the symbol hovered at the specified offset.
	/// </summary>
	/// <param name="definitionProvider">The provider that resolves definition locations.</param>
	/// <param name="hoverProvider">The provider that resolves the hovered symbol.</param>
	/// <param name="snapshot">The document snapshot the offset addresses.</param>
	/// <param name="offset">
	/// The zero-based offset into <paramref name="snapshot"/> to resolve, including the offset at the end
	/// of the document.
	/// </param>
	/// <returns>
	/// The resolved navigation target, or <see langword="null"/> when the offset is outside the snapshot,
	/// the hover provider resolves no symbol, or the symbol resolves to no applicable location.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="definitionProvider"/>, <paramref name="hoverProvider"/>, or
	/// <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	public static TextDefinitionNavigationTarget? ResolveFromHover(
		ITextDefinitionProvider definitionProvider,
		ITextHoverProvider hoverProvider,
		ITextSnapshot snapshot,
		int offset)
	{
		ArgumentNullException.ThrowIfNull(definitionProvider);
		ArgumentNullException.ThrowIfNull(hoverProvider);
		ArgumentNullException.ThrowIfNull(snapshot);

		if (!IsOffsetInSnapshot(snapshot, offset))
			return null;

		string documentText = GetSnapshotText(snapshot);
		TextHoverInfo? hoverInfo = hoverProvider.GetHoverInfo(new TextHoverRequest(documentText, offset));

		if (hoverInfo is null || string.IsNullOrWhiteSpace(hoverInfo.SymbolName))
			return null;

		return CreateTarget(
			snapshot,
			ResolveSymbolLocation(definitionProvider, documentText, hoverInfo.SymbolName, hoverInfo.DefinitionDiscriminator));
	}

	/// <summary>
	/// Resolves the definition of the supplied symbol name.
	/// </summary>
	/// <param name="definitionProvider">The provider that resolves definition locations.</param>
	/// <param name="snapshot">The document snapshot that provides the provider's document context.</param>
	/// <param name="symbolName">
	/// The symbol name to resolve; a <see langword="null"/>, empty, or whitespace-only name reports no
	/// navigation before the provider is called.
	/// </param>
	/// <param name="discriminator">
	/// The optional language-specific discriminator that disambiguates the symbol's definition.
	/// </param>
	/// <returns>
	/// The resolved navigation target, or <see langword="null"/> when the name is blank or the provider
	/// resolves no applicable location.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="definitionProvider"/> or <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	public static TextDefinitionNavigationTarget? ResolveFromSymbol(
		ITextDefinitionProvider definitionProvider,
		ITextSnapshot snapshot,
		string? symbolName,
		TextDefinitionDiscriminator? discriminator = null)
	{
		ArgumentNullException.ThrowIfNull(definitionProvider);
		ArgumentNullException.ThrowIfNull(snapshot);

		if (string.IsNullOrWhiteSpace(symbolName))
			return null;

		return CreateTarget(
			snapshot,
			ResolveSymbolLocation(definitionProvider, GetSnapshotText(snapshot), symbolName, discriminator));
	}

	/// <summary>
	/// Resolves the definition at the specified offset through a position-based resolver.
	/// </summary>
	/// <remarks>
	/// The entry point for position-based providers - for example an LSP-backed resolver that answers a
	/// definition request for a document position - that resolve a location without a hover or symbol
	/// step. Converting the offset to provider coordinates (for example a line and column) is part of the
	/// resolver, because the coordinate convention belongs to the provider protocol.
	/// </remarks>
	/// <param name="positionResolver">
	/// The callback that resolves a definition location from the document snapshot and a zero-based
	/// offset, or <see langword="null"/> when no definition can be resolved.
	/// </param>
	/// <param name="snapshot">The document snapshot the offset addresses.</param>
	/// <param name="offset">
	/// The zero-based offset into <paramref name="snapshot"/> to resolve, including the offset at the end
	/// of the document.
	/// </param>
	/// <returns>
	/// The resolved navigation target, or <see langword="null"/> when the offset is outside the snapshot
	/// or the resolver resolves no applicable location.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="positionResolver"/> or <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	public static TextDefinitionNavigationTarget? ResolveFromOffset(
		Func<ITextSnapshot, int, TextDefinitionLocation?> positionResolver,
		ITextSnapshot snapshot,
		int offset)
	{
		ArgumentNullException.ThrowIfNull(positionResolver);
		ArgumentNullException.ThrowIfNull(snapshot);

		if (!IsOffsetInSnapshot(snapshot, offset))
			return null;

		return CreateTarget(snapshot, positionResolver(snapshot, offset));
	}

	/// <summary>
	/// Resolves the definition of the symbol hovered at the specified offset through asynchronous
	/// resolvers.
	/// </summary>
	/// <remarks>
	/// The asynchronous counterpart of
	/// <see cref="ResolveFromHover(ITextDefinitionProvider, ITextHoverProvider, ITextSnapshot, int)"/> for
	/// providers that may perform I/O or blocking work. The document text is materialized once, so both
	/// resolvers evaluate the same snapshot.
	/// </remarks>
	/// <param name="definitionResolverAsync">The callback that resolves definition locations asynchronously.</param>
	/// <param name="hoverResolverAsync">The callback that resolves the hovered symbol asynchronously.</param>
	/// <param name="snapshot">The document snapshot the offset addresses.</param>
	/// <param name="offset">
	/// The zero-based offset into <paramref name="snapshot"/> to resolve, including the offset at the end
	/// of the document.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the resolver requests.</param>
	/// <returns>
	/// The resolved navigation target, or <see langword="null"/> when the offset is outside the snapshot,
	/// the hover resolver resolves no symbol, or the symbol resolves to no applicable location.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="definitionResolverAsync"/>, <paramref name="hoverResolverAsync"/>, or
	/// <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	public static async Task<TextDefinitionNavigationTarget?> ResolveFromHoverAsync(
		Func<TextDefinitionRequest, CancellationToken, Task<TextDefinitionLocation?>> definitionResolverAsync,
		Func<TextHoverRequest, CancellationToken, Task<TextHoverInfo?>> hoverResolverAsync,
		ITextSnapshot snapshot,
		int offset,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);
		ArgumentNullException.ThrowIfNull(hoverResolverAsync);
		ArgumentNullException.ThrowIfNull(snapshot);

		cancellationToken.ThrowIfCancellationRequested();

		if (!IsOffsetInSnapshot(snapshot, offset))
			return null;

		string documentText = GetSnapshotText(snapshot);
		TextHoverInfo? hoverInfo = await hoverResolverAsync(
			new TextHoverRequest(documentText, offset), cancellationToken).ConfigureAwait(false);

		if (hoverInfo is null || string.IsNullOrWhiteSpace(hoverInfo.SymbolName))
			return null;

		TextDefinitionLocation? location = await definitionResolverAsync(
			new TextDefinitionRequest(documentText, hoverInfo.SymbolName, hoverInfo.DefinitionDiscriminator),
			cancellationToken).ConfigureAwait(false);

		return CreateTarget(snapshot, location);
	}

	/// <summary>
	/// Resolves the definition of the supplied symbol name through an asynchronous definition resolver.
	/// </summary>
	/// <param name="definitionResolverAsync">The callback that resolves definition locations asynchronously.</param>
	/// <param name="snapshot">The document snapshot that provides the provider's document context.</param>
	/// <param name="symbolName">
	/// The symbol name to resolve; a <see langword="null"/>, empty, or whitespace-only name reports no
	/// navigation before the resolver is called.
	/// </param>
	/// <param name="discriminator">
	/// The optional language-specific discriminator that disambiguates the symbol's definition.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the resolver request.</param>
	/// <returns>
	/// The resolved navigation target, or <see langword="null"/> when the name is blank or the resolver
	/// resolves no applicable location.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="definitionResolverAsync"/> or <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	public static async Task<TextDefinitionNavigationTarget?> ResolveFromSymbolAsync(
		Func<TextDefinitionRequest, CancellationToken, Task<TextDefinitionLocation?>> definitionResolverAsync,
		ITextSnapshot snapshot,
		string? symbolName,
		TextDefinitionDiscriminator? discriminator = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);
		ArgumentNullException.ThrowIfNull(snapshot);

		cancellationToken.ThrowIfCancellationRequested();

		if (string.IsNullOrWhiteSpace(symbolName))
			return null;

		TextDefinitionLocation? location = await definitionResolverAsync(
			new TextDefinitionRequest(GetSnapshotText(snapshot), symbolName, discriminator),
			cancellationToken).ConfigureAwait(false);

		return CreateTarget(snapshot, location);
	}

	/// <summary>
	/// Resolves the definition at the specified offset through an asynchronous position-based resolver.
	/// </summary>
	/// <remarks>
	/// The asynchronous counterpart of
	/// <see cref="ResolveFromOffset(Func{ITextSnapshot, int, TextDefinitionLocation?}, ITextSnapshot, int)"/>.
	/// </remarks>
	/// <param name="positionResolverAsync">
	/// The callback that resolves a definition location asynchronously from the document snapshot and a
	/// zero-based offset, or <see langword="null"/> when no definition can be resolved.
	/// </param>
	/// <param name="snapshot">The document snapshot the offset addresses.</param>
	/// <param name="offset">
	/// The zero-based offset into <paramref name="snapshot"/> to resolve, including the offset at the end
	/// of the document.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the resolver request.</param>
	/// <returns>
	/// The resolved navigation target, or <see langword="null"/> when the offset is outside the snapshot
	/// or the resolver resolves no applicable location.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="positionResolverAsync"/> or <paramref name="snapshot"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	public static async Task<TextDefinitionNavigationTarget?> ResolveFromOffsetAsync(
		Func<ITextSnapshot, int, CancellationToken, Task<TextDefinitionLocation?>> positionResolverAsync,
		ITextSnapshot snapshot,
		int offset,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(positionResolverAsync);
		ArgumentNullException.ThrowIfNull(snapshot);

		cancellationToken.ThrowIfCancellationRequested();

		if (!IsOffsetInSnapshot(snapshot, offset))
			return null;

		TextDefinitionLocation? location = await positionResolverAsync(snapshot, offset, cancellationToken)
			.ConfigureAwait(false);

		return CreateTarget(snapshot, location);
	}

	private static TextDefinitionLocation? ResolveSymbolLocation(
		ITextDefinitionProvider definitionProvider,
		string documentText,
		string symbolName,
		TextDefinitionDiscriminator? discriminator)
		=> definitionProvider.GetDefinition(new TextDefinitionRequest(documentText, symbolName, discriminator));

	private static TextDefinitionNavigationTarget? CreateTarget(
		ITextSnapshot snapshot,
		TextDefinitionLocation? location)
	{
		if (location is null)
			return null;

		// A location in another document is the host's to open, so its coordinates are not validated
		// against a document they do not address.
		if (location.DocumentId is not null)
			return new TextDefinitionNavigationTarget(location);

		TextPosition start = location.NavigationStart;

		// A negative line or character, or a line beyond the snapshot, cannot identify a location, so it is
		// rejected symmetrically with the offset bounds instead of being clamped.
		if (start.Line < 0 || start.Line >= snapshot.LineCount || start.Character < 0)
			return null;

		return new TextDefinitionNavigationTarget(location, start);
	}

	private static bool IsOffsetInSnapshot(ITextSnapshot snapshot, int offset)
		=> offset >= 0 && offset <= snapshot.TextLength;

	// The request contracts carry the snapshot text rather than the snapshot itself, so the text is
	// materialized once per resolution and shared by the hover probe and its definition request. A
	// string-backed snapshot returns its backing instance for the full range.
	private static string GetSnapshotText(ITextSnapshot snapshot)
		=> snapshot.GetText(0, snapshot.TextLength);
}
