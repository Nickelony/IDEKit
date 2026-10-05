using System.Text.RegularExpressions;
using Nickelony.IDEKit.Core.Highlighting;
using Nickelony.IDEKit.Core.Media;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Highlighting;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Highlighting;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

namespace Nickelony.Samples;

/// <summary>Highlights JSON with regular-expression rules that rebuild when the theme changes.</summary>
internal sealed class JsonHighlighter : ISampleHighlighter
{
	private readonly TextEditor _editor;
	private readonly RegexHighlightingDefinition _definition;
	private readonly UiDispatcher _dispatcher = UiDispatcher.FromCurrent();

	private SampleTheme _theme;
	private int _themeVersion;

	/// <param name="editor">The editor whose text the highlighting colors.</param>
	/// <param name="theme">The theme whose colors the rules use.</param>
	internal JsonHighlighter(TextEditor editor, SampleTheme theme)
	{
		_editor = editor;
		_theme = theme;

		_definition = RegexHighlightingDefinition.Create(
			"Json",
			BuildRules,
			options: new RegexHighlightingDefinitionOptions(CacheVersion: () => _themeVersion)
		);

		// The definition rebuilds lazily and the editor does not observe a rebuild, and the event arrives on the thread that triggered it.
		_definition.RuleSetChanged += HandleRuleSetChanged;
		_editor.SyntaxHighlighting = _definition;
	}

	public void ApplyTheme(SampleTheme theme)
	{
		_theme = theme;
		_themeVersion++;

		// Assigning the same instance raises no change, so the editor reads the rebuilt rules only after the property was cleared.
		_editor.SyntaxHighlighting = null;
		_editor.SyntaxHighlighting = _definition;
	}

	public void Dispose()
	{
		_definition.RuleSetChanged -= HandleRuleSetChanged;
		_editor.SyntaxHighlighting = null;
	}

	private static RegexHighlightingRule Rule(string pattern, ArgbColor color) =>
		new(new Regex(pattern, RegexOptions.CultureInvariant), new RegexHighlightingStyle(color));

	private void HandleRuleSetChanged(object? sender, EventArgs e) => _dispatcher.Post(() => _editor.SyntaxHighlighting = _definition);

	private IEnumerable<RegexHighlightingRule> BuildRules()
	{
		// The most specific rule comes first: a property name is a string that a colon follows.
		yield return Rule("\"(?:[^\"\\\\]|\\\\.)*\"(?=\\s*:)", _theme.Keyword);
		yield return Rule("\"(?:[^\"\\\\]|\\\\.)*\"", _theme.Text);
		yield return Rule(@"-?\b\d+(?:\.\d+)?\b", _theme.Number);
		yield return Rule(@"\b(?:true|false|null)\b", _theme.Keyword);
	}
}
