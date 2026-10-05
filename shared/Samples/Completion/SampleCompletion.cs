using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Scanning;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.AvaloniaEdit.Media;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.AvalonEdit.Media;
#endif

namespace Nickelony.Samples;

/// <summary>
/// Drives the completion window of one editor: a letter typed in code schedules a request, and a trigger character of the provider asks at once.
/// </summary>
internal sealed class SampleCompletion : IDisposable
{
	private readonly CodeScopeTracker? _scopes;

#if !AVALONIAEDIT
	// The look of the Markdown in the completion tooltip, which a theme switch replaces.
	private SampleTheme _theme;
#endif

	/// <param name="editor">The editor whose completion window the presenter shows.</param>
	/// <param name="provider">Answers the completion requests, and names its trigger characters.</param>
	/// <param name="documentId">The identity of the edited document, which every request carries.</param>
	/// <param name="codeSyntax">
	/// Tells code from comments and strings, where a typed letter opens no window, or <see langword="null"/> to open it after any letter.
	/// </param>
	/// <param name="theme">The look of the completion window.</param>
	/// <param name="logger">Receives the controller's contained failures, or <see langword="null"/> to log nothing.</param>
	internal SampleCompletion(
		TextEditor editor,
		ICompletionProvider provider,
		string documentId,
		CommentSyntax? codeSyntax,
		SampleTheme theme,
		ILogger? logger = null
	)
	{
#if !AVALONIAEDIT
		_theme = theme;
#endif
		_scopes = codeSyntax is { } syntax ? new CodeScopeTracker(editor.Document, syntax) : null;

#if AVALONIAEDIT
		CompletionPresenterHooks? hooks = null;
#else
		// The documentation of a library function and of a server's item is Markdown, so the tooltip renders it as the hover does.
		CompletionPresenterHooks hooks = new() { BuildMarkdownContent = markdown => SampleMarkdown.Render(markdown, _theme) };
#endif

		Presenter = new CompletionPresenter(
			textArea: editor.TextArea,

			presentation: new CompletionPresentationOptions
			{
				WindowTheme = WindowThemeOf(theme),
#if !AVALONIAEDIT
				TooltipTheme = TooltipThemeOf(theme),
#endif
			},

			hooks,
			logger
		);

		Controller = CompletionControllerFactory.Create(
			textArea: editor.TextArea,
			documentId,
			provider,
			Presenter,
			hooks: new CompletionControllerHooks { ScopeAt = _scopes is null ? null : _scopes.ScopeAt },
			logger: logger
		);
	}

	/// <summary>The presenter that owns the completion window.</summary>
	internal CompletionPresenter Presenter { get; }

	/// <summary>The controller that owns the requests and the presentation state.</summary>
	internal CompletionController Controller { get; }

	/// <summary>Asks the provider for items at the caret and shows the window when it answers any.</summary>
	internal Task<bool> RequestAsync() => Controller.RequestAsync();

	/// <summary>Switches the look of the next completion window.</summary>
	internal void ApplyTheme(SampleTheme theme)
	{
		Presenter.WindowTheme = WindowThemeOf(theme);
#if !AVALONIAEDIT
		_theme = theme;
		Presenter.TooltipTheme = TooltipThemeOf(theme);
#endif
	}

	public void Dispose()
	{
		// The presenter goes after the controller, which never disposes it.
		Controller.Dispose();
		Presenter.Dispose();
		_scopes?.Dispose();
	}

	private static CompletionWindowTheme WindowThemeOf(SampleTheme theme)
	{
		return new()
		{
			Background = theme.Surface.ToBrush(),
			Foreground = theme.Foreground.ToBrush(),
			BorderBrush = theme.Border.ToBrush(),
		};
	}

#if !AVALONIAEDIT
	private static CompletionTooltipTheme TooltipThemeOf(SampleTheme theme) =>
		new() { Background = theme.Surface.ToBrush(), BorderBrush = theme.Border.ToBrush() };
#endif
}
