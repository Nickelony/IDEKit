#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
#else
using ICSharpCode.AvalonEdit.Highlighting;
using System.Windows.Media;
#endif
using Nickelony.IDEKit.Core.Highlighting;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.Highlighting;
#endif

/// <summary>
/// Provides a highlighting definition from regex-based rules.
/// </summary>
/// <remarks>
/// <para>
/// The main rule set is built lazily and is safe for concurrent access. A definition over static data
/// (no cache version) builds once through a <see cref="Lazy{T}"/>. A definition over live data is
/// rebuilt when its cache version changes; for that case no lock is held while the cache-version
/// callback or <see cref="BuildRules"/> runs, so accesses never block on host code: two threads that
/// observe a version change can build concurrently, and each installs its rule set with an atomic
/// publication that replaces the snapshot current at that moment. A publication that replaces a snapshot
/// of a different version raises <see cref="RuleSetChanged"/>; the first build of a definition does not.
/// If a publication loses a race against a later one, the next access observes the version mismatch and
/// rebuilds, so the served rule set converges on the current version.
/// </para>
/// <para>
/// A definition is created either from providers through the
/// <see cref="Create(string, Func{IEnumerable{RegexHighlightingRule}}, RegexHighlightingDefinitionOptions?)"/>
/// factories, or by subclassing and supplying the rules and spans through <see cref="BuildRules"/> and
/// <see cref="BuildSpans"/>; both shapes feed the same lazily built, version-aware rule set.
/// </para>
/// <para>
/// A rule style without a color leaves the foreground unset so the editor's theme color is used, and font
/// styles are overridden only when the style requests them. The editor evaluates the rule set of the
/// innermost open span while a span is open, so this definition's per-line rules do not apply inside a
/// span, and spans do not nest: a second begin match inside an open span is plain text. A span that carries
/// <see cref="RegexHighlightingSpan.Rules"/> supplies the rule set that applies inside it.
/// </para>
/// <para>
/// Rules and spans are evaluated during rendering, so hosts should supply regexes with a bounded match
/// timeout; see <see cref="RegexHighlightingRule"/>. A pattern that can match empty text and a pattern that
/// uses <see cref="RegexOptions.RightToLeft"/> are both rejected when the rule set is built. When two rules
/// match at the same position the rule returned first by <see cref="BuildRules"/> wins, so order the most
/// specific rules first; a rule match spans only the current line, so use <see cref="BuildSpans"/> for
/// delimiter-based spans (block comments, long strings) that stay highlighted across lines. The editor
/// engine rationale behind the build-time rejections and the scan order is documented in the package
/// README.
/// </para>
/// </remarks>
public abstract class RegexHighlightingDefinition : IHighlightingDefinition
{
	/// <summary>
	/// Stores a built rule set together with the cache version that produced it, so a reader can never
	/// observe a rule set and a version from different builds.
	/// </summary>
	/// <param name="ruleSet">The built rule set.</param>
	/// <param name="version">The cache version the rule set was built from.</param>
	private sealed class RuleSetSnapshot(HighlightingRuleSet ruleSet, int version)
	{
		public HighlightingRuleSet RuleSet { get; } = ruleSet;

		public int Version { get; } = version;
	}

	private static readonly ReadOnlyDictionary<string, string> s_emptyProperties = new(new Dictionary<string, string>());
	private static readonly ReadOnlyCollection<HighlightingColor> s_emptyNamedColors = Array.AsReadOnly(Array.Empty<HighlightingColor>());

	/// <summary>
	/// Tracks, per thread, the definitions being processed on it, whether their rule set is being
	/// built or their cache-version callback is running.
	/// The recursion guard is thread-local so a concurrent build on another thread cannot interfere
	/// with it, and it is per definition so composing from another definition remains possible.
	/// </summary>
	[ThreadStatic]
	private static HashSet<RegexHighlightingDefinition>? t_buildsInProgress;

	private readonly Func<int>? _cacheVersion;
	private readonly Color? _fallbackColor;
	private readonly Lazy<HighlightingRuleSet>? _staticRuleSet;

	private RuleSetSnapshot? _snapshot;

	/// <summary>
	/// Initializes a new instance of the <see cref="RegexHighlightingDefinition"/> class.
	/// </summary>
	/// <param name="name">The definition name and the name of its main rule set.</param>
	/// <param name="options">
	/// The optional cache-version and fallback-color settings. <see langword="null"/> builds the rule set
	/// once and leaves unparsable rule colors to the editor's theme color; see
	/// <see cref="RegexHighlightingDefinitionOptions"/>.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	protected RegexHighlightingDefinition(string name, RegexHighlightingDefinitionOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(name);

		options ??= RegexHighlightingDefinitionOptions.Default;

		Name = name;

		_cacheVersion = options.CacheVersion;
		_fallbackColor = options.FallbackColor;

		// A definition over static data (no cache version) builds through Lazy, which publishes the
		// single build for all readers without the version-snapshot machinery. PublicationOnly keeps a
		// failed build from being cached, so a transient build failure does not poison the definition and
		// the next access retries. The recursion guard still covers the build; see BuildStaticRuleSet.
		if (_cacheVersion is null)
			_staticRuleSet = new Lazy<HighlightingRuleSet>(BuildStaticRuleSet, LazyThreadSafetyMode.PublicationOnly);
	}

	/// <summary>
	/// Raised after the main rule set is rebuilt because the configured cache version changed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The editor caches highlighted lines and does not observe a rebuilt rule set by itself, so a host
	/// can subscribe and refresh the editor's highlighting (for example by reinstalling the highlighter)
	/// when the event arrives. The event is raised on the rebuilding thread after the build guard is
	/// released, so a handler can access <see cref="MainRuleSet"/> and receive the freshly published rule
	/// set. The initial build of a definition does not raise the event.
	/// </para>
	/// <para>
	/// The event is raised on the thread that triggered the rebuild, so a host refreshing the editor
	/// must marshal that work to the editor's thread. A handler that throws propagates the exception out
	/// of the <see cref="MainRuleSet"/> access that triggered the rebuild.
	/// </para>
	/// </remarks>
	public event EventHandler? RuleSetChanged;

	/// <summary>
	/// Gets the main rule set.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The rule set is built on first access, is cached, and is rebuilt when the configured cache version changes.
	/// The cache-version callback is read once per access.
	/// </para>
	/// <para>
	/// A <see cref="RuleSetChanged"/> handler runs after the guard is released, so it may access the rule set
	/// without triggering this exception.
	/// </para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// The property was accessed re-entrantly from the cache version callback, or from
	/// <see cref="BuildRules"/> or <see cref="BuildSpans"/> on the building thread; the access site throws
	/// instead of recursing.
	/// </exception>
	public HighlightingRuleSet MainRuleSet
	{
		get
		{
			// The guard runs before anything else so any access during this thread's own build or
			// cache-version callback fails fast, no matter whether a snapshot already exists.
			if (t_buildsInProgress?.Contains(this) == true)
			{
				throw new InvalidOperationException(
					"The main rule set must not be accessed from the cache version callback, BuildRules, or BuildSpans; " +
					"the access would recurse instead of returning a rule set.");
			}

			// Static data builds once through Lazy; static and versioned are mutually exclusive, because the
			// constructor creates the lazy path only when there is no cache version, so the null-forgiving
			// read is safe.
			if (_cacheVersion is null)
				return _staticRuleSet!.Value;

			RuleSetSnapshot? snapshot = Volatile.Read(ref _snapshot);

			// The version is read once per access, so a non-idempotent callback cannot make the snapshot and
			// the rebuild decision disagree about the version. The guard is armed while the callback runs:
			// the callback reads this definition only by mistake, and the access must throw instead of
			// re-invoking the callback.
			int version;

			EnterRecursionGuard();

			try
			{
				version = _cacheVersion();
			}
			finally
			{
				ExitRecursionGuard();
			}

			if (snapshot is not null && snapshot.Version == version)
				return snapshot.RuleSet;

			return BuildAndPublish(version);
		}
	}

	/// <summary>
	/// Builds the rule set of a definition over static data, arming the recursion guard so a re-entrant
	/// access from <see cref="BuildRules"/> or <see cref="BuildSpans"/> throws instead of recursing.
	/// </summary>
	/// <remarks>
	/// This is the factory the <see cref="Lazy{T}"/> backing <see cref="MainRuleSet"/> runs for a definition
	/// without a cache version. The guard is armed here because the getter checks it before reaching the
	/// lazy value, so a nested access during the build is rejected.
	/// </remarks>
	/// <returns>The built rule set.</returns>
	private HighlightingRuleSet BuildStaticRuleSet()
	{
		EnterRecursionGuard();

		try
		{
			return BuildRuleSet();
		}
		finally
		{
			ExitRecursionGuard();
		}
	}

	/// <summary>
	/// Builds the rule set, publishes it as the new snapshot, and raises <see cref="RuleSetChanged"/> when
	/// the publication replaced a snapshot of a different version.
	/// </summary>
	/// <param name="version">The cache version the built rule set belongs to.</param>
	/// <returns>The built rule set.</returns>
	private HighlightingRuleSet BuildAndPublish(int version)
	{
		EnterRecursionGuard();

		HighlightingRuleSet ruleSet;
		bool rebuilt;

		try
		{
			ruleSet = BuildRuleSet();

			// The snapshot is published against the snapshot that is current at publication time, not against
			// the one this thread read before building: a concurrent build that published first is observed,
			// so the event reports what this publication actually replaced.
			var newSnapshot = new RuleSetSnapshot(ruleSet, version);
			RuleSetSnapshot? replaced;

			do
			{
				replaced = Volatile.Read(ref _snapshot);
				rebuilt = replaced is not null && replaced.Version != version;
			}
			while (Interlocked.CompareExchange(ref _snapshot, newSnapshot, replaced) != replaced);
		}
		finally
		{
			ExitRecursionGuard();
		}

		// The event is raised after the guard is released: the documented handler workflow reinstalls the
		// editor's highlighter, which reads MainRuleSet synchronously.
		if (rebuilt)
			RuleSetChanged?.Invoke(this, EventArgs.Empty);

		return ruleSet;
	}

	/// <summary>
	/// Marks this definition as being processed on the current thread, so a re-entrant main-rule-set
	/// access throws instead of recursing.
	/// </summary>
	/// <remarks>
	/// The guard is thread-local so a concurrent build on another thread cannot interfere with it,
	/// and per definition so composing from another definition remains possible. It is armed both
	/// while <see cref="BuildRules"/> and <see cref="BuildSpans"/> run and while the cache-version
	/// callback runs, because the callback would otherwise re-invoke itself through the property.
	/// </remarks>
	private void EnterRecursionGuard()
		=> (t_buildsInProgress ??= []).Add(this);

	/// <summary>
	/// Releases the guard armed by <see cref="EnterRecursionGuard"/> and drops the thread-local
	/// set when no definition on this thread is being processed anymore.
	/// </summary>
	private void ExitRecursionGuard()
	{
		HashSet<RegexHighlightingDefinition>? buildsInProgress = t_buildsInProgress;

		if (buildsInProgress is null)
			return;

		buildsInProgress.Remove(this);

		if (buildsInProgress.Count == 0)
			t_buildsInProgress = null;
	}

	/// <inheritdoc/>
	public string Name { get; }

	/// <summary>
	/// Gets an empty collection because this definition has no named colors.
	/// </summary>
	public IEnumerable<HighlightingColor> NamedHighlightingColors => s_emptyNamedColors;

	/// <summary>
	/// Gets an empty property collection.
	/// </summary>
	public IDictionary<string, string> Properties => s_emptyProperties;

	/// <summary>
	/// Returns <see langword="null"/> because this definition has no named colors.
	/// </summary>
	/// <param name="name">The color name to look up.</param>
	/// <returns><see langword="null"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	public HighlightingColor? GetNamedColor(string name)
	{
		ArgumentNullException.ThrowIfNull(name);
		return null;
	}

	/// <summary>
	/// Returns the main rule set when <paramref name="name"/> matches <see cref="Name"/>;
	/// otherwise, returns <see langword="null"/>.
	/// </summary>
	/// <param name="name">The rule set name to look up.</param>
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

	/// <summary>
	/// Provides the rules for the main rule set.
	/// </summary>
	/// <remarks>
	/// Invoked while the main rule set is built, so accessing <see cref="MainRuleSet"/>, or
	/// <see cref="GetNamedRuleSet(string)"/> with a name that matches <see cref="Name"/>, from this method
	/// throws <see cref="InvalidOperationException"/> instead of recursing. Other names return
	/// <see langword="null"/> without touching the rule set. Concurrent first accesses can build
	/// concurrently, so an implementation that shares host state must be thread-safe.
	/// </remarks>
	/// <returns>The rules used to build the main rule set.</returns>
	protected abstract IEnumerable<RegexHighlightingRule> BuildRules();

	/// <summary>
	/// Provides the delimiter-based spans for the main rule set.
	/// </summary>
	/// <remarks>
	/// Each span carries its own rule set while it is open, so the main rules do not apply inside a span
	/// unless the span supplies <see cref="RegexHighlightingSpan.Rules"/>; see
	/// <see cref="RegexHighlightingSpan"/> for the span semantics. The same build-time constraints as for
	/// <see cref="BuildRules"/> apply: accessing the main rule set from this method throws
	/// <see cref="InvalidOperationException"/>, and an implementation that shares host state must be
	/// thread-safe.
	/// </remarks>
	/// <returns>The spans used to build the main rule set; the default is an empty sequence.</returns>
	protected virtual IEnumerable<RegexHighlightingSpan> BuildSpans() => [];

	/// <summary>
	/// Creates a definition from a rule provider without subclassing.
	/// </summary>
	/// <param name="name">The definition name and the name of its main rule set.</param>
	/// <param name="rulesProvider">Provides the rules, invoked while the main rule set is built.</param>
	/// <param name="options">
	/// The optional cache-version and fallback-color settings. <see langword="null"/> builds the rule set
	/// once and leaves unparsable rule colors to the editor's theme color; see
	/// <see cref="RegexHighlightingDefinitionOptions"/>.
	/// </param>
	/// <returns>The created definition.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/> or <paramref name="rulesProvider"/> is <see langword="null"/>.
	/// </exception>
	public static RegexHighlightingDefinition Create(
		string name,
		Func<IEnumerable<RegexHighlightingRule>> rulesProvider,
		RegexHighlightingDefinitionOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(rulesProvider);

		return new DelegateRegexHighlightingDefinition(name, rulesProvider, spansProvider: null, options);
	}

	/// <summary>
	/// Creates a definition from rule and span providers without subclassing.
	/// </summary>
	/// <param name="name">The definition name and the name of its main rule set.</param>
	/// <param name="rulesProvider">Provides the rules, invoked while the main rule set is built.</param>
	/// <param name="spansProvider">Provides the spans, invoked while the main rule set is built.</param>
	/// <param name="options">
	/// The optional cache-version and fallback-color settings. <see langword="null"/> builds the rule set
	/// once and leaves unparsable rule colors to the editor's theme color; see
	/// <see cref="RegexHighlightingDefinitionOptions"/>.
	/// </param>
	/// <returns>The created definition.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/>, <paramref name="rulesProvider"/>, or <paramref name="spansProvider"/> is
	/// <see langword="null"/>.
	/// </exception>
	public static RegexHighlightingDefinition CreateWithSpans(
		string name,
		Func<IEnumerable<RegexHighlightingRule>> rulesProvider,
		Func<IEnumerable<RegexHighlightingSpan>> spansProvider,
		RegexHighlightingDefinitionOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(rulesProvider);
		ArgumentNullException.ThrowIfNull(spansProvider);

		return new DelegateRegexHighlightingDefinition(name, rulesProvider, spansProvider, options);
	}

	private sealed class DelegateRegexHighlightingDefinition : RegexHighlightingDefinition
	{
		private readonly Func<IEnumerable<RegexHighlightingRule>> _rulesProvider;
		private readonly Func<IEnumerable<RegexHighlightingSpan>>? _spansProvider;

		public DelegateRegexHighlightingDefinition(
			string name,
			Func<IEnumerable<RegexHighlightingRule>> rulesProvider,
			Func<IEnumerable<RegexHighlightingSpan>>? spansProvider,
			RegexHighlightingDefinitionOptions? options)
			: base(name, options)
		{
			_rulesProvider = rulesProvider;
			_spansProvider = spansProvider;
		}

		protected override IEnumerable<RegexHighlightingRule> BuildRules() => _rulesProvider();

		protected override IEnumerable<RegexHighlightingSpan> BuildSpans()
			=> _spansProvider is null ? [] : _spansProvider();
	}

	/// <summary>
	/// Builds the neutral rule set from the definition's rules and spans, then converts it to the
	/// editor's rule set.
	/// </summary>
	/// <remarks>
	/// The neutral build validates every pattern and rejects an invalid one before any rule enters the set;
	/// see <see cref="RegexHighlightingRuleSet.Create"/>. A span without an end pattern enters the converted
	/// set with an expression that cannot match, so the span stays open to the document end, and a span with
	/// <see cref="RegexHighlightingSpan.Rules"/> enters the set with those rules as its nested rule set.
	/// </remarks>
	/// <returns>The built rule set.</returns>
	private HighlightingRuleSet BuildRuleSet()
		=> ConvertRuleSet(RegexHighlightingRuleSet.Create(Name, BuildRules(), BuildSpans()));

	/// <summary>
	/// Converts a validated neutral rule set to the editor's rule set.
	/// </summary>
	/// <param name="ruleSet">The neutral rule set to convert.</param>
	/// <returns>The converted editor rule set.</returns>
	private HighlightingRuleSet ConvertRuleSet(RegexHighlightingRuleSet ruleSet)
	{
		var highlightingRuleSet = new HighlightingRuleSet
		{
			Name = ruleSet.Name
		};

		foreach (RegexHighlightingRule rule in ruleSet.Rules)
			highlightingRuleSet.Rules.Add(CreateRule(rule));

		foreach (RegexHighlightingSpan span in ruleSet.Spans)
		{
			var highlightingSpan = new HighlightingSpan
			{
				StartExpression = span.Begin,

				// A span without an end pattern stays open to the document end: the editor's span model
				// requires an end expression and the highlight engine dereferences it while coloring, so
				// the null is mapped to a pattern that cannot match anything.
				EndExpression = span.End ?? s_neverMatchingEndExpression,
				SpanColor = span.SpanStyle.ToHighlightingColor(_fallbackColor),
				StartColor = span.BeginStyle?.ToHighlightingColor(_fallbackColor),
				EndColor = span.EndStyle?.ToHighlightingColor(_fallbackColor),

				// Without a dedicated begin or end style, the span color covers the delimiter matches as well.
				SpanColorIncludesStart = span.BeginStyle is null,
				SpanColorIncludesEnd = span.EndStyle is null
			};

			if (span.Rules is { Count: > 0 } nestedRules)
				highlightingSpan.RuleSet = CreateNestedRuleSet(nestedRules);

			highlightingRuleSet.Spans.Add(highlightingSpan);
		}

		return highlightingRuleSet;
	}

	/// <summary>
	/// Creates the editor's highlighting rule from a validated neutral rule.
	/// </summary>
	/// <param name="rule">The validated neutral rule to convert.</param>
	/// <returns>The converted editor highlighting rule.</returns>
	private HighlightingRule CreateRule(RegexHighlightingRule rule)
		=> new()
		{
			Regex = rule.Pattern,
			Color = rule.Style.ToHighlightingColor(_fallbackColor)
		};

	/// <summary>
	/// Creates the nested editor rule set for a span from its validated neutral rules.
	/// </summary>
	/// <param name="rules">The validated neutral rules of the span.</param>
	/// <returns>The converted nested editor rule set.</returns>
	private HighlightingRuleSet CreateNestedRuleSet(IReadOnlyList<RegexHighlightingRule> rules)
	{
		var ruleSet = new HighlightingRuleSet();

		foreach (RegexHighlightingRule rule in rules)
			ruleSet.Rules.Add(CreateRule(rule));

		return ruleSet;
	}

	/// <summary>
	/// The end expression mapped onto a span that has none: a pattern that cannot match any text, so the
	/// span stays open to the document end. The editor's highlight engine requires a non-null end
	/// expression, so the <see langword="null"/> from <see cref="RegexHighlightingSpan.End"/> never
	/// reaches it.
	/// </summary>
	private static readonly Regex s_neverMatchingEndExpression = new(@"(?!)");
}
