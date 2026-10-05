using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Editing;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SignatureHelp;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SignatureHelp;

#endif

/// <summary>Creates a <see cref="SignatureHelpController"/> that serves a text area.</summary>
/// <remarks>
/// The controller reads the caret of the text area and runs on the text area's dispatcher, so it must be created on the text area's thread.
/// </remarks>
public static class SignatureHelpControllerFactory
{
	private const string OverloadJustification =
		"Both overloads keep the shared trailing options and logger, and their third parameters differ in type, so no call binds to the other.";

	/// <summary>Creates a controller that reads the caret of <paramref name="textArea"/> and runs on its dispatcher.</summary>
	/// <param name="textArea">The text area the controller serves.</param>
	/// <param name="provider">The provider that supplies the signature help for a request.</param>
	/// <param name="hooks">The required signature help callbacks.</param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <returns>The controller, which the caller disposes.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="provider"/> is <see langword="null"/>, or a required hook is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns <paramref name="textArea"/>.</exception>
	[SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = OverloadJustification)]
	public static SignatureHelpController Create(
		TextArea textArea,
		ISignatureHelpProvider provider,
		SignatureHelpControllerHooks hooks,
		SignatureHelpControllerOptions? options = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(provider);

		return new SignatureHelpController(
			getCaretOffset: () => textArea.Document is null ? null : textArea.Caret.Offset,
			provider,
			hooks,
			dispatcher: UiDispatcher.From(textArea.Dispatcher),
			options,
			logger
		);
	}

	/// <summary>Creates a controller that shows its answers through <paramref name="presenter"/> and follows the caret through the call.</summary>
	/// <param name="textArea">The text area the controller serves, whose popups <paramref name="presenter"/> shows.</param>
	/// <param name="provider">The provider that supplies the signature help for a request.</param>
	/// <param name="presenter">
	/// The presenter that shows the answers; the caller keeps it and disposes it after the controller. A later controller created
	/// over the same presenter replaces this one in the presenter's wiring.
	/// </param>
	/// <param name="buildRequest">
	/// Builds the provider request for an offset and a trigger context, or returns <see langword="null"/>
	/// to veto it, as <see cref="SignatureHelpControllerHooks.BuildRequest"/> does.
	/// </param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <returns>The controller, which the caller disposes.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/>, <paramref name="provider"/>, <paramref name="presenter"/> or <paramref name="buildRequest"/>
	/// is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns <paramref name="textArea"/>.</exception>
	/// <remarks>
	/// <para>
	/// While the popup is open, a caret move schedules a <see cref="SignatureHelpTriggerKind.ContentChange"/> retrigger, so an answer
	/// of <see langword="null"/> dismisses the popup once the caret left the call; a move before the offset that opened the popup dismisses
	/// it at once. That offset is a text anchor, so an edit before the call, from this view or another one, moves it with the text.
	/// </para>
	/// <para>
	/// A popup that closes on its own and a <see cref="SignatureHelpPresenter.Hide"/> of the host
	/// dismiss the controller, and so does a show the presenter refuses, on the next dispatcher turn unless a newer show opened the popup,
	/// so <see cref="SignatureHelpController.CurrentPresentation"/> agrees with the popup.
	/// </para>
	/// <para>
	/// When <paramref name="provider"/> implements <see cref="ISignatureHelpTriggerCharacters"/>,
	/// a typed trigger character schedules a <see cref="SignatureHelpTriggerKind.TriggerCharacter"/> request, and so does a typed
	/// retrigger character while the popup is open. A character is evaluated after its input applied, whichever handler applied it, so an auto-closed
	/// parenthesis opens the popup. A host with its own typing rules passes a provider without the interface and calls
	/// <see cref="SignatureHelpController.ScheduleRequest"/> itself.
	/// </para>
	/// </remarks>
	[SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = OverloadJustification)]
	public static SignatureHelpController Create(
		TextArea textArea,
		ISignatureHelpProvider provider,
		SignatureHelpPresenter presenter,
		Func<int, SignatureHelpContext, SignatureHelpRequest?> buildRequest,
		SignatureHelpControllerOptions? options = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(provider);
		ArgumentNullException.ThrowIfNull(presenter);
		ArgumentNullException.ThrowIfNull(buildRequest);

		var dispatcher = UiDispatcher.From(textArea.Dispatcher);
		SignatureHelpController? controller = null;

		// Where the call starts: the offset of the request that opened the popup, kept while it stays open.
		TextAnchor? openAnchor = null;

		void show(SignatureHelpInformation information, int offset)
		{
			if (!presenter.IsOpen && textArea.Document is { } document)
				openAnchor = document.CreateAnchor(Math.Clamp(offset, 0, document.TextLength));

			presenter.Show(information, offset);

			// The controller counts the answer as shown once this hook returns, so a refused show dismisses it after the hook, unless a newer
			// show opened the popup in the meantime.
			if (!presenter.IsOpen)
			{
				dispatcher.Post(() =>
				{
					if (!presenter.IsOpen)
						controller?.Dismiss();
				});
			}
		}

		controller = Create(
			textArea,
			provider,
			hooks: new SignatureHelpControllerHooks(buildRequest, show, presenter.HideForController),
			options,
			logger
		);

		presenter.Attach(
			controller,

			caretMoved: () =>
			{
				if (openAnchor is { } start && textArea.Caret.Offset < start.Offset)
					controller.Dismiss();
				else
					controller.ScheduleRequest();
			},

			typedText: provider is ISignatureHelpTriggerCharacters characters
				? new TypedTextSubscription(textArea, (typed, _) => HandleTypedCharacter(controller, presenter, characters, typed))
				: null
		);

		return controller;
	}

	// A retrigger character refreshes only an open popup, and a trigger character opens or refreshes it; both carry the character, and a request
	// while the popup is open is marked as a retrigger by the controller.
	private static void HandleTypedCharacter(
		SignatureHelpController controller,
		SignatureHelpPresenter presenter,
		ISignatureHelpTriggerCharacters characters,
		char typed
	)
	{
		string character = typed.ToString();

		if (characters.TriggerCharacters.Contains(character, StringComparer.Ordinal))
			controller.ScheduleRequest(SignatureHelpTriggerKind.TriggerCharacter, character);
		else if (presenter.IsOpen && characters.RetriggerCharacters.Contains(character, StringComparer.Ordinal))
			controller.ScheduleRequest(SignatureHelpTriggerKind.TriggerCharacter, character);
	}
}
