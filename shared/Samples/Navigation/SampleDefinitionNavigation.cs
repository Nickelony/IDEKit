using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;
#endif

namespace Nickelony.Samples;

/// <summary>Moves the caret of one editor to the definition of the symbol at the caret on a command, or under a Ctrl+click.</summary>
/// <remarks>
/// The binding sends the name and the offset, so the in-process provider answers by name and a language server by position.
/// A location in this document moves the caret here, and a location in another document goes to the host's callback.
/// </remarks>
internal sealed class SampleDefinitionNavigation : IDisposable
{
	private readonly TextEditor _editor;
	private readonly IDefinitionProvider? _provider;
	private readonly string _documentId;
	private readonly Func<DefinitionLocation, CancellationToken, Task<bool>>? _navigateToDocumentAsync;
	private readonly IDisposable? _pointerNavigation;

	/// <param name="editor">The editor whose caret moves.</param>
	/// <param name="provider">Answers the definition requests, or <see langword="null"/> when the document has none.</param>
	/// <param name="documentId">The identity of the edited document, which every request carries.</param>
	/// <param name="navigateToDocumentAsync">
	/// Shows a location in another document and reports whether it did, or <see langword="null"/> to report such a location as not navigated.
	/// </param>
	/// <param name="logger">Receives a failed Ctrl+click navigation, or <see langword="null"/> to log nothing.</param>
	internal SampleDefinitionNavigation(
		TextEditor editor,
		IDefinitionProvider? provider,
		string documentId,
		Func<DefinitionLocation, CancellationToken, Task<bool>>? navigateToDocumentAsync,
		ILogger? logger
	)
	{
		_editor = editor;
		_provider = provider;
		_documentId = documentId;
		_navigateToDocumentAsync = navigateToDocumentAsync;

		_pointerNavigation = provider is null
			? null
			: editor.TextArea.AttachPointerNavigation(
				documentId,
				provider,
				tryNavigateCrossFileDefinitionAsync: navigateToDocumentAsync,
				logger: logger
			);
	}

	/// <summary>Moves the caret to the definition of the symbol at the caret.</summary>
	/// <returns><see langword="true"/> when the provider answered a location and the caret moved to it; otherwise, <see langword="false"/>.</returns>
	internal Task<bool> GoToDefinitionAsync()
	{
		return _provider is { } provider
			? _editor.TextArea.GoToDefinitionAsync(
				documentId: _documentId,
				provider,
				offset: _editor.CaretOffset,
				tryNavigateCrossFileDefinitionAsync: _navigateToDocumentAsync
			)
			: Task.FromResult(false);
	}

	public void Dispose() => _pointerNavigation?.Dispose();
}
