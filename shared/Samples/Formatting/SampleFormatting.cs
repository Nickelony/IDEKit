using Nickelony.IDEKit.LanguageFeatures.Formatting;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Formatting;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Formatting;
#endif

namespace Nickelony.Samples;

/// <summary>Formats the document of one editor through a formatting provider, which applies the provider's edits as one undo step.</summary>
internal sealed class SampleFormatting
{
	private readonly TextEditor _editor;
	private readonly IFormattingProvider? _provider;
	private readonly string _documentId;
	private readonly FormattingService _service = new();

	/// <param name="editor">The editor whose document is formatted.</param>
	/// <param name="provider">Answers the formatting requests, or <see langword="null"/> when no formatter is configured.</param>
	/// <param name="documentId">The identity of the edited document, which every request carries.</param>
	internal SampleFormatting(TextEditor editor, IFormattingProvider? provider, string documentId)
	{
		_editor = editor;
		_provider = provider;
		_documentId = documentId;
	}

	/// <summary>Whether a formatting provider is configured, so <see cref="FormatAsync"/> can ask it.</summary>
	internal bool CanFormat => _provider is not null;

	/// <summary>Asks the provider to format the document with the editor's indentation and applies its edits.</summary>
	/// <returns>How the formatting ended, or <see cref="FormattingOutcome.NothingToApply"/> when no provider is configured.</returns>
	internal Task<FormattingOutcome> FormatAsync()
	{
		if (_provider is not { } provider)
			return Task.FromResult(FormattingOutcome.NothingToApply);

		var options = new FormattingOptions(_editor.Options.IndentationSize, _editor.Options.ConvertTabsToSpaces);

		return _service.FormatDocumentAsync(editor: _editor, provider, documentId: _documentId, options);
	}
}
