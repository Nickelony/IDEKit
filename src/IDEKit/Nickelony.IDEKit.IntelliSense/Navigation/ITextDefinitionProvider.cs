namespace Nickelony.IDEKit.IntelliSense.Navigation;

/// <summary>
/// Resolves a navigable definition location for a symbol in the current document context.
/// </summary>
/// <remarks>
/// Implementations must be safe to call from any thread: the request is an immutable snapshot and
/// no UI state may be touched. Implementations must reject a <see langword="null"/> request with
/// <see cref="ArgumentNullException"/>, and they must resolve a blank
/// <see cref="TextDefinitionRequest.SymbolName"/> (the "no symbol at the requested position" state)
/// to <see langword="null"/> instead of failing.
/// </remarks>
public interface ITextDefinitionProvider
{
	/// <summary>
	/// Gets the definition location for the supplied request.
	/// </summary>
	/// <param name="request">The current document and symbol lookup request.</param>
	/// <returns>The resolved definition location, or <see langword="null"/> when no definition can be resolved.</returns>
	TextDefinitionLocation? GetDefinition(TextDefinitionRequest request);
}
