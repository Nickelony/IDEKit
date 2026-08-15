#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
using Nickelony.IDEKit.AvalonEdit.Rendering;
#endif
using Nickelony.IDEKit.IntelliSense.SemanticTokens;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Highlighting;
#endif

/// <summary>
/// Resolves a semantic token to its visual style. Implementations can use the token type and
/// modifiers with their own theme model, so the colorizer stays style-neutral.
/// </summary>
/// <remarks>
/// The resolver runs synchronously while the colorizer builds or rebuilds its styled map. A resolver that
/// throws aborts the current token application: the colorizer keeps the previously applied set in effect,
/// and the caller can retry the push once the resolver configuration is fixed. Return a style without
/// formatting when a token should not be styled.
/// </remarks>
public interface ISemanticTokenStyleResolver
{
	/// <summary>
	/// Resolves the visual style for the given semantic token.
	/// </summary>
	/// <param name="token">The semantic token to resolve.</param>
	/// <returns>The resolved style.</returns>
	TextRunStyle Resolve(TextSemanticToken token);
}
