namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>
/// Describes one auto-closing pair: the input that opens it and the closing text that is inserted,
/// skipped, or wrapped around a selection.
/// </summary>
/// <remarks>
/// <para>
/// The opening token is recognized when the input completes it at the caret: the entered text must
/// end the token, and the token's remaining characters must already end the text before the caret.
/// A single character typed in one input event is the common case; a multi-character token such as
/// <c>/*</c> resolves when its final character is typed. The closing text may span multiple
/// characters, for example an auto-comma suffix such as <c>},</c>. A pair whose opening or closing
/// text is empty is ignored, so an unconfigured pair is inert.
/// </para>
/// <para>
/// <see cref="Kind"/> selects the resolution rules: bracket-like pairs are left to normal text input in
/// front of their closing text, while quote-like pairs can skip existing closing text and can be
/// suppressed after a word character. The token-run rule (a token typed directly after the same token is
/// left to normal text input) applies to quote-like pairs and to any pair whose opening token and closing
/// text coincide, so a symmetric bracket-like pair such as <c>|</c> is not tripled either.
/// </para>
/// </remarks>
public sealed record TextAutoClosingPair
{
	private string _open;
	private string _close;

	/// <summary>
	/// Initializes an auto-closing pair.
	/// </summary>
	/// <param name="open">The opening token; it is recognized by completion at the caret and is normally a single character.</param>
	/// <param name="close">The closing text to insert, skip, or wrap a selection in.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="open"/> or <paramref name="close"/> is <see langword="null"/>.
	/// </exception>
	public TextAutoClosingPair(string open, string close)
	{
		ArgumentNullException.ThrowIfNull(open);
		ArgumentNullException.ThrowIfNull(close);

		_open = open;
		_close = close;
	}

	/// <summary>
	/// Gets the opening token of the pair; it is recognized by completion at the caret and is
	/// normally a single character.
	/// </summary>
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

	/// <summary>
	/// Gets the text inserted or skipped as the closing text.
	/// </summary>
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

	/// <summary>
	/// Gets the parentheses pair: <c>(</c> and <c>)</c>.
	/// </summary>
	public static TextAutoClosingPair Parentheses { get; } = new("(", ")");

	/// <summary>
	/// Gets the braces pair: <c>{</c> and <c>}</c>.
	/// </summary>
	public static TextAutoClosingPair Braces { get; } = new("{", "}");

	/// <summary>
	/// Gets the brackets pair: <c>[</c> and <c>]</c>.
	/// </summary>
	public static TextAutoClosingPair Brackets { get; } = new("[", "]");

	/// <summary>
	/// Gets the angle-brackets pair: <c>&lt;</c> and <c>&gt;</c>.
	/// </summary>
	/// <remarks>
	/// Not part of <see cref="TextAutoClosingOptions.Default"/> because <c>&lt;</c> is a comparison
	/// operator in many languages.
	/// </remarks>
	public static TextAutoClosingPair AngleBrackets { get; } = new("<", ">");

	/// <summary>
	/// Gets the double-quotes pair, including suppression after a word character.
	/// </summary>
	public static TextAutoClosingPair DoubleQuotes { get; } =
		new("\"", "\"") { Kind = TextAutoClosingPairKind.Quote, SuppressAfterWordCharacter = true };

	/// <summary>
	/// Gets the single-quotes pair, including suppression after a word character.
	/// </summary>
	public static TextAutoClosingPair SingleQuotes { get; } =
		new("'", "'") { Kind = TextAutoClosingPairKind.Quote, SuppressAfterWordCharacter = true };

	/// <summary>
	/// Gets the backticks pair.
	/// </summary>
	/// <remarks>
	/// Not part of <see cref="TextAutoClosingOptions.Default"/> because a backtick is a string
	/// delimiter only in some languages. Word-character suppression stays disabled for backticks.
	/// </remarks>
	public static TextAutoClosingPair Backticks { get; } = new("`", "`") { Kind = TextAutoClosingPairKind.Quote };

	/// <summary>
	/// Gets the resolution kind of the pair. Defaults to <see cref="TextAutoClosingPairKind.Bracket"/>.
	/// </summary>
	/// <remarks>
	/// Quote-like pairs (string delimiters) must be configured explicitly; the kind is not inferred from the
	/// shape of the tokens.
	/// </remarks>
	public TextAutoClosingPairKind Kind { get; init; }

	/// <summary>
	/// Gets a value indicating whether typing the opening token over a non-empty selection
	/// wraps the selection in the opening token and the closing text.
	/// </summary>
	/// <remarks>
	/// When disabled, typing the opening token over a selection replaces the selection through normal
	/// text input. Defaults to <see langword="true"/>.
	/// </remarks>
	public bool WrapSelection { get; init; } = true;

	/// <summary>
	/// Gets a value indicating whether auto-closing is suppressed after a word character. The word
	/// characters come from <see cref="TextAutoClosingOptions.IdentifierPolicy"/> and default to
	/// letters, digits, and underscores. Defaults to <see langword="false"/>.
	/// </summary>
	/// <remarks>
	/// The check applies to <see cref="TextAutoClosingPairKind.Quote"/> pairs: typing the token in
	/// front of an existing closing text skips it, typing it directly after the same token is left to
	/// normal text input so quote runs can be built, and this option can keep the pair from auto-closing
	/// after a word.
	/// </remarks>
	public bool SuppressAfterWordCharacter { get; init; }

	/// <summary>
	/// Gets a value indicating whether an existing unmatched closing text later on the caret's line
	/// suppresses the insert of a bracket-like pair. Defaults to <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// The scan reads raw text: it has no string or comment awareness, so a closing text inside a
	/// string or comment counts as existing content and suppresses the insert. Set to
	/// <see langword="false"/> when the host already tokenizes the text and wants the insert
	/// regardless; <see cref="TextAutoClosingOptions.AutoCloseUnconditionally"/> disables the scan
	/// together with the other lookahead gates. The property applies to bracket-like pairs only.
	/// </remarks>
	public bool CheckUnmatchedClosingText { get; init; } = true;
}
