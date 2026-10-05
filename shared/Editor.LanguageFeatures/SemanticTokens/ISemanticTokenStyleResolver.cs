using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
using Nickelony.IDEKit.AvalonEdit.Rendering;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;

#endif

/// <summary>Resolves a semantic token to its visual style.</summary>
/// <remarks>
/// Implementations can use the token type and modifiers with their own theme model, so the colorizer stays style-neutral. The resolver runs
/// synchronously while the colorizer builds or rebuilds its styled map. A resolver that throws aborts the current token application:
/// the colorizer keeps its currently applied styling in effect, and the caller can retry the push once the resolver configuration is fixed.
/// Return a style without formatting when a token should not be styled.
/// </remarks>
public interface ISemanticTokenStyleResolver
{
	TextRunStyle Resolve(SemanticToken token);
}
