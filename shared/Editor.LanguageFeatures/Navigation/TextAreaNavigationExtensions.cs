using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
#if AVALONIAEDIT
using Avalonia.Input;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Text;
using Nickelony.IDEKit.AvaloniaEdit.Navigation;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
using PointerModifiers = Avalonia.Input.KeyModifiers;
#else
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Text;
using Nickelony.IDEKit.AvalonEdit.Navigation;
using Nickelony.IDEKit.AvalonEdit.Threading;
using PointerModifiers = System.Windows.Input.ModifierKeys;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;

#endif

/// <summary>Navigates the editor's <see cref="TextArea"/> to a definition that <see cref="DefinitionResolver"/> resolves.</summary>
/// <remarks>
/// <para>
/// The navigation policy itself - which provider answers which probe, when a probe resolves to no navigation, and whether a location
/// is applicable to the document the request was resolved from - lives in the neutral <see cref="DefinitionResolver"/>. This type owns
/// what is specific to the editor: the thread-affinity check, the captured document snapshot, the staleness check of the asynchronous forms,
/// the marshaling of the resolver calls back to the text area's thread, the caret and scroll application, and the pointer gesture.
/// </para>
/// <para>
/// A resolved in-document position moves the caret to the start of the location's selection range (or of its target range when no selection
/// range is present; the shared <see cref="DefinitionLocation.NavigationStart"/> applies that rule) without selecting text, and a column past the end
/// of its line is clamped to the line end. A location that names another document is reported through the caller's optional cross-file callback,
/// or as not navigated when the callback is omitted, because opening the target document stays host-owned. A text area whose document was cleared
/// (or never assigned) reports not navigated instead of failing on the missing document.
/// </para>
/// <para>
/// Every entry point awaits resolver delegates, so a provider that performs I/O or blocking work (for example a language server) fits.
/// They run on the thread that owns the text area and fail fast with the dispatcher's threading error otherwise, marshal their resolver
/// calls and the applied navigation back to that thread explicitly, and apply a within-document target only while the document
/// still carries the captured snapshot, so a document that changed or was replaced while a resolver was running reports <see langword="false"/>.
/// A cross-file location is still reported through the callback, whose coordinates address the other document. A token canceled before the target
/// is applied applies nothing and cancels the call.
/// </para>
/// </remarks>
public static class TextAreaNavigationExtensions
{
	// The shared body of every asynchronous entry point: the thread-affinity and cancellation checks, the
	// captured document snapshot and version, and the applied target are identical; only the resolver that
	// produces the target differs. The document, its snapshot, and its version are captured in one
	// editor-thread turn, so the version stamp describes exactly the text the resolver receives.
	private static async Task<bool> NavigateToDefinitionAsync(
		TextArea textArea,
		Func<ITextSnapshot, CancellationToken, Task<DefinitionNavigationTarget?>> resolveAsync,
		Func<DefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync,
		CancellationToken cancellationToken
	)
	{
		textArea.Dispatcher.VerifyAccess();
		cancellationToken.ThrowIfCancellationRequested();

		TextDocument? document = textArea.Document;

		if (document is null)
			return false;

		// The lazy snapshot captures the document's text source in constant time; the text is materialized
		// only if the resolver reads it, which can happen off the editor thread.
		var snapshot = new TextDocumentSnapshot(document);
		ITextSourceVersion version = document.Version;

		DefinitionNavigationTarget? target = await resolveAsync(snapshot, cancellationToken).ConfigureAwait(true);

		return await ApplyTargetAsync(textArea, document, version, target, tryNavigateCrossFileDefinitionAsync, cancellationToken)
			.ConfigureAwait(true);
	}

	/// <summary>Resolves the definition of the symbol at the specified offset through a definition provider and navigates to it.</summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="documentId">The identifier of the document the text area shows; the request carries it.</param>
	/// <param name="provider">The provider that resolves the definition, for example a language-server provider.</param>
	/// <param name="offset">The zero-based document offset to resolve, including the offset at the end of the document.</param>
	/// <param name="tryNavigateCrossFileDefinitionAsync">
	/// An optional callback that navigates to a definition in another file; see <see cref="TextAreaNavigationExtensions"/> for how a cross-file
	/// location is reported.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the provider request.</param>
	/// <returns><see langword="true"/> when a definition location was found and navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="documentId"/>, or <paramref name="provider"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>, or <paramref name="tryNavigateCrossFileDefinitionAsync"/> returned
	/// a <see langword="null"/> task.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	/// <remarks>
	/// The entry point for a host with an <see cref="IDefinitionProvider"/>, a language-server provider included:
	/// the request names the identifier at the offset and carries the offset, so a provider that resolves by name and one that resolves by position
	/// both answer (<see cref="DefinitionResolver.ResolveAtOffsetAsync"/>). A location that names <paramref name="documentId"/>, which a language
	/// server returns for a definition in the same file, moves the caret in this text area instead of reaching the cross-file callback.
	/// </remarks>
	public static async Task<bool> GoToDefinitionAsync(
		this TextArea textArea,
		string documentId,
		IDefinitionProvider provider,
		int offset,
		Func<DefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(provider);

		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		return await NavigateToDefinitionAsync(
				textArea,

				resolveAsync: (snapshot, token) =>
					DefinitionResolver.ResolveAtOffsetAsync(
						definitionResolverAsync: (request, resolverToken) =>
							UiDispatcher.From(textArea.Dispatcher).Invoke(() => provider.GetDefinitionAsync(request, resolverToken)),

						documentId,
						snapshot,
						offset,
						cancellationToken: token
					),

				tryNavigateCrossFileDefinitionAsync,
				cancellationToken
			)
			.ConfigureAwait(true);
	}

	/// <summary>Resolves the symbol at the specified offset through an asynchronous hover resolver and navigates to its definition.</summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="documentId">The identifier of the document the text area shows; the resolver requests carry it.</param>
	/// <param name="definitionResolverAsync">The callback that resolves definition locations asynchronously.</param>
	/// <param name="hoverResolverAsync">The callback that resolves the hovered symbol asynchronously.</param>
	/// <param name="offset">The zero-based document offset to resolve, including the offset at the end of the document.</param>
	/// <param name="tryNavigateCrossFileDefinitionAsync">
	/// An optional callback that navigates to a definition in another file; see <see cref="TextAreaNavigationExtensions"/> for how a cross-file
	/// location is reported.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the resolver requests.</param>
	/// <returns><see langword="true"/> when a definition location was found and navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="documentId"/>, <paramref name="definitionResolverAsync"/>,
	/// or <paramref name="hoverResolverAsync"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>, or <paramref name="tryNavigateCrossFileDefinitionAsync"/> returned
	/// a <see langword="null"/> task.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	/// <remarks>
	/// The entry point for a host whose hover names the symbol through <see cref="HoverInformation.SymbolName"/>, such as an in-process
	/// provider that resolves a definition by name. A language-server hover names no symbol, so against a server this form navigates
	/// nowhere and a host calls <see cref="GoToDefinitionAsync"/> instead. The definition resolver receives the symbol with the hovered offset,
	/// and the document snapshot is captured once per definition navigation; see <see cref="TextAreaNavigationExtensions"/> for the thread marshaling
	/// and the staleness check a within-document target passes.
	/// </remarks>
	public static async Task<bool> GoToDefinitionByHoverAsync(
		this TextArea textArea,
		string documentId,
		Func<DefinitionRequest, CancellationToken, Task<DefinitionLocation?>> definitionResolverAsync,
		Func<HoverRequest, CancellationToken, Task<HoverInformation?>> hoverResolverAsync,
		int offset,
		Func<DefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);
		ArgumentNullException.ThrowIfNull(hoverResolverAsync);

		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		// The resolver calls are dispatched onto the text area's thread, which the documented affinity
		// guarantee requires; the resolver applies the shared policy to the captured snapshot.
		return await NavigateToDefinitionAsync(
				textArea,

				resolveAsync: (snapshot, token) =>
					HoverDefinitionResolver.ResolveAsync(
						definitionResolverAsync: (request, resolverToken) =>
							UiDispatcher.From(textArea.Dispatcher).Invoke(() => definitionResolverAsync(request, resolverToken)),

						hoverResolverAsync: (request, resolverToken) =>
							UiDispatcher.From(textArea.Dispatcher).Invoke(() => hoverResolverAsync(request, resolverToken)),

						documentId,
						snapshot,
						offset,
						cancellationToken: token
					),

				tryNavigateCrossFileDefinitionAsync,
				cancellationToken
			)
			.ConfigureAwait(true);
	}

	/// <summary>Resolves and navigates to the definition of the supplied symbol name through an asynchronous definition resolver.</summary>
	/// <param name="textArea">The text area to navigate.</param>
	/// <param name="documentId">The identifier of the document the text area shows; the resolver request carries it.</param>
	/// <param name="definitionResolverAsync">The callback that resolves definition locations asynchronously.</param>
	/// <param name="symbolName">The symbol name to resolve; a <see langword="null"/> or whitespace-only name reports not navigated.</param>
	/// <param name="discriminator">The optional language-specific discriminator that disambiguates the symbol's definition.</param>
	/// <param name="tryNavigateCrossFileDefinitionAsync">
	/// An optional callback that navigates to a definition in another file; see <see cref="TextAreaNavigationExtensions"/> for how a cross-file
	/// location is reported.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the resolver request.</param>
	/// <returns><see langword="true"/> when a definition location was found and navigation succeeded; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="documentId"/>, or <paramref name="definitionResolverAsync"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the thread that owns <paramref name="textArea"/>, or <paramref name="tryNavigateCrossFileDefinitionAsync"/> returned
	/// a <see langword="null"/> task.
	/// </exception>
	/// <exception cref="OperationCanceledException">The operation was canceled.</exception>
	/// <remarks>
	/// The entry point for a name-catalog provider that resolves a symbol without a position, so the request carries no caret offset.
	/// A language-server provider resolves by position and answers no definition without an offset, so against a server this form never navigates
	/// and a host calls <see cref="GoToDefinitionAsync"/> instead. See <see cref="TextAreaNavigationExtensions"/> for the thread marshaling
	/// and the staleness check a within-document target passes.
	/// </remarks>
	public static async Task<bool> GoToDefinitionBySymbolAsync(
		this TextArea textArea,
		string documentId,
		Func<DefinitionRequest, CancellationToken, Task<DefinitionLocation?>> definitionResolverAsync,
		string? symbolName,
		DefinitionDiscriminator? discriminator = null,
		Func<DefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(definitionResolverAsync);

		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		return await NavigateToDefinitionAsync(
				textArea,

				resolveAsync: (snapshot, token) =>
					string.IsNullOrWhiteSpace(symbolName)
						? Task.FromResult<DefinitionNavigationTarget?>(null)
						: DefinitionResolver.ResolveFromSymbolAsync(
							definitionResolverAsync: (request, resolverToken) =>
								UiDispatcher.From(textArea.Dispatcher).Invoke(() => definitionResolverAsync(request, resolverToken)),

							documentId,
							snapshot,
							symbolName,
							discriminator,
							cancellationToken: token
						),

				tryNavigateCrossFileDefinitionAsync,
				cancellationToken
			)
			.ConfigureAwait(true);
	}

	/// <summary>Navigates to the definition of the symbol under a left click that holds the navigation modifiers, until disposed.</summary>
	/// <param name="textArea">The text area whose clicks navigate.</param>
	/// <param name="documentId">The identifier of the document the text area shows; every request carries it.</param>
	/// <param name="provider">The provider that resolves the definition, for example a language-server provider.</param>
	/// <param name="tryNavigateCrossFileDefinitionAsync">
	/// An optional callback that navigates to a definition in another file; see <see cref="TextAreaNavigationExtensions"/> for how a cross-file
	/// location is reported.
	/// </param>
	/// <param name="modifiers">
	/// The modifier keys a click holds to navigate, and no others. Where the parameter accepts <see langword="null"/>, that value selects
	/// the platform's command modifier: Meta on macOS and Control elsewhere.
	/// </param>
	/// <param name="logger">The logger that receives a failed navigation, or <see langword="null"/> to log nothing.</param>
	/// <returns>A subscription that detaches the gesture and cancels a navigation in flight when it is disposed.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="documentId"/>, or <paramref name="provider"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <remarks>
	/// <para>
	/// A single left press that holds exactly <paramref name="modifiers"/> over the text of a line is handled before the editor's
	/// own press handler, so it neither moves the caret nor starts a selection, and it navigates through <see cref="GoToDefinitionAsync"/>.
	/// A press without those modifiers, a double press, and a press off the text, such as past the end of a line or on a margin, stay plain clicks.
	/// </para>
	/// <para>
	/// The press is handled before its definition resolves, so a click that resolves no definition places the caret at the clicked position,
	/// where a plain click puts it, unless the document or the caret changed while the definition resolved. A later navigating click cancels
	/// the one in flight, and the later one wins. Attach and dispose on the text area's thread.
	/// </para>
	/// <para>
	/// After the text area replaces its document,
	/// a click there is a plain click and the navigation in flight is canceled, so a host attaches the gesture again for the new document,
	/// as the
	/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorLanguageFeaturesGuide.md#one-controller-per-document">document rule</see>
	/// says.
	/// </para>
	/// </remarks>
	public static IDisposable AttachPointerNavigation(
		this TextArea textArea,
		string documentId,
		IDefinitionProvider provider,
		Func<DefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync = null,
#if AVALONIAEDIT
		KeyModifiers? modifiers = null,
#else
		ModifierKeys modifiers = ModifierKeys.Control,
#endif
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(provider);

		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

#if AVALONIAEDIT
		PointerModifiers navigationModifiers = modifiers ?? CommandModifiers(OperatingSystem.IsMacOS());
#else
		PointerModifiers navigationModifiers = modifiers;
#endif

		return new PointerNavigationSession(
			textArea,
			documentId,
			provider,
			tryNavigateCrossFileDefinitionAsync,
			modifiers: navigationModifiers,
			logger: logger
		);
	}

#if AVALONIAEDIT
	/// <summary>The modifier that a platform's command gestures hold: Meta, the Command key, on macOS, and Control elsewhere.</summary>
	internal static KeyModifiers CommandModifiers(bool isMacOS) => isMacOS ? KeyModifiers.Meta : KeyModifiers.Control;
#endif

	// A target without an in-document start names another document, which the host opens through the callback;
	// the callback is dispatched onto the text area's thread like the resolvers. An in-document target addresses
	// the captured document, so it is applied only while that document still carries the snapshot the resolver
	// evaluated; the check and the application run as one dispatcher callback, so no edit can interleave between
	// them. A cross-document target is not checked here: its coordinates address the other document and are
	// unaffected by changes to this one. Each callback checks the token first, so a navigation that a later
	// click or a disposal canceled while it was in flight applies nothing, even when its resolver ignored the token.
	private static async Task<bool> ApplyTargetAsync(
		TextArea textArea,
		TextDocument document,
		ITextSourceVersion version,
		DefinitionNavigationTarget? target,
		Func<DefinitionLocation, CancellationToken, Task<bool>>? tryNavigateCrossFileDefinitionAsync,
		CancellationToken cancellationToken
	)
	{
		if (target is null)
			return false;

		bool canceled = false;

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

			await UiDispatcher
				.From(textArea.Dispatcher)
				.InvokeAsync(() =>
				{
					canceled = cancellationToken.IsCancellationRequested;

					if (!canceled)
						navigation = tryNavigateCrossFileDefinitionAsync(target.Location, cancellationToken);
				})
				.ConfigureAwait(true);

			if (canceled)
				throw new OperationCanceledException(cancellationToken);

			if (navigation is null)
				throw new InvalidOperationException("The cross-file definition navigation callback returned a null task.");

			return await navigation.ConfigureAwait(true);
		}

		// The cancellation and currency checks and the application are marshaled as one dispatcher callback
		// through a non-blocking hop, so no edit or later click can interleave between them and a busy
		// dispatcher cannot deadlock this continuation.
		bool navigated = false;

		await UiDispatcher
			.From(textArea.Dispatcher)
			.InvokeAsync(() =>
			{
				canceled = cancellationToken.IsCancellationRequested;
				navigated = !canceled && IsCurrentSnapshot(textArea, document, version) && ApplyPosition(textArea, start);
			})
			.ConfigureAwait(true);

		if (canceled)
			throw new OperationCanceledException(cancellationToken);

		return navigated;
	}

	// Determines whether the text area still carries the document version a location was resolved from.
	// The editor replaces the document version object on every change, so an unchanged version reference
	// proves the resolver evaluated text the document still describes; the identity check rejects a document
	// that was replaced entirely.
	internal static bool IsCurrentSnapshot(TextArea textArea, TextDocument document, ITextSourceVersion version) =>
		ReferenceEquals(textArea.Document, document) && ReferenceEquals(document.Version, version);

	private static bool ApplyPosition(TextArea textArea, TextPosition start)
	{
		// CreateCaretLocation clamps the line and column and creates a zero-length selection, so the
		// caret is placed without selecting the line and typing after a definition navigation replaces nothing. The
		// column addition saturates so a start character at int.MaxValue still clamps to the line end instead of
		// overflowing into a negative column. The recorded identifier is the document's FileName, or null
		// for an unsaved document, and it gives the location context only.
		NavigationLocation navigationLocation = TextAreaExtensions.CreateCaretLocation(
			textArea,
			textArea.Document.FileName,
			new TextLocation(start.Line + 1, (int)Math.Min((long)start.Character + 1, int.MaxValue))
		);

		textArea.ApplyLocation(navigationLocation);
		return true;
	}
}
