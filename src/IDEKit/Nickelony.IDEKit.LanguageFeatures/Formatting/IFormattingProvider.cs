using Nickelony.IDEKit.LanguageFeatures.Editing;

namespace Nickelony.IDEKit.LanguageFeatures.Formatting;

/// <summary>Computes the edits that format a document, without applying them.</summary>
/// <remarks>
/// Implementations must be safe to call from any thread and must not touch UI state. Implementations reject a <see langword="null"/>
/// request with <see cref="ArgumentNullException"/>, and the call observes the supplied cancellation token. A synchronous whole-document
/// text transform that needs no provider round trip implements <c>IDocumentFormatter</c> in <c>Nickelony.IDEKit.Core</c> instead, and a host adapts
/// one contract to the other.
/// </remarks>
public interface IFormattingProvider
{
	/// <summary>Returns the edits that format the requested document.</summary>
	/// <param name="request">The document and formatting options.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The workspace edit to apply, or <see langword="null"/> when formatting is unsupported or no changes are available.</returns>
	Task<WorkspaceEdit?> GetFormattingEditsAsync(FormattingRequest request, CancellationToken cancellationToken = default);
}
