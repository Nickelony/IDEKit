using System.Collections.ObjectModel;
using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>Configures the auto-closing pairs and how they behave.</summary>
/// <remarks>
/// The pairs are evaluated in order and the first resolving pair wins, so each opening token should be unique. Record equality compares
/// <see cref="Pairs"/> and <see cref="IdentifierPolicy"/> by reference, and assigning <see cref="Pairs"/> stores a copy, so two content-equal options
/// are equal only when both keep the default pairs and share one policy instance; compare explicitly when a cache reacts to option changes.
/// </remarks>
public sealed record AutoClosingOptions
{
	/// <summary>
	/// The built-in auto-close-before preset for bracket-like pairs, used when <see cref="AutoCloseBefore"/> is <see langword="null"/>.
	/// </summary>
	public const string DefaultBracketAutoCloseBefore = "\"'`;:.,=}])>";

	/// <summary>
	/// The built-in auto-close-before preset for quote-like pairs, used when <see cref="AutoCloseBefore"/> is <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// Excludes the quote characters, so a quote typed in front of a quote is either a skip or a normal insertion.
	/// </remarks>
	public const string DefaultQuoteAutoCloseBefore = ";:.,=}])>";

	private static readonly ReadOnlyCollection<AutoClosingPair> s_defaultPairs = Array.AsReadOnly<AutoClosingPair>([
		AutoClosingPair.Parentheses,
		AutoClosingPair.Braces,
		AutoClosingPair.Brackets,
		AutoClosingPair.DoubleQuotes,
		AutoClosingPair.SingleQuotes,
	]);

	private IReadOnlyList<AutoClosingPair> _pairs = s_defaultPairs;

	public static AutoClosingOptions Default { get; } = new();

	/// <summary>The auto-closing pairs in evaluation order.</summary>
	/// <exception cref="ArgumentNullException">The assigned list is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The assigned list contains a <see langword="null"/> entry.</exception>
	/// <remarks>
	/// The assigned list is copied, so later caller-side mutation cannot change the configuration.
	/// </remarks>
	public IReadOnlyList<AutoClosingPair> Pairs
	{
		get => _pairs;
		init
		{
			ArgumentNullException.ThrowIfNull(value);

			// Copy first so the null-entry check and the stored list see the same sequence.
			ReadOnlyCollection<AutoClosingPair> copy = Array.AsReadOnly([.. value]);

			if (copy.Any(static pair => pair is null))
				throw new ArgumentException("The pair list must not contain null entries.", nameof(value));

			_pairs = copy;
		}
	}

	/// <summary>
	/// The characters that allow auto-closing when they follow the caret, or <see langword="null"/> to use the built-in preset of the pair's kind.
	/// </summary>
	/// <remarks>
	/// The character after the caret always allows auto-closing when it is the end of the text or whitespace; otherwise, it must be listed
	/// here (or belong to the kind's preset when this is <see langword="null"/>). Assigning a set replaces the kind's preset
	/// rather than narrowing it, so an empty set admits only whitespace and the end of the text; wrapping a selection does not consult this property.
	/// </remarks>
	public string? AutoCloseBefore { get; init; }

	/// <summary>
	/// Whether an opening token resolves its insert without the gates that inspect the surrounding text; defaults to <see langword="false"/>.
	/// </summary>
	/// <remarks>
	/// When set, the insert resolves without the character-after-the-caret, unmatched-closer, and word-character gates;
	/// the doubled-token rule still applies, and escape protection still applies to skips.
	/// </remarks>
	public bool AutoCloseUnconditionally { get; init; }

	/// <summary>
	/// The escape character that marks the following token as part of an escape sequence, or <see langword="null"/> to disable escape handling;
	/// defaults to the backslash character.
	/// </summary>
	/// <remarks>
	/// A quote-like token preceded by an odd number of escape characters is neither skipped nor treated as existing closing text, and an escaped
	/// quote-like pair is not deleted as a pair.
	/// </remarks>
	public char? EscapeCharacter { get; init; } = '\\';

	private IdentifierCharacterPolicy _identifierPolicy = IdentifierCharacterPolicy.Default;

	/// <summary>
	/// The character policy that defines word characters for <see cref="AutoClosingPair.SuppressAfterWordCharacter"/>;
	/// defaults to <see cref="IdentifierCharacterPolicy.Default"/>, a C-like approximation.
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
	/// The tracking mode that must hold before typing existing closing text skips it; defaults to <see cref="AutoClosingTrackingMode.TrackedOnly"/>.
	/// </summary>
	/// <remarks>
	/// With <see cref="AutoClosingTrackingMode.TrackedOnly"/>, text is skipped only when an editor-side service inserted and tracked it per document.
	/// The tracking lives on the service instance, so a caller that resolves without applying through that path gets no skips in this mode
	/// and should select <see cref="AutoClosingTrackingMode.Always"/> or <see cref="AutoClosingTrackingMode.Never"/>.
	/// </remarks>
	public AutoClosingTrackingMode ClosingTextSkipTracking { get; init; } = AutoClosingTrackingMode.TrackedOnly;

	/// <summary>
	/// The tracking mode that must hold before a host pair deletion (for example Backspace) removes an auto-closing pair;
	/// defaults to <see cref="AutoClosingTrackingMode.TrackedOnly"/>.
	/// </summary>
	/// <remarks>
	/// With <see cref="AutoClosingTrackingMode.TrackedOnly"/>, the pair is removed only when its closing text was inserted and tracked per document
	/// by an editor-side service; otherwise, the host deletes a single character.
	/// </remarks>
	public AutoClosingTrackingMode PairDeletionTracking { get; init; } = AutoClosingTrackingMode.TrackedOnly;
}
