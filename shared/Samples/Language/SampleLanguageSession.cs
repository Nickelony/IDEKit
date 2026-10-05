using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
#if AVALONIAEDIT
using AvaloniaEdit;
#else
using ICSharpCode.AvalonEdit;
#endif

namespace Nickelony.Samples;

/// <summary>Connects one editor to its language features through one collaborator per feature, which the folder of that feature holds.</summary>
internal sealed class SampleLanguageSession : IDisposable
{
	private readonly TextEditor _editor;
	private readonly SampleLanguageFeatures _features;
	private readonly string _documentId;
	private readonly ILogger? _logger;

	private SampleTheme _theme;

	/// <param name="editor">The editor control the session serves.</param>
	/// <param name="features">The language-feature contracts that answer the editor's requests.</param>
	/// <param name="documentId">The identity of the edited document: its local file path.</param>
	/// <param name="theme">The look of the popups, the windows and the menus.</param>
	/// <param name="hoverDelay">How long the pointer rests over a name before its hover shows.</param>
	/// <param name="applyEditAsync">Applies a code action's edit to every document it names.</param>
	/// <param name="navigateToDocumentAsync">
	/// Shows a definition in another document, or <see langword="null"/> to report such a definition as not navigated.
	/// </param>
	/// <param name="logger">Receives the controllers' contained failures, or <see langword="null"/> to log nothing.</param>
	internal SampleLanguageSession(
		TextEditor editor,
		SampleLanguageFeatures features,
		string documentId,
		SampleTheme theme,
		TimeSpan hoverDelay,
		Func<WorkspaceEdit, Task> applyEditAsync,
		Func<DefinitionLocation, CancellationToken, Task<bool>>? navigateToDocumentAsync = null,
		ILogger? logger = null
	)
	{
		_editor = editor;
		_features = features;
		_documentId = documentId;
		_theme = theme;
		_logger = logger;

		Diagnostics = SampleDiagnostics.Create(editor, features, documentId);

		if (features.Completion is { } completion)
		{
			Completion = new SampleCompletion(
				editor,
				provider: completion,
				documentId,
				codeSyntax: SampleEditingFeatures.CodeSyntaxOf(SampleFiles.KindOf(documentId)),
				theme,
				logger
			);
		}

		// A diagnostic shows under the pointer without a hover provider, so a file that is only checked gets the popup too.
		if (features.Hover is not null || Diagnostics is not null)
		{
			Hover = new SampleHover(
				editor,
				provider: features.Hover,
				documentId,
				findDiagnostic: offset => Diagnostics?.FindAt(offset),
				theme,
				restDelay: hoverDelay,
				logger
			);
		}

		if (features.CodeActions is { } codeActions)
			CodeActions = new SampleCodeActions(editor, provider: codeActions, documentId, applyEditAsync, theme, logger);

		SignatureHelp = new SampleSignatureHelp(editor, provider: features.SignatureHelp, documentId, theme, logger);

		SemanticTokens = features.SemanticTokens is null
			? null
			: new SampleSemanticTokens(editor, source: features.SemanticTokens, documentId, theme);

		Definitions = new SampleDefinitionNavigation(editor, provider: features.Definitions, documentId, navigateToDocumentAsync, logger);
		References = new SampleReferenceSearch(editor, features.References, documentId);
		Outline = new SampleOutline(editor, features.DocumentSymbols, documentId);
		Formatting = new SampleFormatting(editor, features.Formatting, documentId);
		Rename = new SampleRename(editor, features.Rename, documentId);
	}

	/// <summary>The completion window, or <see langword="null"/> when the features complete nothing.</summary>
	internal SampleCompletion? Completion { get; }

	/// <summary>The hover popup, or <see langword="null"/> when the features answer no hover and produce no diagnostic.</summary>
	internal SampleHover? Hover { get; private set; }

	internal SampleSignatureHelp SignatureHelp { get; }

	/// <summary>The light bulb and its menu, or <see langword="null"/> when the features offer no code action.</summary>
	internal SampleCodeActions? CodeActions { get; }

	/// <summary>The underlined diagnostics, or <see langword="null"/> when the features produce none.</summary>
	internal DiagnosticsController? Diagnostics { get; }

	/// <summary>The coloring of semantic tokens, or <see langword="null"/> when the features publish none.</summary>
	internal SampleSemanticTokens? SemanticTokens { get; }

	internal SampleDefinitionNavigation Definitions { get; }

	internal SampleReferenceSearch References { get; }

	internal SampleOutline Outline { get; }

	internal SampleFormatting Formatting { get; }

	internal SampleRename Rename { get; }

	/// <summary>
	/// Builds the hover again with another rest delay, which the hover controller takes when it is built; the other features keep running.
	/// </summary>
	internal void ApplyHoverDelay(TimeSpan hoverDelay)
	{
		if (Hover is null)
			return;

		Hover.Dispose();

		Hover = new SampleHover(
			editor: _editor,
			provider: _features.Hover,
			documentId: _documentId,
			findDiagnostic: offset => Diagnostics?.FindAt(offset),
			theme: _theme,
			restDelay: hoverDelay,
			logger: _logger
		);
	}

	/// <summary>Switches the look of every popup, window, menu and token color.</summary>
	internal void ApplyTheme(SampleTheme theme)
	{
		_theme = theme;

		Completion?.ApplyTheme(theme);
		Hover?.ApplyTheme(theme);
		SignatureHelp.ApplyTheme(theme);
		CodeActions?.ApplyTheme(theme);
		SemanticTokens?.ApplyTheme(theme);
	}

	public void Dispose()
	{
		Outline.Dispose();
		Definitions.Dispose();
		SemanticTokens?.Dispose();
		CodeActions?.Dispose();
		SignatureHelp.Dispose();
		Hover?.Dispose();
		Completion?.Dispose();
		Diagnostics?.Dispose();
	}
}
