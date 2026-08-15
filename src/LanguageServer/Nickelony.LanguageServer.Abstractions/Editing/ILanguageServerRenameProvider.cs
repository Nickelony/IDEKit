namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Produces symbol rename edits.
/// </summary>
public interface ILanguageServerRenameProvider
{
	/// <summary>
	/// Gets a value indicating whether symbol rename is supported by the current ready session.
	/// </summary>
	/// <remarks>
	/// This flag is <see langword="false"/> until a language-server session is ready and supports rename; the
	/// lazy-startup and capability-gating contract is on <see cref="ILanguageServerIntelliSenseProvider"/>.
	/// </remarks>
	bool SupportsRename { get; }

	/// <summary>
	/// Produces rename edits for the supplied symbol location.
	/// </summary>
	/// <param name="request">The document, symbol position, and replacement name.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// The workspace edit to apply, or <see langword="null"/> when rename is unsupported, the requested name is
	/// invalid (for example blank), no changes are available, or the server's edit contains resource operations
	/// that this model cannot represent (<see cref="TextWorkspaceEdit"/>).
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	Task<TextWorkspaceEdit?> RenameSymbolAsync(LanguageServerRenameRequest request, CancellationToken cancellationToken = default);
}
