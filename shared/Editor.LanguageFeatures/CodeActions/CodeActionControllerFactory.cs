using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
#if AVALONIAEDIT
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

#endif

/// <summary>Creates a <see cref="CodeActionController"/> that serves a text area.</summary>
/// <remarks>
/// The controller reads the caret, the selection, and the document of the text area and runs on the text area's dispatcher, so it must be created
/// on the text area's thread. A host that shows the actions in the library's indicator margin and menu creates a <see cref="CodeActionPresenter"/>
/// over the same text area and controller.
/// </remarks>
public static class CodeActionControllerFactory
{
	/// <summary>Creates a controller that watches <paramref name="textArea"/> and runs on its dispatcher.</summary>
	/// <param name="textArea">The text area the controller serves.</param>
	/// <param name="provider">The provider that supplies the code actions for a request.</param>
	/// <param name="hooks">The host callbacks.</param>
	/// <param name="options">The controller options, or <see langword="null"/> to use the defaults.</param>
	/// <param name="logger">An optional logger for request failures.</param>
	/// <returns>The controller, which the caller disposes.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textArea"/> or <paramref name="provider"/> is <see langword="null"/>, or a required hook is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The caller is not on the thread that owns <paramref name="textArea"/>.</exception>
	public static CodeActionController Create(
		TextArea textArea,
		ICodeActionProvider provider,
		CodeActionControllerHooks hooks,
		CodeActionControllerOptions? options = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(textArea);
		ArgumentNullException.ThrowIfNull(provider);

		var dispatcher = UiDispatcher.From(textArea.Dispatcher);

		// The source subscribes to the text area, so the thread check comes first and a call from another thread touches nothing.
		dispatcher.VerifyAccess();

		var selectionSource = new CodeActionSelectionSource(textArea);

		try
		{
			return new CodeActionController(selectionSource, provider, hooks, dispatcher, options, logger);
		}
		catch
		{
			// The controller takes over the source only when its constructor returns, so a constructor that throws leaves the release to this catch.
			selectionSource.Dispose();
			throw;
		}
	}
}
