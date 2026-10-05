using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Hover;
#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Navigation;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using System.Windows;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Navigation;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;

#endif

/// <summary>Creates a <see cref="HoverController"/> that serves a text area.</summary>
/// <remarks>
/// The controller reads the pointer over the text area and runs on the text area's dispatcher, so it must be created on the text area's thread.
/// </remarks>
public static class HoverControllerFactory
{
	/// <summary>Creates a controller that tracks the pointer over <paramref name="textArea"/> and runs on its dispatcher.</summary>
	/// <param name="textArea">The text area the controller serves; hover positions are resolved against it.</param>
	/// <param name="provider">
	/// The provider that supplies the hover content for a request, or <see langword="null"/> to show the diagnostic under the pointer without asking
	/// for any hover content.
	/// </param>
	/// <param name="hooks">The hover callbacks; the hook group documents which members are required.</param>
	/// <param name="options">When the controller asks for a hover, or <see langword="null"/> for <see cref="HoverPointerOptions.Default"/>.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <returns>The controller, which the caller disposes.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="hooks"/> is <see langword="null"/>, or a required hook is <see langword="null"/>;
	/// <see cref="HoverControllerHooks.BuildRequest"/> is required when <paramref name="provider"/> is supplied.
	/// </exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns <paramref name="textArea"/>.</exception>
	/// <remarks>
	/// <para>
	/// The pointer maps to an offset only over the text of a line (<see cref="TextAreaExtensions.TryGetTextOffsetFromPoint"/>), so the blank space
	/// past the end of a row hovers nothing.
	/// </para>
	/// <para>
	/// Without a <see cref="HoverControllerHooks.ContextVersionProvider"/>,
	/// the controller's context version counts the changes of the text area's text and document, so typed text discards the hover in flight.
	/// Pass <see cref="HoverPresenter.Show"/> as <see cref="HoverControllerHooks.ShowHover"/> to show the hovers in the binding's popup.
	/// </para>
	/// </remarks>
	public static HoverController Create(
		TextArea textArea,
		IHoverProvider? provider,
		HoverControllerHooks hooks,
		HoverPointerOptions? options = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(hooks);

		return Create(
			textArea,
			provider,
			hooks,
			options: options ?? HoverPointerOptions.Default,
			logger,
			offsetFromPoint: point => textArea.TryGetTextOffsetFromPoint(point, out int offset) ? offset : null
		);
	}

	/// <summary>Creates the controller over a mapping from a pointer position to a document offset, which a test replaces.</summary>
	internal static HoverController Create(
		TextArea textArea,
		IHoverProvider? provider,
		HoverControllerHooks hooks,
		HoverPointerOptions options,
		ILogger? logger,
		Func<Point, int?> offsetFromPoint
	)
	{
		var dispatcher = UiDispatcher.From(textArea.Dispatcher);

		// The source subscribes to the text area, so the thread check comes first and a call from another thread touches nothing.
		dispatcher.VerifyAccess();

		// The source asks the controller it is handed to, so the request reaches a controller that exists by the time the pointer rests.
		HoverController? controller = null;

		var pointerSource = new HoverPointerSource(
			textArea,
			offsetFromPoint,
			restDelay: options.RestDelay,
			requestHover: () => _ = controller?.RequestAsync()
		);

		// Without a host context version, a change of the text discards the hover in flight, because the text it describes moved.
		if (hooks.ContextVersionProvider is null)
		{
			hooks = new HoverControllerHooks
			{
				BuildDecision = hooks.BuildDecision,
				BuildRequest = hooks.BuildRequest,
				ResolveRequestOffset = hooks.ResolveRequestOffset,
				ShowHover = hooks.ShowHover,
				ContextVersionProvider = () => pointerSource.DocumentVersion,
			};
		}

		try
		{
			controller = new HoverController(pointerSource, provider, hooks, dispatcher, logger);
			return controller;
		}
		catch
		{
			// The controller owns the source only once its constructor returns, so a rejected hook set would leave the
			// subscriptions on the text area.
			pointerSource.Dispose();
			throw;
		}
	}
}
