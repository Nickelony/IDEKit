namespace Nickelony.IDEKit.IntelliSense.Signatures;

/// <summary>
/// Resolves signature help content from the current document context.
/// </summary>
/// <remarks>
/// Implementations must be safe to call from any thread: the request is an immutable snapshot and
/// no UI state may be touched. The request's trigger context, when present, describes retriggers so
/// implementations can keep the selected overload stable across content changes. Implementations
/// must reject a <see langword="null"/> request with <see cref="ArgumentNullException"/>.
/// </remarks>
public interface ITextSignatureHelpProvider
{
	/// <summary>
	/// Gets the signature help information for the supplied request.
	/// </summary>
	/// <param name="request">The current document and caret-position request.</param>
	/// <returns>The resolved signature help information, or <see langword="null"/> when none is available.</returns>
	TextSignatureHelp? GetSignatureHelp(TextSignatureHelpRequest request);
}
