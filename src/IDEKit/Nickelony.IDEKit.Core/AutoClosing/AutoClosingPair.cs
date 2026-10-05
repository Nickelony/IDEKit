namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>Describes one auto-closing pair.</summary>
/// <remarks>
/// An opening token is recognized by completion at the caret: the input must end the token and the token's remaining characters must already end
/// the text before the caret, so a multi-character token resolves when its final character is typed. The closing text may span multiple characters,
/// and a pair with an empty opening or closing text is ignored.
/// </remarks>
public sealed record AutoClosingPair
{
	private string _open;
	private string _close;

	/// <exception cref="ArgumentNullException"><paramref name="open"/> or <paramref name="close"/> is <see langword="null"/>.</exception>
	public AutoClosingPair(string open, string close)
	{
		ArgumentNullException.ThrowIfNull(open);
		ArgumentNullException.ThrowIfNull(close);

		_open = open;
		_close = close;
	}

	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Open
	{
		get => _open;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_open = value;
		}
	}

	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Close
	{
		get => _close;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_close = value;
		}
	}

	public static AutoClosingPair Parentheses { get; } = new("(", ")");

	public static AutoClosingPair Braces { get; } = new("{", "}");

	public static AutoClosingPair Brackets { get; } = new("[", "]");

	/// <summary>Not part of <see cref="AutoClosingOptions.Default"/>, because <c>&lt;</c> is a comparison operator in many languages.</summary>
	public static AutoClosingPair AngleBrackets { get; } = new("<", ">");

	/// <summary>The double-quotes pair, with word-character suppression enabled.</summary>
	public static AutoClosingPair DoubleQuotes { get; } = new("\"", "\"") { Kind = AutoClosingPairKind.Quote, SuppressAfterWordCharacter = true };

	/// <summary>The single-quotes pair, with word-character suppression enabled.</summary>
	public static AutoClosingPair SingleQuotes { get; } = new("'", "'") { Kind = AutoClosingPairKind.Quote, SuppressAfterWordCharacter = true };

	/// <summary>
	/// Not part of <see cref="AutoClosingOptions.Default"/>, because a backtick is a string delimiter only in some languages;
	/// word-character suppression stays disabled.
	/// </summary>
	public static AutoClosingPair Backticks { get; } = new("`", "`") { Kind = AutoClosingPairKind.Quote };

	/// <summary>Defaults to <see cref="AutoClosingPairKind.Bracket"/>.</summary>
	public AutoClosingPairKind Kind { get; init; }

	/// <summary>Whether typing the opening token over a non-empty selection wraps the selection rather than replacing it.</summary>
	/// <remarks>
	/// When <see langword="false"/>, typing the opening token over a selection replaces it through normal text input.
	/// Defaults to <see langword="true"/>.
	/// </remarks>
	public bool WrapSelection { get; init; } = true;

	/// <summary>Whether auto-closing is suppressed after a word character; defaults to <see langword="false"/>.</summary>
	/// <remarks>
	/// The check applies to <see cref="AutoClosingPairKind.Quote"/> pairs; word characters come
	/// from <see cref="AutoClosingOptions.IdentifierPolicy"/>.
	/// </remarks>
	public bool SuppressAfterWordCharacter { get; init; }

	/// <summary>
	/// Whether an unmatched closing text later on the caret's line suppresses the insert of a bracket-like pair; defaults to <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// The scan reads raw text with no string or comment awareness, so a closing text inside a string or comment still suppresses the insert;
	/// <see cref="AutoClosingOptions.AutoCloseUnconditionally"/> disables the scan with the other lookahead gates. Applies to bracket-like
	/// pairs only.
	/// </remarks>
	public bool CheckUnmatchedClosingText { get; init; } = true;
}
