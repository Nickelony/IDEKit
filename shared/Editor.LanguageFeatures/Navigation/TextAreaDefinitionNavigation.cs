#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Navigation;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Navigation;
#endif
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;
#endif

/// <summary>
/// Applies a definition navigation resolved by <see cref="TextDefinitionNavigator"/> to the editor's
/// <see cref="TextArea"/>.
/// </summary>
/// <remarks>
/// <para>
/// The navigation policy itself - which provider answers which probe, when a probe resolves to no navigation,
/// and whether a location is applicable to the document the request was resolved from - lives in the neutral
/// <see cref="TextDefinitionNavigator"/>. This type owns what is specific to the editor: the thread-affinity
/// check, the captured document snapshot, the staleness check of the asynchronous forms, the marshalling of the
/// resolver calls back to the text area's thread, and the caret and scroll application.
/// </para>
/// <para>
/// A resolved in-document position moves the caret to the start of the location's selection range (or of its
/// target range when no selection range is present; the shared
/// <see cref="TextDefinitionLocation.NavigationStart"/> applies that rule) without selecting text, and a column
/// past the end of its line is clamped by the base navigation helper. A location that names another document is
/// reported through the caller's optional cross-file callback, or as not navigated when the callback is omitted,
/// because opening the target document stays host-owned. A text area whose document was cleared (or never
/// assigned) reports not navigated instead of failing on the missing document.
/// </para>
/// <para>
/// The synchronous forms invoke in-memory providers on the calling thread; the asynchronous forms await resolver
/// delegates and are the right choice when a provider may perform I/O or blocking work (for example an
/// LSP-backed resolver). All forms run on the thread that owns the text area and fail fast with the dispatcher's
/// threading error otherwise; the asynchronous forms also marshal their resolver calls and the applied navigation
/// back to that thread explicitly, and they apply a within-document target only while the document still carries
/// the captured snapshot. A cross-file location is still reported through the callback, whose coordinates address
/// the other document. Both a hover-first/symbol form (name-catalog providers) and an offset-first form
/// (position-based providers, which receives the document snapshot and the offset directly) are available; a host
/// built on the editor's <c>TextEditor</c> passes <c>editor.TextArea</c>.
/// </para>
/// </remarks>
public static class TextAreaDefinitionNavigation
{
	/// <summary>
	/// Resolves the symbol at the specified offset through the hover provider and navigates to its definition.
	/// </summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="definitionProvider">The provider that resolves definition locations.</param>
	/// <param name="hoverProvider">The provider that resolves the hovered symbol.</param>
	/// <param name="offset">The zero-based document offset to resolve, including the offset at the end of the document.</param>
	/// <param name="tryNavigateCrossFileDefinition">
	/// An optional callback that navigates to a definition in another file; a cross-file location is reported
	/// as not navigated when the callback is omitted. See the type remarks.
	/// </param>
	/// <returns><see langword="true"/> when a definition location was found and navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="definitionProvider"/>, or <paramref name="hoverProvider"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	public static bool TryGoToDefinition(
		this TextArea textArea,
		ITextDefinitionProvider definitionProvider,
		ITextHoverProvider hoverProvider,
		int offset,
		Func<TextDefinitionLocation, bool>? tryNavigateCrossFileDefinition = null)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionProvider);
		ArgumentNullException.ThrowIfNull(hoverProvider);

		return NavigateToDefinition(
			textArea,
			snapshot => TextDefinitionNavigator.ResolveFromHover(definitionProvider, hoverProvider, snapshot, offset),
			tryNavigateCrossFileDefinition);
	}

	/// <summary>
	/// Resolves the definition at the specified offset through an offset-based definition resolver and
	/// navigates to it.
	/// </summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="definitionResolver">
	/// The callback that resolves a definition location from the document snapshot and a zero-based offset,
	/// or <see langword="null"/> when no definition can be resolved.
	/// </param>
	/// <param name="offset">The zero-based document offset to resolve, including the offset at the end of the document.</param>
	/// <param name="tryNavigateCrossFileDefinition">
	/// An optional callback that navigates to a definition in another file; a cross-file location is reported
	/// as not navigated when the callback is omitted. See the type remarks.
	/// </param>
	/// <returns><see langword="true"/> when a definition location was found and navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The offset-first entry point for position-based providers - for example an LSP-backed resolver that
	/// answers a definition request for a document position - that resolve a location without a hover or symbol
	/// step. The resolver receives the document snapshot and the zero-based offset; the snapshot text is
	/// available through <see cref="ITextSnapshot.GetText(int, int)"/>, and converting the offset to provider
	/// coordinates (for example a line and column) is part of the resolver, because the coordinate convention
	/// belongs to the provider protocol. <see cref="TryGoToDefinition(TextArea, ITextDefinitionProvider, ITextHoverProvider, int, Func{TextDefinitionLocation, bool}?)"/>
	/// remains the convenience entry point for name-catalog providers that resolve definitions by symbol.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="definitionResolver"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	public static bool TryGoToDefinitionAtOffset(
		this TextArea textArea,
		Func<ITextSnapshot, int, TextDefinitionLocation?> definitionResolver,
		int offset,
		Func<TextDefinitionLocation, bool>? tryNavigateCrossFileDefinition = null)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionResolver);

		return NavigateToDefinition(
			textArea,
			snapshot => TextDefinitionNavigator.ResolveFromOffset(definitionResolver, snapshot, offset),
			tryNavigateCrossFileDefinition);
	}

	/// <summary>
	/// Resolves and navigates to the definition of the supplied symbol name.
	/// </summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="definitionProvider">The provider that resolves definition locations.</param>
	/// <param name="symbolName">The symbol name to resolve; a <see langword="null"/> or whitespace-only name reports not navigated.</param>
	/// <param name="discriminator">The optional language-specific discriminator that disambiguates the symbol's definition.</param>
	/// <param name="tryNavigateCrossFileDefinition">
	/// An optional callback that navigates to a definition in another file; a cross-file location is reported
	/// as not navigated when the callback is omitted. See the type remarks.
	/// </param>
	/// <returns><see langword="true"/> when a definition location was found and navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="definitionProvider"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	public static bool TryGoToDefinitionBySymbol(
		this TextArea textArea,
		ITextDefinitionProvider definitionProvider,
		string? symbolName,
		TextDefinitionDiscriminator? discriminator = null,
		Func<TextDefinitionLocation, bool>? tryNavigateCrossFileDefinition = null)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionProvider);

		return NavigateToDefinition(
			textArea,
			snapshot => TextDefinitionNavigator.ResolveFromSymbol(definitionProvider, snapshot, symbolName, discriminator),
			tryNavigateCrossFileDefinition);
	}

	// Captures the document the navigation is resolved against. The text is materialized once and shared by
	// the probe and the definition request; the snapshot also supplies the file name and the line count the
	// position validation needs. A document-less text area has nothing to navigate, so the helper reports
	// "not navigated" instead of failing on the missing document.
	private static StringTextSnapshot? CreateSnapshot(TextArea textArea)
	{
		TextDocument? document = textArea.Document;

		return document is null ? null : new StringTextSnapshot(document.Text, document.FileName);
	}

	// The shared body of every synchronous entry point: the thread-affinity check, the captured document
	// snapshot, and the applied target are identical; only the resolver that produces the target differs.
	private static bool NavigateToDefinition(
		TextArea textArea,
		Func<StringTextSnapshot, TextDefinitionNavigationTarget?> resolve,
		Func<TextDefinitionLocation, bool>? tryNavigateCrossFileDefinition)
	{
		textArea.Dispatcher.VerifyAccess();

		if (CreateSnapshot(textArea) is not { } snapshot)
			return false;

		return ApplyTarget(textArea, resolve(snapshot), tryNavigateCrossFileDefinition);
	}

	// The shared body of every asynchronous entry point: the thread-affinity and cancellation checks, the
	// captured document snapshot and version, and the applied target are identical; only the resolver that
	// produces the target differs. The document, its snapshot, and its version are captured in one
	// editor-thread turn, so the version stamp describes exactly the text the resolver receives.
	private static async Task<bool> NavigateToDefinitionAsync(
		TextArea textArea,
		Func<StringTextSnapshot, CancellationToken, Task<TextDefinitionNavigationTarget?>> resolveAsync,
		Func<TextDefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync,
		CancellationToken cancellationToken)
	{
		textArea.Dispatcher.VerifyAccess();
		cancellationToken.ThrowIfCancellationRequested();

		TextDocument? document = textArea.Document;

		if (document is null)
			return false;

		var snapshot = new StringTextSnapshot(document.Text, document.FileName);
		ITextSourceVersion version = document.Version;

		TextDefinitionNavigationTarget? target = await resolveAsync(snapshot, cancellationToken).ConfigureAwait(true);

		return await ApplyTargetAsync(
			textArea,
			document,
			version,
			target,
			tryNavigateCrossFileDefinitionAsync,
			cancellationToken).ConfigureAwait(true);
	}

	private static bool ApplyTarget(
		TextArea textArea,
		TextDefinitionNavigationTarget? target,
		Func<TextDefinitionLocation, bool>? tryNavigateCrossFileDefinition)
	{
		if (target is null)
			return false;

		// A target without an in-document start names another document, which the host opens.
		if (target.DocumentStart is not { } start)
			return tryNavigateCrossFileDefinition?.Invoke(target.Location) ?? false;

		return ApplyPosition(textArea, start);
	}

	/// <summary>
	/// Resolves the symbol at the specified offset through an asynchronous hover resolver and navigates to its
	/// definition.
	/// </summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="definitionResolverAsync">The callback that resolves definition locations asynchronously.</param>
	/// <param name="hoverResolverAsync">The callback that resolves the hovered symbol asynchronously.</param>
	/// <param name="offset">The zero-based document offset to resolve, including the offset at the end of the document.</param>
	/// <param name="tryNavigateCrossFileDefinitionAsync">
	/// An optional callback that navigates to a definition in another file; a cross-file location is reported
	/// as not navigated when the callback is omitted. See the type remarks.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the resolver requests.</param>
	/// <returns><see langword="true"/> when a definition location was found and navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The asynchronous counterpart of <see cref="TryGoToDefinition(TextArea, ITextDefinitionProvider, ITextHoverProvider, int, Func{TextDefinitionLocation, bool}?)"/>
	/// for providers that may perform I/O or blocking work. The document text is materialized once per definition
	/// navigation, so both resolvers evaluate the same snapshot; resolver calls and the applied definition
	/// navigation are marshalled back to the thread that owns the text area explicitly (see the type remarks).
	/// A within-document target is applied only while the document still carries the captured snapshot, so a
	/// document that changed or was replaced while a resolver was running reports <see langword="false"/>.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="definitionResolverAsync"/>, or
	/// <paramref name="hoverResolverAsync"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	public static async Task<bool> TryGoToDefinitionAsync(
		this TextArea textArea,
		Func<TextDefinitionRequest, CancellationToken, Task<TextDefinitionLocation?>> definitionResolverAsync,
		Func<TextHoverRequest, CancellationToken, Task<TextHoverInfo?>> hoverResolverAsync,
		int offset,
		Func<TextDefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);
		ArgumentNullException.ThrowIfNull(hoverResolverAsync);

		// The resolver calls are dispatched onto the text area's thread, which the documented affinity
		// guarantee requires; the navigator applies the shared policy to the captured snapshot.
		return await NavigateToDefinitionAsync(
			textArea,
			(snapshot, token) => TextDefinitionNavigator.ResolveFromHoverAsync(
				(request, resolverToken) => DispatcherInvocation.Run(
					textArea.Dispatcher,
					() => definitionResolverAsync(request, resolverToken)),
				(request, resolverToken) => DispatcherInvocation.Run(
					textArea.Dispatcher,
					() => hoverResolverAsync(request, resolverToken)),
				snapshot,
				offset,
				token),
			tryNavigateCrossFileDefinitionAsync,
			cancellationToken).ConfigureAwait(true);
	}

	/// <summary>
	/// Resolves the definition at the specified offset through an asynchronous offset-based definition
	/// resolver and navigates to it.
	/// </summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="definitionResolverAsync">
	/// The callback that resolves a definition location asynchronously from the document snapshot and a
	/// zero-based offset, or <see langword="null"/> when no definition can be resolved.
	/// </param>
	/// <param name="offset">The zero-based document offset to resolve, including the offset at the end of the document.</param>
	/// <param name="tryNavigateCrossFileDefinitionAsync">
	/// An optional callback that navigates to a definition in another file; a cross-file location is reported
	/// as not navigated when the callback is omitted. See the type remarks.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the resolver request.</param>
	/// <returns><see langword="true"/> when a definition location was found and navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The asynchronous counterpart of <see cref="TryGoToDefinitionAtOffset(TextArea, Func{ITextSnapshot, int, TextDefinitionLocation?}, int, Func{TextDefinitionLocation, bool}?)"/>
	/// for providers that may perform I/O or blocking work. The document snapshot is captured once per definition
	/// navigation, so the resolver evaluates the same snapshot the offset addresses; resolver calls and the
	/// applied navigation are marshalled back to the thread that owns the text area explicitly (see the type
	/// remarks).
	/// A within-document target is applied only while the document still carries the captured snapshot, so a
	/// document that changed or was replaced while the resolver was running reports <see langword="false"/>.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="definitionResolverAsync"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>, or
	/// <paramref name="tryNavigateCrossFileDefinitionAsync"/> returned a <see langword="null"/> task.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	public static async Task<bool> TryGoToDefinitionAtOffsetAsync(
		this TextArea textArea,
		Func<ITextSnapshot, int, CancellationToken, Task<TextDefinitionLocation?>> definitionResolverAsync,
		int offset,
		Func<TextDefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);

		textArea.Dispatcher.VerifyAccess();
		cancellationToken.ThrowIfCancellationRequested();

		// The document, its snapshot, and its version are captured in one editor-thread turn, so the version
		// stamp describes exactly the text the resolver receives.
		TextDocument? document = textArea.Document;

		if (document is null)
			return false;

		var snapshot = new StringTextSnapshot(document.Text, document.FileName);
		ITextSourceVersion version = document.Version;

		// The resolver call is dispatched onto the text area's thread, which the documented affinity guarantee
		// requires; the navigator applies the shared policy to the captured snapshot.
		TextDefinitionNavigationTarget? target = await TextDefinitionNavigator.ResolveFromOffsetAsync(
			(positionSnapshot, positionOffset, token) => DispatcherInvocation.Run(
				textArea.Dispatcher,
				() => definitionResolverAsync(positionSnapshot, positionOffset, token)),
			snapshot,
			offset,
			cancellationToken).ConfigureAwait(true);

		return await ApplyTargetAsync(
			textArea,
			document,
			version,
			target,
			tryNavigateCrossFileDefinitionAsync,
			cancellationToken).ConfigureAwait(true);
	}

	/// <summary>
	/// Resolves and navigates to the definition of the supplied symbol name through an asynchronous definition
	/// resolver.
	/// </summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="definitionResolverAsync">The callback that resolves definition locations asynchronously.</param>
	/// <param name="symbolName">The symbol name to resolve; a <see langword="null"/> or whitespace-only name reports not navigated.</param>
	/// <param name="discriminator">The optional language-specific discriminator that disambiguates the symbol's definition.</param>
	/// <param name="tryNavigateCrossFileDefinitionAsync">
	/// An optional callback that navigates to a definition in another file; a cross-file location is reported
	/// as not navigated when the callback is omitted. See the type remarks.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the resolver request.</param>
	/// <returns><see langword="true"/> when a definition location was found and navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The asynchronous counterpart of <see cref="TryGoToDefinitionBySymbol(TextArea, ITextDefinitionProvider, string?, TextDefinitionDiscriminator?, Func{TextDefinitionLocation, bool}?)"/>
	/// for providers that may perform I/O or blocking work; resolver calls and the applied definition navigation
	/// are marshalled back to the thread that owns the text area explicitly (see the type remarks).
	/// A within-document target is applied only while the document still carries the captured snapshot, so a
	/// document that changed or was replaced while the resolver was running reports <see langword="false"/>.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="definitionResolverAsync"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>, or
	/// <paramref name="tryNavigateCrossFileDefinitionAsync"/> returned a <see langword="null"/> task.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	public static async Task<bool> TryGoToDefinitionBySymbolAsync(
		this TextArea textArea,
		Func<TextDefinitionRequest, CancellationToken, Task<TextDefinitionLocation?>> definitionResolverAsync,
		string? symbolName,
		TextDefinitionDiscriminator? discriminator = null,
		Func<TextDefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);

		return await NavigateToDefinitionAsync(
			textArea,
			(snapshot, token) => string.IsNullOrWhiteSpace(symbolName)
				? Task.FromResult<TextDefinitionNavigationTarget?>(null)
				: TextDefinitionNavigator.ResolveFromSymbolAsync(
					(request, resolverToken) => DispatcherInvocation.Run(
						textArea.Dispatcher,
						() => definitionResolverAsync(request, resolverToken)),
					snapshot,
					symbolName,
					discriminator,
					token),
			tryNavigateCrossFileDefinitionAsync,
			cancellationToken).ConfigureAwait(true);
	}

	// A target without an in-document start names another document, which the host opens through the callback;
	// the callback is dispatched onto the text area's thread like the resolvers. An in-document target addresses
	// the captured document, so it is applied only while that document still carries the snapshot the resolver
	// evaluated; the check and the application run as one dispatcher callback, so no edit can interleave between
	// them. A cross-document target is not checked here: its coordinates address the other document and are
	// unaffected by changes to this one.
	private static async Task<bool> ApplyTargetAsync(
		TextArea textArea,
		TextDocument document,
		ITextSourceVersion version,
		TextDefinitionNavigationTarget? target,
		Func<TextDefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync,
		CancellationToken cancellationToken)
	{
		if (target is null)
			return false;

		if (target.DocumentStart is not { } start)
		{
			if (tryNavigateCrossFileDefinitionAsync is null)
				return false;

			// The host callback is invoked on the text area's thread through a non-blocking hop, so a busy
			// dispatcher cannot deadlock this continuation; the returned task is awaited afterwards. A
			// callback that returns no task violates the delegate contract, which no caller can act on as a
			// parameter error, so it is reported as an invalid operation; a callback that throws propagates
			// to the caller, exactly like the resolver callbacks.
			Task<bool>? navigation = null;

			await DispatcherInvocation.RunAsync(
				textArea.Dispatcher,
				() => { navigation = tryNavigateCrossFileDefinitionAsync(target.Location, cancellationToken); }).ConfigureAwait(true);

			if (navigation is null)
			{
				throw new InvalidOperationException(
					"The cross-file definition navigation callback returned a null task.");
			}

			return await navigation.ConfigureAwait(true);
		}

		// The currency check and the application are marshalled as one dispatcher callback through a
		// non-blocking hop, so no edit can interleave between them and a busy dispatcher cannot deadlock
		// this continuation.
		bool navigated = false;

		await DispatcherInvocation.RunAsync(
			textArea.Dispatcher,
			() => { navigated = IsCurrentSnapshot(textArea, document, version) && ApplyPosition(textArea, start); }).ConfigureAwait(true);

		return navigated;
	}

	// Determines whether the text area still carries the document version a location was resolved from.
	// The editor replaces the document version object on every change, so an unchanged version reference
	// proves the resolver evaluated text the document still describes; the identity check rejects a document
	// that was replaced entirely.
	private static bool IsCurrentSnapshot(TextArea textArea, TextDocument document, ITextSourceVersion version)
		=> ReferenceEquals(textArea.Document, document) && ReferenceEquals(document.Version, version);

	private static bool ApplyPosition(TextArea textArea, TextPosition start)
	{
		// CreateCaretLocation clamps the line and column and creates a zero-length selection, so the
		// caret is placed without selecting the line and typing after a definition navigation replaces nothing. The
		// column addition saturates so a start character at int.MaxValue still clamps to the line end instead of
		// overflowing into a negative column. The file path is only carried for context; without one, the
		// document's own path (or an empty path for an unsaved document) is recorded.
		NavigationLocation navigationLocation = TextAreaNavigationOperations.CreateCaretLocation(
			textArea,
			textArea.Document.FileName ?? string.Empty,
			new TextLocation(start.Line + 1, (int)Math.Min((long)start.Character + 1, int.MaxValue)));

		textArea.ApplyLocation(navigationLocation);
		return true;
	}
}
