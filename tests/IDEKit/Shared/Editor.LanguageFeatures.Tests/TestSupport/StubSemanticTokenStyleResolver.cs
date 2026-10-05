#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using Brush = Avalonia.Media.IBrush;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using Brush = System.Windows.Media.Brush;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>A configurable <see cref="ISemanticTokenStyleResolver"/> test double shared by the semantic-token and wiring tests.</summary>
internal sealed class StubSemanticTokenStyleResolver : ISemanticTokenStyleResolver
{
	private Brush? _foreground = Brushes.Red;

	private bool _isBold;
	private bool _isItalic;

	private string? _boldModifier;
	private TextDecorationCollection? _decorations;
	private Func<SemanticToken, TextRunStyle>? _styleFactory;

	/// <summary>The number of <see cref="Resolve(SemanticToken)"/> calls the resolver received.</summary>
	public int ResolveCallCount { get; private set; }

	/// <summary>Configures the style the resolver returns until it is configured again.</summary>
	/// <param name="foreground">The returned foreground brush, or <see langword="null"/> for none.</param>
	/// <param name="isBold">Whether the returned style is bold.</param>
	/// <param name="isItalic">Whether the returned style is italic.</param>
	/// <param name="boldModifier">A modifier whose presence makes an otherwise plain style bold.</param>
	/// <param name="decorations">The returned text decorations, or <see langword="null"/> for none.</param>
	public void SetStyle(
		Brush? foreground,
		bool isBold = false,
		bool isItalic = false,
		string? boldModifier = null,
		TextDecorationCollection? decorations = null
	)
	{
		_foreground = foreground;
		_isBold = isBold;
		_isItalic = isItalic;
		_boldModifier = boldModifier;
		_decorations = decorations;
		_styleFactory = null;
	}

	/// <summary>Configures a factory that computes the style from the token, so a test can throw or branch per token.</summary>
	/// <param name="styleFactory">The factory the resolver delegates to.</param>
	public void SetStyleFactory(Func<SemanticToken, TextRunStyle> styleFactory) => _styleFactory = styleFactory;

	/// <inheritdoc/>
	public TextRunStyle Resolve(SemanticToken token)
	{
		ResolveCallCount++;

		if (_styleFactory is not null)
			return _styleFactory(token);

		bool isBold = _isBold || (_boldModifier is not null && token.HasModifier(_boldModifier));
		return new TextRunStyle(Foreground: _foreground, isBold, IsItalic: _isItalic, TextDecorations: _decorations);
	}
}
