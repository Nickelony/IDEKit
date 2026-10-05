using Nickelony.IDEKit.LanguageFeatures.Navigation;
#if AVALONIAEDIT
using AvaloniaEdit;
#else
using ICSharpCode.AvalonEdit;
#endif

namespace Nickelony.Samples;

/// <summary>Asks a reference provider for every reference of the symbol at the caret of one editor.</summary>
internal sealed class SampleReferenceSearch
{
	private readonly TextEditor _editor;
	private readonly IReferenceProvider? _provider;
	private readonly string _documentId;

	/// <param name="editor">The editor whose caret names the symbol.</param>
	/// <param name="provider">Answers the reference requests, or <see langword="null"/> when the document has none.</param>
	/// <param name="documentId">The identity of the edited document, which every request carries.</param>
	internal SampleReferenceSearch(TextEditor editor, IReferenceProvider? provider, string documentId)
	{
		_editor = editor;
		_provider = provider;
		_documentId = documentId;
	}

	/// <summary>Asks for the references of the symbol at the caret, its declaration included.</summary>
	/// <returns>The references, which can lie in other documents, or an empty list when no provider is configured.</returns>
	internal Task<IReadOnlyList<ReferenceLocation>> FindAsync()
	{
		if (_provider is not { } provider)
			return Task.FromResult<IReadOnlyList<ReferenceLocation>>([]);

		return provider.GetReferencesAsync(new ReferenceRequest(_documentId, _editor.Text, _editor.CaretOffset));
	}
}
