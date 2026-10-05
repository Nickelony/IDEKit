using Nickelony.IDEKit.LanguageFeatures.Controllers.SemanticTokens;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

namespace Nickelony.Samples;

/// <summary>Colors the tokens a semantic-tokens source publishes for one editor's document over the TextMate highlighting.</summary>
internal sealed class SampleSemanticTokens : IDisposable
{
	private readonly TextEditor _editor;
	private readonly IDisposable _sourceSubscription;
	private readonly FunctionSemanticTokensSource? _tokenizer;
	private readonly string _documentId;
	private readonly UiDispatcher _dispatcher = UiDispatcher.FromCurrent();
	private readonly FunctionTokenStyleResolver _styles;
	private readonly SemanticTokensColorizer _colorizer;

	/// <param name="editor">The editor whose text the tokens color.</param>
	/// <param name="source">Publishes the tokens.</param>
	/// <param name="documentId">The identity of the edited document, which every publication names.</param>
	/// <param name="theme">The colors of the token types.</param>
	internal SampleSemanticTokens(TextEditor editor, ISemanticTokensSource source, string documentId, SampleTheme theme)
	{
		_editor = editor;
		_documentId = documentId;

		_styles = new FunctionTokenStyleResolver(theme);
		_colorizer = new SemanticTokensColorizer(editor.TextArea.TextView, _styles);
		editor.TextArea.TextView.LineTransformers.Add(_colorizer);

		// A second view of a document colors the tokens the source published before it opened.
		_sourceSubscription = source.Subscribe(
			documentId,
			dispatcher: _dispatcher,
			onChanged: tokens => _colorizer.ApplyTokens(tokens),
			documentIdComparer: SampleDocuments.PathComparer
		);

		// A language server publishes for the text it was told about; the in-process source tokenizes the text it is given.
		_tokenizer = source as FunctionSemanticTokensSource;

		if (_tokenizer is not null)
		{
			editor.TextChanged += HandleTextChanged;
			_tokenizer.Update(documentId, editor.Text);
		}
	}

	internal void ApplyTheme(SampleTheme theme)
	{
		_styles.ApplyTheme(theme);
		_colorizer.Rebuild();
	}

	public void Dispose()
	{
		_editor.TextChanged -= HandleTextChanged;
		_sourceSubscription.Dispose();
		_editor.TextArea.TextView.LineTransformers.Remove(_colorizer);
	}

	private void HandleTextChanged(object? sender, EventArgs e) => _tokenizer?.Update(_documentId, _editor.Text);
}
