using Nickelony.IDEKit.Core.Media;
using Nickelony.IDEKit.Markdown;
using Nickelony.IDEKit.TextMate;

namespace Nickelony.Samples;

/// <summary>A look for the sample: a palette of <see cref="ArgbColor"/> values and the neutral themes derived from it.</summary>
/// <param name="Name">The name the theme catalog looks the theme up by.</param>
/// <param name="IsDark">Whether the theme is dark, which selects the variant of the window chrome that follows it.</param>
/// <param name="Background">The editor background.</param>
/// <param name="Foreground">The editor text color.</param>
/// <param name="Surface">The background of popups, menus and the completion window.</param>
/// <param name="Border">The border of popups and menus.</param>
/// <param name="Selection">The background of selected text.</param>
/// <param name="LineNumber">The color of the line numbers.</param>
/// <param name="Keyword">The color of keywords and of the property names of the settings file.</param>
/// <param name="Text">The color of string literals.</param>
/// <param name="Number">The color of numeric literals.</param>
/// <param name="Function">The color of function names, which the semantic tokens apply.</param>
/// <param name="TokenTheme">The TextMate rules that color the document.</param>
/// <param name="MarkdownTheme">The theme the hover and signature-help popups render Markdown with.</param>
internal sealed record SampleTheme(
	string Name,
	bool IsDark,
	ArgbColor Background,
	ArgbColor Foreground,
	ArgbColor Surface,
	ArgbColor Border,
	ArgbColor Selection,
	ArgbColor LineNumber,
	ArgbColor Keyword,
	ArgbColor Text,
	ArgbColor Number,
	ArgbColor Function,
	TextMateTokenTheme TokenTheme,
	MarkdownRenderTheme MarkdownTheme
);
