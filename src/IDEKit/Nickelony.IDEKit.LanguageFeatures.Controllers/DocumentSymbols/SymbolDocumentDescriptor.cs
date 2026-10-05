using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;

/// <summary>
/// Describes the document whose symbols the quick pick lists: where its outline comes from, what it says, and the editor that shows it.
/// </summary>
/// <remarks>
/// The quick pick keeps an outline for the same provider, document identifier and text, whichever editor shows the document, so a host may create
/// a new descriptor, and a new target, each time it is asked.
/// </remarks>
public sealed class SymbolDocumentDescriptor
{
	/// <param name="provider">Answers the outline of the document.</param>
	/// <param name="documentId">The identifier of the document, which the outline request carries.</param>
	/// <param name="documentText">The current text of the document, which the ranges of the outline index into.</param>
	/// <param name="target">The editor that shows the document, in which the quick pick selects the symbols it previews.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="provider"/>, <paramref name="documentId"/>, <paramref name="documentText"/> or <paramref name="target"/>
	/// is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	public SymbolDocumentDescriptor(IDocumentSymbolProvider provider, string documentId, string documentText, IEditorNavigationTarget target)
	{
		ArgumentNullException.ThrowIfNull(provider);
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(target);

		Provider = provider;
		DocumentId = documentId;
		DocumentText = documentText;
		Target = target;
	}

	/// <summary>Answers the outline of the document.</summary>
	public IDocumentSymbolProvider Provider { get; }

	/// <summary>The identifier of the document, which the outline request carries.</summary>
	public string DocumentId { get; }

	/// <summary>The current text of the document, which the ranges of the outline index into.</summary>
	public string DocumentText { get; }

	/// <summary>The editor that shows the document.</summary>
	public IEditorNavigationTarget Target { get; }
}
