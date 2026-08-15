namespace Nickelony.IDEKit.IntelliSense.Hover;

/// <summary>
/// Resolves hover content from the current document context.
/// </summary>
/// <remarks>
/// Implementations must be safe to call from any thread: the request is an immutable snapshot and
/// no UI state may be touched. Implementations must reject a <see langword="null"/> request with
/// <see cref="ArgumentNullException"/>.
/// </remarks>
public interface ITextHoverProvider
{
	/// <summary>
	/// Gets the hover information for the supplied request.
	/// </summary>
	/// <param name="request">The current document and hover-position request.</param>
	/// <returns>The resolved hover information, or <see langword="null"/> when none is available.</returns>
	TextHoverInfo? GetHoverInfo(TextHoverRequest request);
}
