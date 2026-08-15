using Nickelony.IDEKit.Core.Identifiers;
using System.Collections.ObjectModel;

namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>
/// Configures the auto-closing pairs and how they behave.
/// </summary>
/// <remarks>
/// <para>
/// The pairs are evaluated in the order they appear in <see cref="Pairs"/>, and the first matching
/// pair wins, so a pair's opening token should be unique across the list.
/// </para>
/// <para>
/// A pair whose opening or closing text is empty is ignored (see
/// <see cref="TextAutoClosingPair"/>), so a partial configuration never contributes an action or
/// rejects the input.
/// </para>
/// <para>
/// Record equality compares <see cref="Pairs"/> and <see cref="IdentifierPolicy"/> by reference, so two
/// content-equal options built from equal-but-distinct lists are not equal. Compare the configuration
/// explicitly when a cache needs to react to option changes.
/// </para>
/// </remarks>
public sealed record TextAutoClosingOptions
{
	/// <summary>
	/// The built-in auto-close-before preset for bracket-like pairs: <c>"'`;:.,=}])&gt;</c>.
	/// It is used when <see cref="AutoCloseBefore"/> is left at its default of <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// A bracket-like pair may auto-close before a quote character, whitespace or the end of the
	/// text, or one of the listed punctuation characters. The set is a conventional default chosen
	/// for common editing behavior; assign <see cref="AutoCloseBefore"/> to replace it.
	/// </remarks>
	public const string DefaultBracketAutoCloseBefore = "\"'`;:.,=}])>";

	/// <summary>
	/// The built-in auto-close-before preset for quote-like pairs: <c>;:.,=}])&gt;</c>.
	/// It is used when <see cref="AutoCloseBefore"/> is left at its default of <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// The preset excludes the quote characters, so typing a quote in front of a quote is either a
	/// skip or a normal insertion. Assign <see cref="AutoCloseBefore"/> to replace it.
	/// </remarks>
	public const string DefaultQuoteAutoCloseBefore = ";:.,=}])>";

	private static readonly ReadOnlyCollection<TextAutoClosingPair> s_defaultPairs = Array.AsReadOnly<TextAutoClosingPair>(
	[
		TextAutoClosingPair.Parentheses,
		TextAutoClosingPair.Braces,
		TextAutoClosingPair.Brackets,
		TextAutoClosingPair.DoubleQuotes,
		TextAutoClosingPair.SingleQuotes,
	]);

	private IReadOnlyList<TextAutoClosingPair> _pairs = s_defaultPairs;

	/// <summary>
	/// Gets the default options with the conventional pair set.
	/// </summary>
	/// <remarks>
	/// The default set is a conventional starting point: it contains parentheses, braces, brackets,
	/// double quotes, and single quotes (with word-character suppression for the quotes).
	/// Language-specific tokens such as angle brackets and backticks are available as
	/// <see cref="TextAutoClosingPair"/> presets and are opt-in; hosts assign <see cref="Pairs"/> for
	/// their own conventions.
	/// </remarks>
	public static TextAutoClosingOptions Default { get; } = new();

	/// <summary>
	/// Gets the auto-closing pairs in evaluation order.
	/// </summary>
	/// <remarks>
	/// The default set is the language-neutral one described by <see cref="Default"/>. The assigned
	/// list is copied before it is validated, so later caller-side mutation cannot change the
	/// configuration or the sequence the null-entry check saw.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned list is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The assigned list contains a <see langword="null"/> entry.</exception>
	public IReadOnlyList<TextAutoClosingPair> Pairs
	{
		get => _pairs;
		init
		{
			ArgumentNullException.ThrowIfNull(value);

			// Copy first so the null-entry check and the stored list see the same sequence.
			ReadOnlyCollection<TextAutoClosingPair> copy = Array.AsReadOnly([.. value]);

			if (copy.Any(static pair => pair is null))
				throw new ArgumentException("The pair list must not contain null entries.", nameof(value));

			_pairs = copy;
		}
	}

	/// <summary>
	/// Gets the characters that allow auto-closing when they follow the caret, or <see langword="null"/>
	/// to use the built-in preset of the pair's kind
	/// (<see cref="DefaultBracketAutoCloseBefore"/> or
	/// <see cref="DefaultQuoteAutoCloseBefore"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The character after the caret always allows auto-closing when it is the end of the text or
	/// whitespace; otherwise, it must be listed here, or belong to the kind's preset when this
	/// property is <see langword="null"/>. Assigning a set replaces the kind's preset instead of
	/// narrowing it, so an empty set admits no character besides whitespace and the end of the text.
	/// Wrapping a selection does not consult this property.
	/// </para>
	/// <para>
	/// The default is <see langword="null"/>: bracket-like pairs use
	/// <see cref="DefaultBracketAutoCloseBefore"/> and quote-like pairs use
	/// <see cref="DefaultQuoteAutoCloseBefore"/>. Language-specific sets can be supplied by
	/// the host.
	/// </para>
	/// </remarks>
	public string? AutoCloseBefore { get; init; }

	/// <summary>
	/// Gets a value indicating whether an opening token resolves its insert without consulting the
	/// text before the caret, bypassing all three before-caret gates listed in the remarks. Defaults
	/// to <see langword="false"/>.
	/// </summary>
	/// <remarks>
	/// When set, typing an opening token resolves the insert without consulting the character after
	/// the caret (<see cref="AutoCloseBefore"/> and the kind's preset), without the unmatched-closer
	/// scan (see <see cref="TextAutoClosingPair.CheckUnmatchedClosingText"/>), and without the
	/// word-character suppression of quote-like pairs
	/// (<see cref="TextAutoClosingPair.SuppressAfterWordCharacter"/>). The doubled-token rule still
	/// applies, and escaping still protects quote-like tokens.
	/// </remarks>
	public bool AutoCloseUnconditionally { get; init; }

	/// <summary>
	/// Gets the escape character that marks the token after it as part of an escape sequence, or
	/// <see langword="null"/> to disable escape handling. Defaults to the backslash character.
	/// </summary>
	/// <remarks>
	/// A quote-like token preceded by an odd number of escape characters is neither skipped nor treated as
	/// an existing closing text, and a quote-like pair whose opening token or closing text is escaped is
	/// not deleted as a pair, so languages without backslash escapes can disable the check.
	/// </remarks>
	public char? EscapeCharacter { get; init; } = '\\';

	private IdentifierCharacterPolicy _identifierPolicy = IdentifierCharacterPolicy.Default;

	/// <summary>
	/// Gets the character policy that defines word characters for
	/// <see cref="TextAutoClosingPair.SuppressAfterWordCharacter"/>. Defaults to
	/// <see cref="IdentifierCharacterPolicy.Default"/>, which is a C-like approximation; languages with a
	/// different word rule can supply their own policy.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned policy is <see langword="null"/>.</exception>
	public IdentifierCharacterPolicy IdentifierPolicy
	{
		get => _identifierPolicy;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_identifierPolicy = value;
		}
	}

	/// <summary>
	/// Gets the provenance that must hold before typing existing closing text skips it.
	/// Defaults to <see cref="TextAutoClosingProvenance.Auto"/>.
	/// </summary>
	/// <remarks>
	/// With <see cref="TextAutoClosingProvenance.Auto"/>, closing text is skipped only when it is the text
	/// an editor-side service inserted earlier and tracked per document; content that was loaded or typed
	/// through any other path is not skipped. The tracking lives on the service instance, so a caller that
	/// resolves actions without applying them through that tracked path gets no skips in this mode and
	/// should select <see cref="TextAutoClosingProvenance.Always"/> or
	/// <see cref="TextAutoClosingProvenance.Never"/>.
	/// </remarks>
	public TextAutoClosingProvenance ClosingTextSkipProvenance { get; init; } = TextAutoClosingProvenance.Auto;

	/// <summary>
	/// Gets the provenance that must hold before a host pair deletion (for example a plain Backspace)
	/// removes an auto-closing pair. Defaults to <see cref="TextAutoClosingProvenance.Auto"/>.
	/// </summary>
	/// <remarks>
	/// With <see cref="TextAutoClosingProvenance.Auto"/>, the pair is removed only when its closing text
	/// is the text an editor-side service inserted earlier and tracked per document; otherwise, the host
	/// deletes a single character, matching the convention of mainstream desktop editors. Deleting a
	/// pair whose closing text was not auto-inserted would remove text the user did not ask to remove.
	/// </remarks>
	public TextAutoClosingProvenance PairDeletionProvenance { get; init; } = TextAutoClosingProvenance.Auto;
}
