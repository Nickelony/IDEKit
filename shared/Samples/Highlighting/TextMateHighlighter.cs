using System.Collections.Concurrent;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.TextMate;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.TextMate;
#endif

namespace Nickelony.Samples;

/// <summary>Highlights source code with the TextMate grammar of a language and the token rules of the theme.</summary>
internal sealed class TextMateHighlighter : ISampleHighlighter
{
	// The grammars are immutable and costly to load, so every editor and code block of the sample shares them.
	private static readonly ConcurrentDictionary<string, IGrammar> s_grammars = new(StringComparer.Ordinal);

	private readonly TextMateHighlightingSession _session;

	/// <param name="editor">The editor whose text the highlighting colors.</param>
	/// <param name="languageId">The TextMate language identifier of the grammar, such as "lua".</param>
	/// <param name="theme">The theme whose token rules color the text.</param>
	internal TextMateHighlighter(TextEditor editor, string languageId, SampleTheme theme) =>
		_session = TextMateHighlightingSession.Start(editor.TextArea.TextView, s_grammars.GetOrAdd(languageId, LoadGrammar), theme.TokenTheme);

	public void ApplyTheme(SampleTheme theme) => _session.Theme = theme.TokenTheme;

	public void Dispose() => _session.Dispose();

	private static IGrammar LoadGrammar(string languageId)
	{
		var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
		return new Registry(registryOptions).LoadGrammar(registryOptions.GetScopeByLanguageId(languageId));
	}
}
