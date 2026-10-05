using Nickelony.IDEKit.LanguageFeatures.Editing;

namespace Nickelony.IDEKit.LanguageFeatures.Rename;

/// <summary>Computes the edits that rename a symbol, without applying them.</summary>
/// <remarks>
/// Implementations must be safe to call from any thread and must not touch UI state. Implementations reject a <see langword="null"/>
/// request with <see cref="ArgumentNullException"/>, and the call observes the supplied cancellation token.
/// </remarks>
public interface IRenameProvider
{
	/// <summary>Returns the edits that rename the symbol at the requested offset.</summary>
	/// <param name="request">The document, symbol position, and replacement name.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// The workspace edit to apply, or <see langword="null"/> when rename is unsupported, the requested name is invalid (for example blank),
	/// no changes are available, or the provider's edit contains resource operations that <see cref="WorkspaceEdit"/> cannot represent.
	/// </returns>
	Task<WorkspaceEdit?> GetRenameEditsAsync(RenameRequest request, CancellationToken cancellationToken = default);
}
