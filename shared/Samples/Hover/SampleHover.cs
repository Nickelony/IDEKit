using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Media;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.AvaloniaEdit.Media;
using PopupContent = Avalonia.Controls.Control;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;
using Nickelony.IDEKit.AvalonEdit.Media;
using PopupContent = System.Windows.FrameworkElement;
#endif

namespace Nickelony.Samples;

/// <summary>Shows the hover of the word under the pointer of one editor, and the diagnostic there, as Markdown in a popup.</summary>
/// <remarks>
/// The controller asks for a hover once the pointer rests over the text, and the presenter places, hides and closes the popup, so this type
/// only builds the content.
/// </remarks>
internal sealed class SampleHover : IDisposable
{
	private readonly Func<int, Diagnostic?> _findDiagnostic;
	private readonly HoverPresenter _presenter;
	private readonly HoverController _controller;

	private SampleTheme _theme;

	/// <param name="editor">The editor whose pointer the hover follows.</param>
	/// <param name="provider">
	/// Answers the hover requests, or <see langword="null"/> to show the diagnostic under the pointer and nothing else.
	/// </param>
	/// <param name="documentId">The identity of the edited document, which every request carries.</param>
	/// <param name="findDiagnostic">Returns the diagnostic at an offset, which the popup shows where the provider answers nothing.</param>
	/// <param name="theme">The look of the popup and of the Markdown it renders.</param>
	/// <param name="restDelay">How long the pointer rests over a name before the hover shows.</param>
	/// <param name="logger">Receives the controller's contained failures, or <see langword="null"/> to log nothing.</param>
	internal SampleHover(
		TextEditor editor,
		IHoverProvider? provider,
		string documentId,
		Func<int, Diagnostic?> findDiagnostic,
		SampleTheme theme,
		TimeSpan restDelay,
		ILogger? logger = null
	)
	{
		_findDiagnostic = findDiagnostic;
		_theme = theme;
		_presenter = new HoverPresenter(editor.TextArea, BuildContent, new HoverPresentationOptions { Theme = PopupTheme(theme) });

		_controller = HoverControllerFactory.Create(
			textArea: editor.TextArea,
			provider,

			hooks: new HoverControllerHooks
			{
				BuildDecision = offset => BuildDecision(offset, provider is not null),
				BuildRequest = provider is null ? null : offset => new HoverRequest(documentId, editor.Text, offset),
				ShowHover = _presenter.Show,
			},

			options: new HoverPointerOptions { RestDelay = restDelay },
			logger
		);
	}

	internal void ApplyTheme(SampleTheme theme)
	{
		_theme = theme;
		_presenter.Theme = PopupTheme(theme);
	}

	public void Dispose()
	{
		_controller.Dispose();
		_presenter.Dispose();
	}

	private static HoverPopupTheme PopupTheme(SampleTheme theme)
	{
		return HoverPopupTheme.Default with { Background = theme.Surface.ToBrush(), BorderBrush = theme.Border.ToBrush() };
	}

	// The diagnostic travels with the decision, so the controller can show it when the provider has no hover for the offset.
	private HoverDecision BuildDecision(int offset, bool asksProvider)
	{
		return new()
		{
			ShouldRequestHover = asksProvider,
			RequestOffset = offset,
			CanShowHoverContent = true,
			CanShowDiagnosticFallback = true,
			Diagnostic = _findDiagnostic(offset),
		};
	}

	// The hover and the diagnostic under the pointer render as one document, the diagnostic last, as editors commonly show them.
	private PopupContent BuildContent(HoverInformation? information, Diagnostic? diagnostic)
	{
		List<string> parts = [];

		if (information is not null)
			parts.Add(information.Content.Value);

		if (diagnostic is not null)
			parts.Add($"**{diagnostic.Severity}**: {diagnostic.Message}");

		return SampleMarkdown.Render(string.Join("\n\n", parts), _theme);
	}
}
