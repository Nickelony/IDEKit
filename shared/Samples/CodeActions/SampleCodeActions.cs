using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Editing;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.AvaloniaEdit.Media;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.AvalonEdit.Media;
#endif

namespace Nickelony.Samples;

/// <summary>
/// Offers the code actions of the selection of one editor in a light-bulb margin and its menu, and applies the chosen action's edits.
/// </summary>
/// <remarks>
/// An action's edit can name other documents, so it goes to the document authority,
/// as a rename's does; see <see cref="SampleDocuments.ApplyEditAsync"/>. The controller resolves an action that a language server offered
/// without an edit before the hook sees it, and refuses an action when the editor's text differs from the text it was requested for, so the hook
/// applies the edit as it is. The language server runs the action's command after the hook returned, so an action with a command only has no edit
/// for the hook to apply.
/// </remarks>
internal sealed class SampleCodeActions : IDisposable
{
	private readonly TextEditor _editor;
	private readonly string _documentId;
	private readonly Func<WorkspaceEdit, Task> _applyEditAsync;
	private readonly CodeActionController _controller;
	private readonly CodeActionPresenter _presenter;

	/// <param name="editor">The editor whose selection the actions apply to.</param>
	/// <param name="provider">Answers the code-action requests.</param>
	/// <param name="documentId">The identity of the edited document, which every request carries.</param>
	/// <param name="applyEditAsync">Applies an action's edit to every document it names.</param>
	/// <param name="theme">The look of the menu.</param>
	/// <param name="logger">Receives the controller's contained failures, or <see langword="null"/> to log nothing.</param>
	internal SampleCodeActions(
		TextEditor editor,
		ICodeActionProvider provider,
		string documentId,
		Func<WorkspaceEdit, Task> applyEditAsync,
		SampleTheme theme,
		ILogger? logger = null
	)
	{
		_editor = editor;
		_documentId = documentId;
		_applyEditAsync = applyEditAsync;

		_controller = CodeActionControllerFactory.Create(
			textArea: editor.TextArea,
			provider,
			hooks: new CodeActionControllerHooks(BuildRequest, ExecuteActionAsync: ApplyAsync),
			logger: logger
		);

		_presenter = new CodeActionPresenter(editor.TextArea, _controller, new CodeActionPresentationOptions { MenuTheme = MenuThemeOf(theme) });
		editor.TextArea.LeftMargins.Insert(0, _presenter.Margin);
	}

	/// <summary>Asks the provider for actions at the caret and opens the menu when it answers any.</summary>
	internal async Task<bool> OpenMenuAsync()
	{
		await _controller.RequestAsync();
		return _presenter.TryOpenMenu();
	}

	/// <summary>Switches the look of the next menu.</summary>
	internal void ApplyTheme(SampleTheme theme) => _presenter.MenuTheme = MenuThemeOf(theme);

	public void Dispose()
	{
		_editor.TextArea.LeftMargins.Remove(_presenter.Margin);

		_presenter.Dispose();
		_controller.Dispose();
	}

	private static CodeActionMenuTheme MenuThemeOf(SampleTheme theme)
	{
		return new()
		{
			Background = theme.Surface.ToBrush(),
			Foreground = theme.Foreground.ToBrush(),
			BorderBrush = theme.Border.ToBrush(),
		};
	}

	private CodeActionRequest BuildRequest(CodeActionSelection selection) => new(_documentId, selection.GetText(), selection.Range);

	private Task ApplyAsync(CodeAction action, CancellationToken cancellationToken) =>
		action.Edit is { } edit ? _applyEditAsync(edit) : Task.CompletedTask;
}
