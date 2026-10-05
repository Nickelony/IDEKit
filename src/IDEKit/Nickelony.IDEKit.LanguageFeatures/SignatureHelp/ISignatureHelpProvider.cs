namespace Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

/// <summary>Resolves signature help content from the current document context.</summary>
/// <remarks>
/// Implementations must be safe to call from any thread: the request is an immutable snapshot and no UI state may be touched.
/// Implementations must reject a <see langword="null"/> request with <see cref="ArgumentNullException"/>, and the call observes the supplied
/// cancellation token.
/// </remarks>
public interface ISignatureHelpProvider
{
	/// <summary>Returns the signature help information for the supplied request.</summary>
	/// <param name="request">The current document and caret-position request.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The resolved signature help information, or <see langword="null"/> when none is available.</returns>
	Task<SignatureHelpInformation?> GetSignatureHelpAsync(SignatureHelpRequest request, CancellationToken cancellationToken = default);
}
