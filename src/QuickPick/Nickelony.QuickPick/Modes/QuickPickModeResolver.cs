namespace Nickelony.QuickPick;

/// <summary>Resolves the mode of a quick pick from the typed text: the list of modes, the longest registered prefix, or the default mode.</summary>
/// <remarks>
/// The resolver reads the prefix of each mode once, when it is created, and holds the list of modes that the reserved prefix
/// <c>?</c> shows while the help mode is on. A text that starts with the reserved prefix selects the list of modes; otherwise, the longest registered
/// prefix that the text starts with wins, so a mode whose prefix extends another's still answers its own text, and a text that starts with no prefix
/// belongs to the mode with an empty prefix, or to the list of modes when there is none. With the help mode off, <c>?</c> is an ordinary character,
/// and a mode with an empty prefix must exist.
/// </remarks>
internal sealed class QuickPickModeResolver
{
	private readonly List<(string Prefix, IQuickPickProvider Provider)> _prefixed = [];
	private readonly Dictionary<IQuickPickProvider, string> _prefixes = new(ReferenceEqualityComparer.Instance);
	private readonly IQuickPickProvider? _defaultProvider;

	/// <param name="modes">The registered modes.</param>
	/// <param name="options">
	/// The options of the controller, whose help mode settings decide whether the reserved prefix lists the modes and what the list says.
	/// </param>
	/// <exception cref="ArgumentException">
	/// <paramref name="modes"/> holds a <see langword="null"/> entry, a mode without a prefix, a mode with the reserved prefix while the help
	/// mode is on, two modes with the same prefix, or one provider in two modes; or the help mode is off and no mode has the empty prefix.
	/// </exception>
	internal QuickPickModeResolver(IReadOnlyList<QuickPickMode> modes, QuickPickControllerOptions options)
	{
		bool helpMode = options.EnableHelpMode;
		HashSet<string> seen = new(StringComparer.Ordinal);
		List<(string Prefix, IQuickPickProvider Provider)> registered = [];

		foreach (QuickPickMode mode in modes)
		{
			if (mode is null)
				throw new ArgumentException("The mode list must not contain null entries.", nameof(modes));

			string prefix =
				mode.Prefix
				?? mode.Provider.Prefix
				?? throw new ArgumentException("A mode must have a prefix; an empty string selects the default mode.", nameof(modes));

			if (helpMode && prefix == QuickPickHelpProvider.ReservedPrefix)
				throw new ArgumentException($"The prefix '{prefix}' is reserved for the list of modes.", nameof(modes));

			if (!seen.Add(prefix))
				throw new ArgumentException($"Two modes share the prefix '{prefix}'.", nameof(modes));

			if (_prefixes.ContainsKey(mode.Provider))
				throw new ArgumentException("A provider can be offered in one mode only.", nameof(modes));

			registered.Add((prefix, mode.Provider));
			_prefixes[mode.Provider] = prefix;

			if (prefix.Length == 0)
				_defaultProvider = mode.Provider;
			else
				_prefixed.Add((prefix, mode.Provider));
		}

		if (!helpMode && _defaultProvider is null)
		{
			throw new ArgumentException(
				"Without the help mode, one mode needs the empty prefix, because a text that starts with no other prefix must pick a mode.",
				nameof(modes)
			);
		}

		// The longest prefix is tried first.
		_prefixed.Sort(static (left, right) => right.Prefix.Length.CompareTo(left.Prefix.Length));

		if (helpMode)
		{
			Help = new QuickPickHelpProvider(registered, options);
			_prefixes[Help] = QuickPickHelpProvider.ReservedPrefix;
		}
	}

	/// <summary>The provider of the list of modes, or <see langword="null"/> while the help mode is off.</summary>
	internal IQuickPickProvider? Help { get; }

	/// <summary>The mode of an empty text: the provider with an empty prefix, or the list of modes.</summary>
	internal IQuickPickProvider Default => _defaultProvider ?? Help!;

	/// <summary>Resolves the mode of a text.</summary>
	/// <param name="text">The text of the input, including the prefix.</param>
	/// <returns>The mode, and the text that follows its prefix, which is the query.</returns>
	internal (IQuickPickProvider Mode, string Query) Resolve(string text)
	{
		if (Help is not null && text.StartsWith(QuickPickHelpProvider.ReservedPrefix, StringComparison.Ordinal))
			return (Help, text[QuickPickHelpProvider.ReservedPrefix.Length..]);

		foreach ((string prefix, IQuickPickProvider provider) in _prefixed)
		{
			if (text.StartsWith(prefix, StringComparison.Ordinal))
				return (provider, text[prefix.Length..]);
		}

		return (Default, text);
	}

	/// <summary>Gives the prefix a mode was registered with, which the recent items of the mode are kept under.</summary>
	/// <param name="provider">A registered provider or <see cref="Help"/>.</param>
	internal string PrefixOf(IQuickPickProvider provider) => _prefixes[provider];
}
