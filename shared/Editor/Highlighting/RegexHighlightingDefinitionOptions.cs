using Nickelony.IDEKit.Core.Highlighting;
#if AVALONIAEDIT
using Avalonia.Media;
#else
using System.Windows.Media;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.Highlighting;
#endif

/// <summary>
/// Configures a <see cref="RegexHighlightingDefinition"/> built through its
/// <see cref="RegexHighlightingDefinition.Create(string, Func{IEnumerable{RegexHighlightingRule}}, RegexHighlightingDefinitionOptions?)"/>
/// factories or supplied to the protected constructor from a subclass.
/// </summary>
/// <param name="CacheVersion">
/// The function that returns the cache version. The main rule set is rebuilt when the returned value
/// changes; <see langword="null"/> (the default) builds the rule set once and never rebuilds it.
/// </param>
/// <param name="FallbackColor">
/// The fallback foreground color used when a rule color value cannot be parsed. The default
/// <see langword="null"/> leaves the foreground unset so the editor's theme color is used.
/// </param>
public sealed record RegexHighlightingDefinitionOptions(Func<int>? CacheVersion = null, Color? FallbackColor = null)
{
	/// <summary>
	/// Gets the default options: no cache version (the rule set is built once and never rebuilt) and no fallback
	/// color (unparsable rule colors keep the editor's theme color). A host overrides only the members it needs
	/// with a <c>with</c> expression.
	/// </summary>
	public static RegexHighlightingDefinitionOptions Default { get; } = new();
}
