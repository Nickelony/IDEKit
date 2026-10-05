using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Nickelony.IDEKit.Core.Highlighting;
#if AVALONIAEDIT
using AvaloniaEdit.Highlighting;
#else
using ICSharpCode.AvalonEdit.Highlighting;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Highlighting;

#else

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

#endif

/// <summary>Provides a highlighting definition from regex-based rules.</summary>
/// <remarks>
/// <para>
/// The main rule set is built lazily and is safe for concurrent access; <see cref="RuleSetCache{TRuleSet}"/> owns the build, the cache version
/// and the change event. A definition over static data (no cache version) builds once; a definition over live data is rebuilt when its cache
/// version changes.
/// </para>
/// <para>
/// A definition is created either from providers through the
/// <see cref="Create(string, Func{IEnumerable{RegexHighlightingRule}}, Func{IEnumerable{RegexHighlightingSpan}}, RegexHighlightingDefinitionOptions?)"/>
/// factories, or by subclassing and supplying the rules and spans through <see cref="BuildRules"/> and <see cref="BuildSpans"/>; either shape feeds
/// the same lazily built, version-aware rule set.
/// </para>
/// <para>
/// A rule style without a color leaves the foreground unset so the editor's theme color is used,
/// and font styles are overridden only when the style requests them. While a span is open, the rule set of the innermost open span applies
/// instead of this definition's per-line rules; see <see cref="RegexHighlightingSpan"/> for how a span's rules and its nesting behave.
/// </para>
/// <para>
/// Rules and spans are evaluated during rendering, so hosts should supply
/// regexes with a bounded match timeout; see <see cref="RegexHighlightingRule"/>. A pattern that uses <see cref="RegexOptions.RightToLeft"/>,
/// or that matches empty text on a probe string, is rejected when the rule set is built; see <see cref="RegexHighlightingRuleSet.Create"/>.
/// The first matching rule wins: when two rules match at the same position, the rule returned first by <see cref="BuildRules"/> is used.
/// A rule match spans only the current line; delimiter-based spans that stay highlighted across lines (block comments, long strings)
/// belong to <see cref="BuildSpans"/>.
/// </para>
/// </remarks>
public abstract class RegexHighlightingDefinition : IHighlightingDefinition
{
	private static readonly ReadOnlyDictionary<string, string> s_emptyProperties = new(new Dictionary<string, string>());
	private static readonly ReadOnlyCollection<HighlightingColor> s_emptyNamedColors = Array.AsReadOnly(Array.Empty<HighlightingColor>());

	private readonly RuleSetCache<HighlightingRuleSet> _ruleSets;

	/// <param name="name">The definition name and the name of its main rule set.</param>
	/// <param name="options">
	/// The optional cache-version setting. <see langword="null"/> builds the rule set once; see <see cref="RegexHighlightingDefinitionOptions"/>.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	protected RegexHighlightingDefinition(string name, RegexHighlightingDefinitionOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(name);

		options ??= RegexHighlightingDefinitionOptions.Default;
		Name = name;

		_ruleSets = new RuleSetCache<HighlightingRuleSet>(BuildRuleSet, options.CacheVersion);
		_ruleSets.Changed += (_, _) => RuleSetChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Raised after the main rule set is rebuilt because the configured cache version changed.</summary>
	/// <remarks>
	/// <para>
	/// The editor caches highlighted lines and does not observe a rebuilt rule set by itself, so a host can subscribe and refresh the editor's
	/// highlighting (for example by reinstalling the highlighter) when the event arrives. A handler can access <see cref="MainRuleSet"/> and receive
	/// the freshly published rule set. The initial build of a definition does not raise the event.
	/// </para>
	/// <para>
	/// The event is raised on the thread that triggered the rebuild, so a host refreshing the editor must marshal that work to the editor's thread.
	/// A handler that throws propagates the exception out of the <see cref="MainRuleSet"/> access that triggered the rebuild.
	/// </para>
	/// </remarks>
	public event EventHandler? RuleSetChanged;

	/// <exception cref="InvalidOperationException">
	/// The property was accessed re-entrantly from the cache version callback, or from <see cref="BuildRules"/> or <see cref="BuildSpans"/>
	/// on the building thread; the access site throws instead of recursing.
	/// </exception>
	/// <remarks>
	/// The rule set is built on first access, is cached, and is rebuilt when the configured cache version changes. The cache-version callback is read
	/// once per access.
	/// </remarks>
	public HighlightingRuleSet MainRuleSet => _ruleSets.Value;

	/// <inheritdoc/>
	public string Name { get; }

	/// <summary>An empty collection because this definition has no named colors.</summary>
	public IEnumerable<HighlightingColor> NamedHighlightingColors => s_emptyNamedColors;

	/// <summary>An empty property collection.</summary>
	public IDictionary<string, string> Properties => s_emptyProperties;

	/// <summary>Returns <see langword="null"/> because this definition has no named colors.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public HighlightingColor? GetNamedColor(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		return null;
	}

	/// <summary>
	/// Returns the main rule set when <paramref name="name"/> matches <see cref="Name"/>; otherwise, returns <see langword="null"/>.
	/// </summary>
	/// <returns>The main rule set for <see cref="Name"/>, or <see langword="null"/> when no match exists.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The main rule set is built re-entrantly; see <see cref="MainRuleSet"/> for the build-time rules.
	/// </exception>
	public HighlightingRuleSet? GetNamedRuleSet(string name)
	{
		ArgumentNullException.ThrowIfNull(name);

		// Compare the name before touching the rule set so unknown names do not build it.
		return name == Name ? MainRuleSet : null;
	}

	/// <summary>Provides the rules for the main rule set.</summary>
	/// <returns>The rules that build the main rule set.</returns>
	/// <remarks>
	/// Invoked while the main rule set is built, so accessing <see cref="MainRuleSet"/>, or <see cref="GetNamedRuleSet(string)"/> with a name
	/// that matches <see cref="Name"/>, from this method throws <see cref="InvalidOperationException"/> instead of recursing. Other names return
	/// <see langword="null"/> without touching the rule set. Concurrent first accesses can build concurrently, so an implementation that shares host
	/// state must be thread-safe.
	/// </remarks>
	protected abstract IEnumerable<RegexHighlightingRule> BuildRules();

	/// <summary>Provides the delimiter-based spans for the main rule set.</summary>
	/// <returns>The spans that build the main rule set; the default is an empty sequence.</returns>
	/// <remarks>
	/// Each span carries its own rule set while it is open, so the main rules do not apply inside a span
	/// unless the span supplies <see cref="RegexHighlightingSpan.Rules"/>; see <see cref="RegexHighlightingSpan"/> for the span semantics.
	/// The same build-time constraints as for <see cref="BuildRules"/> apply: accessing the main rule set
	/// from this method throws <see cref="InvalidOperationException"/>, and an implementation that shares host state must be thread-safe.
	/// </remarks>
	protected virtual IEnumerable<RegexHighlightingSpan> BuildSpans() => [];

	/// <summary>Creates a definition from rule and optional span providers without subclassing.</summary>
	/// <param name="name">The definition name and the name of its main rule set.</param>
	/// <param name="rulesProvider">Provides the rules, invoked while the main rule set is built.</param>
	/// <param name="spansProvider">
	/// Provides the spans, invoked while the main rule set is built, or <see langword="null"/> for a definition with no spans.
	/// </param>
	/// <param name="options">
	/// The optional cache-version setting. <see langword="null"/> builds the rule set once; see <see cref="RegexHighlightingDefinitionOptions"/>.
	/// </param>
	/// <returns>The created definition.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="rulesProvider"/> is <see langword="null"/>.</exception>
	public static RegexHighlightingDefinition Create(
		string name,
		Func<IEnumerable<RegexHighlightingRule>> rulesProvider,
		Func<IEnumerable<RegexHighlightingSpan>>? spansProvider = null,
		RegexHighlightingDefinitionOptions? options = null
	)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(rulesProvider);

		return new DelegateRegexHighlightingDefinition(name, rulesProvider, spansProvider, options);
	}

	private sealed class DelegateRegexHighlightingDefinition : RegexHighlightingDefinition
	{
		private readonly Func<IEnumerable<RegexHighlightingRule>> _rulesProvider;
		private readonly Func<IEnumerable<RegexHighlightingSpan>>? _spansProvider;

		internal DelegateRegexHighlightingDefinition(
			string name,
			Func<IEnumerable<RegexHighlightingRule>> rulesProvider,
			Func<IEnumerable<RegexHighlightingSpan>>? spansProvider,
			RegexHighlightingDefinitionOptions? options
		)
			: base(name, options)
		{
			_rulesProvider = rulesProvider;
			_spansProvider = spansProvider;
		}

		protected override IEnumerable<RegexHighlightingRule> BuildRules() => _rulesProvider();

		protected override IEnumerable<RegexHighlightingSpan> BuildSpans() => _spansProvider is null ? [] : _spansProvider();
	}

	/// <summary>Builds the neutral rule set from the definition's rules and spans, then converts it to the editor's rule set.</summary>
	/// <returns>The built rule set.</returns>
	/// <remarks>
	/// The neutral build validates every pattern and rejects an invalid one before any rule
	/// enters the set; see <see cref="RegexHighlightingRuleSet.Create"/>. A span without an end pattern enters the converted set with an expression
	/// that cannot match, so the span stays open to the document end, and a span with <see cref="RegexHighlightingSpan.Rules"/> enters the set
	/// with those rules as its nested rule set.
	/// </remarks>
	private HighlightingRuleSet BuildRuleSet() =>
		RegexHighlightingRuleSetConversion.Convert(RegexHighlightingRuleSet.Create(Name, BuildRules(), BuildSpans()));
}
