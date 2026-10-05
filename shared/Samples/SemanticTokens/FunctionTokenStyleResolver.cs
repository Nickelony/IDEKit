using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvaloniaEdit.Media;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvalonEdit.Media;
using Nickelony.IDEKit.AvalonEdit.Rendering;
#endif

namespace Nickelony.Samples;

/// <summary>Gives a function name the function color of the theme in bold, and leaves every other token to the TextMate grammar.</summary>
internal sealed class FunctionTokenStyleResolver : ISemanticTokenStyleResolver
{
	private TextRunStyle _functionStyle;

	/// <param name="theme">The theme whose function color the style takes.</param>
	internal FunctionTokenStyleResolver(SampleTheme theme) => _functionStyle = CreateStyle(theme);

	/// <summary>Takes the function color of another theme.</summary>
	/// <remarks>
	/// The colorizer reads the new style when it rebuilds.
	/// </remarks>
	internal void ApplyTheme(SampleTheme theme) => _functionStyle = CreateStyle(theme);

	public TextRunStyle Resolve(SemanticToken token) => token.Type == SemanticTokenTypes.Function ? _functionStyle : default;

	private static TextRunStyle CreateStyle(SampleTheme theme) =>
		new(Foreground: theme.Function.ToBrush(), IsBold: true, IsItalic: false, TextDecorations: null);
}
