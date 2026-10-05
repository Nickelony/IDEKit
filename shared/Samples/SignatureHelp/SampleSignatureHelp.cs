using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Media;
using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SignatureHelp;
using Nickelony.IDEKit.AvaloniaEdit.Media;
using PopupContent = Avalonia.Controls.Control;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SignatureHelp;
using Nickelony.IDEKit.AvalonEdit.Media;
using PopupContent = System.Windows.FrameworkElement;
#endif

namespace Nickelony.Samples;

/// <summary>Shows the signature of the called function below the caret of one editor while the caret is inside the argument list.</summary>
/// <remarks>
/// The factory opens the help on the provider's trigger characters and follows the caret through the call, and the presenter closes the popup,
/// so this type builds the content.
/// </remarks>
internal sealed class SampleSignatureHelp : IDisposable
{
	private readonly TextEditor _editor;
	private readonly string _fenceLanguage;
	private readonly SignatureHelpPresenter _presenter;
	private readonly SignatureHelpController? _controller;

	private SampleTheme _theme;

	/// <param name="editor">The editor whose caret the popup follows.</param>
	/// <param name="provider">Answers the requests and names the characters that start them, or <see langword="null"/> to show no help.</param>
	/// <param name="documentId">The identity of the edited document, which every request carries.</param>
	/// <param name="theme">The look of the popup.</param>
	/// <param name="logger">Receives the controller's contained failures, or <see langword="null"/> to log nothing.</param>
	internal SampleSignatureHelp(TextEditor editor, ISignatureHelpProvider? provider, string documentId, SampleTheme theme, ILogger? logger = null)
	{
		_editor = editor;
		_fenceLanguage = SampleMarkdown.FenceLanguageOf(documentId);
		_theme = theme;
		_presenter = new SignatureHelpPresenter(editor.TextArea, BuildContent, new SignatureHelpPresentationOptions { Theme = PopupTheme(theme) });

		if (provider is null)
			return;

		_controller = SignatureHelpControllerFactory.Create(
			textArea: editor.TextArea,
			provider,
			presenter: _presenter,
			buildRequest: (offset, context) => new SignatureHelpRequest(documentId, documentText: _editor.Text, caretOffset: offset, context),
			logger: logger
		);
	}

	/// <summary>Whether the popup is showing.</summary>
	internal bool IsVisible => _presenter.IsOpen;

	/// <summary>Asks for signature help at the caret.</summary>
	/// <returns><see langword="true"/> when the provider answered a signature; otherwise, <see langword="false"/>.</returns>
	internal Task<bool> RequestAsync() => _controller?.RequestAsync(_editor.CaretOffset) ?? Task.FromResult(false);

	internal void ApplyTheme(SampleTheme theme)
	{
		_theme = theme;
		_presenter.Theme = PopupTheme(theme);
	}

	public void Dispose()
	{
		_controller?.Dispose();
		_presenter.Dispose();
	}

	private static SignatureHelpPopupTheme PopupTheme(SampleTheme theme)
	{
		return SignatureHelpPopupTheme.Default with { Background = theme.Surface.ToBrush(), BorderBrush = theme.Border.ToBrush() };
	}

	private PopupContent BuildContent(SignatureHelpInformation information)
	{
		SignatureInformation signature = information.ActiveSignature;
		List<string> parts = [SampleMarkdown.Fence(signature.Label, _fenceLanguage)];

		// The parameter under the caret leads, because it is what the user types next.
		if (information.ActiveParameterIndex is int index && index < signature.Parameters.Count)
		{
			ParameterInformation parameter = signature.Parameters[index];
			parts.Add($"**{parameter.Label}**{(parameter.Documentation is { } documentation ? $": {documentation.Value}" : string.Empty)}");
		}

		if (signature.Documentation is { } summary)
			parts.Add(summary.Value);

		return SampleMarkdown.Render(string.Join("\n\n", parts), _theme);
	}
}
