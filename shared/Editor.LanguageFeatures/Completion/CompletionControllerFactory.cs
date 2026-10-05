using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Text;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Editing;
using Nickelony.IDEKit.AvalonEdit.Text;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>
/// Creates a <see cref="CompletionController"/> that serves a text area and shows its decisions through a <see cref="CompletionPresenter"/>.
/// </summary>
/// <remarks>
/// The controller reads the document and the caret of the text area and runs on the text area's dispatcher, so it must be created on the text
/// area's thread.
/// </remarks>
public static class CompletionControllerFactory
{
	/// <summary>
	/// Creates a controller that reads <paramref name="textArea"/>, shows its decisions through <paramref name="presenter"/>,
	/// and runs on the dispatcher of the text area.
	/// </summary>
	/// <param name="textArea">The text area the controller serves, which <paramref name="presenter"/> was created for.</param>
	/// <param name="documentId">
	/// The identifier of the edited document, which every request carries. One controller serves one document:
	/// when the text area replaces its document, the controller stops requesting, so a host creates a new one for a document replacement or a move,
	/// as the
	/// <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorLanguageFeaturesGuide.md#one-controller-per-document">document rule</see>
	/// says.
	/// </param>
	/// <param name="provider">
	/// The provider that answers the requests; when it implements <see cref="ICompletionTriggerCharacters"/>, its characters open completion
	/// when typed.
	/// </param>
	/// <param name="presenter">
	/// The presenter that shows the decisions, created for <paramref name="textArea"/>. The caller keeps it and disposes it after the controller.
	/// A later controller created over the same presenter replaces this one in the presenter's wiring.
	/// </param>
	/// <param name="hooks">
	/// The optional host hooks, or <see langword="null"/> when the host needs none: the scope at the caret and the host context version.
	/// The hooks that restyle the window and build the tooltip go to the <see cref="CompletionPresenter"/>.
	/// </param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="kernel">
	/// The kernel that turns the provider's items into a decision, or <see langword="null"/> for one with the default word locator and filter.
	/// </param>
	/// <param name="logger">An optional logger for request and trigger failures.</param>
	/// <returns>The controller, which the caller disposes before the presenter.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="documentId"/>, <paramref name="provider"/> or <paramref name="presenter"/>
	/// is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns <paramref name="textArea"/>.</exception>
	/// <remarks>
	/// <para>
	/// The factory forwards each character typed into the text area, evaluated after its input applied
	/// whichever handler applied it, and the replacement of the text area's document to the controller, so a commit of the open window runs first
	/// and an auto-closing insert or overtype skip still triggers.
	/// </para>
	/// <para>
	/// Ownership follows the signature help factory. The controller owns nothing: it never disposes the presenter. The presenter owns
	/// the engine subscriptions that the factory creates, from the moment this method returns, and disposes them when a later controller replaces
	/// them or the presenter is disposed. The caller therefore disposes the controller, then the presenter.
	/// </para>
	/// </remarks>
	public static CompletionController Create(
		TextArea textArea,
		string documentId,
		ICompletionProvider provider,
		CompletionPresenter presenter,
		CompletionControllerHooks? hooks = null,
		CompletionControllerOptions? options = null,
		CompletionSessionKernel? kernel = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		ArgumentNullException.ThrowIfNull(provider);
		ArgumentNullException.ThrowIfNull(presenter);

		var dispatcher = UiDispatcher.From(textArea.Dispatcher);

		// The subscriptions attach to the text area, so the thread check comes first and a call from another thread touches nothing.
		dispatcher.VerifyAccess();

		var controller = new CompletionController(
			captureContext: () => Capture(textArea),
			documentId,
			provider,
			presenter,
			dispatcher,
			hooks,
			options,
			kernel,
			logger
		);

		var typedText = new TypedTextSubscription(textArea, controller.HandleCharacterTyped);

		void handleDocumentChanged(object? sender, EventArgs e) => controller.HandleDocumentReplaced();

		textArea.DocumentChanged += handleDocumentChanged;

		presenter.Attach(
			controller,

			new ActionSubscription(() =>
			{
				typedText.Dispose();
				textArea.DocumentChanged -= handleDocumentChanged;
			})
		);

		return controller;
	}

	// The snapshot and the caret are taken together, on the text area's thread, so the provider reads the text the caret refers to.
	private static CompletionEditorContext? Capture(TextArea textArea)
	{
		if (textArea.Document is not TextDocument document)
			return null;

		return new CompletionEditorContext(new TextDocumentSnapshot(document), document.ClampOffset(textArea.Caret.Offset));
	}
}
