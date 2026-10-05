using Nickelony.IDEKit.Core.Media;
using Nickelony.IDEKit.Core.Themes;
using Nickelony.IDEKit.Markdown;
using Nickelony.IDEKit.TextMate;

namespace Nickelony.Samples;

/// <summary>The themes of the sample, built from the Tomorrow palettes, and the catalog that looks them up by name.</summary>
internal static class SampleThemes
{
	private static readonly Palette s_light = new(
		Background: "#FFFFFF",
		Foreground: "#4D4D4C",
		Surface: "#F5F5F5",
		Border: "#D6D6D6",
		Selection: "#D6E4F5",
		Muted: "#8E908C",
		Keyword: "#4271AE",
		Control: "#8959A8",
		Local: "#C82829",
		Text: "#718C00",
		Number: "#F5871F",
		Function: "#B07D00",
		Type: "#3E999F",
		Variable: "#8959A8"
	);

	// The dark palette is the Lua theme of Tomb Editor.
	private static readonly Palette s_dark = new(
		Background: "#2D2D2D",
		Foreground: "#CCCCCC",
		Surface: "#393939",
		Border: "#515151",
		Selection: "#515151",
		Muted: "#999999",
		Keyword: "#6699CC",
		Control: "#CC99CC",
		Local: "#F99157",
		Text: "#F99157",
		Number: "#99CC99",
		Function: "#FFCC66",
		Type: "#66CCCC",
		Variable: "#E6C8FF"
	);

	internal static ThemeCatalog<SampleTheme> Catalog { get; } =
		new(
			[Create("Light", isDark: false, s_light), Create("Dark", isDark: true, s_dark)],
			defaultThemeName: "Light",
			new ThemeCatalogHooks<SampleTheme>(theme => theme.Name)
		);

	private static SampleTheme Create(string name, bool isDark, Palette palette)
	{
		ArgbColor surface = Color(palette.Surface);

		return new SampleTheme(
			name,
			isDark,
			Background: Color(palette.Background),
			Foreground: Color(palette.Foreground),
			surface,
			Border: Color(palette.Border),
			Selection: Color(palette.Selection),
			LineNumber: Color(palette.Muted),
			Keyword: Color(palette.Keyword),
			Text: Color(palette.Text),
			Number: Color(palette.Number),
			Function: Color(palette.Function),

			TokenTheme: new TextMateTokenTheme
			{
				// The scopes are the ones the Lua and C# grammars of TextMateSharp emit, most general first.
				Rules =
				[
					Rule("comment", palette.Muted, "italic"),
					Rule("string, constant.character.escape", palette.Text),
					Rule("constant.numeric", palette.Number),
					Rule("constant.language", palette.Type, "bold"),
					Rule("keyword, storage", palette.Keyword, "bold"),
					Rule("keyword.control.goto, keyword.control.return, keyword.control.break", palette.Control),
					Rule("keyword.control.local", palette.Local, "bold"),
					Rule("entity.name.class, entity.name.type, support.class, support.type, variable.language.self", palette.Type, "bold"),
					Rule("variable.parameter, variable.other.object", palette.Variable),
					Rule("entity.name.function, support.function, support.function.library, support.function.any-method", palette.Function, "bold"),
					Rule("keyword.operator, punctuation", palette.Foreground),
					Rule("markup.heading, entity.name.section, punctuation.definition.heading", palette.Keyword, "bold"),
					Rule("markup.bold", palette.Foreground, "bold"),
					Rule("markup.italic", palette.Foreground, "italic"),
					Rule("markup.inline.raw, markup.fenced_code, markup.raw", palette.Text),
					Rule("markup.underline.link, string.other.link", palette.Function),
					Rule("markup.quote", palette.Muted, "italic"),
					Rule("punctuation.definition.list, markup.list", palette.Control),
				],
			},

			MarkdownTheme: MarkdownRenderTheme.Default with
			{
				Foreground = Color(palette.Foreground),
				SurfaceBackground = surface,
				LinkForeground = Color(palette.Keyword),
				MaxWidth = 560,

				// The default border blend suits a page; in a small popup a lighter border keeps the frame quiet.
				BorderBlendRatio = 0.12,
			}
		);
	}

	private static TextMateTokenThemeRule Rule(string scope, string foreground, string? fontStyle = null)
	{
		return new()
		{
			Scope = scope,
			Foreground = foreground,
			FontStyle = fontStyle,
		};
	}

	private static ArgbColor Color(string text) => ArgbColor.TryParse(text, out ArgbColor color) ? color : ArgbColor.Black;

	/// <summary>The colors of one theme as the hexadecimal strings that the TextMate rules take.</summary>
	private sealed record Palette(
		string Background,
		string Foreground,
		string Surface,
		string Border,
		string Selection,
		string Muted,
		string Keyword,
		string Control,
		string Local,
		string Text,
		string Number,
		string Function,
		string Type,
		string Variable
	);
}
