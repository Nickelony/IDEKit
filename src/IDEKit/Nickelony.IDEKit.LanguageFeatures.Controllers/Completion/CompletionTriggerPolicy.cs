namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>Stores the rule that decides which typed characters open completion, and in which scopes.</summary>
/// <remarks>
/// <para>
/// The completion controller applies the rule when a character is typed. A typed character that starts a word schedules a request
/// in the <see cref="WordScopes"/>, and a provider's trigger character requests completion at once, in every scope or only in the word scopes.
/// A scope of <see cref="CompletionScopes.None"/> opens nothing. A host that starts every request itself uses <see cref="Manual"/>.
/// Each option validates itself when assigned.
/// </para>
/// <para>
/// Record equality compares <see cref="WordStart"/> by reference.
/// </para>
/// </remarks>
public sealed record CompletionTriggerPolicy
{
	private const CompletionScopes AllScopes = CompletionScopes.Code | CompletionScopes.Comment | CompletionScopes.StringLiteral;

	// Letters and underscores start a word, so a typed digit, such as one of a number, schedules no request.
	private static readonly Func<char, bool> s_wordStart = static character => char.IsLetter(character) || character == '_';

	private readonly CompletionScopes _wordScopes = CompletionScopes.Code;
	private readonly Func<char, bool> _wordStart = s_wordStart;

	/// <summary>
	/// The policy that schedules a request for a word character typed in code, and requests at once for a trigger character anywhere.
	/// </summary>
	public static CompletionTriggerPolicy Default { get; } = new();

	/// <summary>The policy under which no typed character opens completion, for a host that calls the controller's requests itself.</summary>
	public static CompletionTriggerPolicy Manual { get; } = new() { WordScopes = CompletionScopes.None, TriggerCharactersInEveryScope = false };

	/// <summary>The scopes in which a typed word character schedules a request; defaults to <see cref="CompletionScopes.Code"/>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value holds a flag that <see cref="CompletionScopes"/> does not define.</exception>
	public CompletionScopes WordScopes
	{
		get => _wordScopes;
		init =>
			_wordScopes =
				(value & ~AllScopes) == 0
					? value
					: throw new ArgumentOutOfRangeException(
						nameof(WordScopes),
						value,
						"The value holds a flag that CompletionScopes does not define."
					);
	}

	/// <summary>
	/// Whether a trigger character requests completion in every scope, or only in the <see cref="WordScopes"/>; defaults to <see langword="true"/>,
	/// because a server can complete inside a string, such as a module path after a dot.
	/// </summary>
	/// <remarks>
	/// Every scope means every scope but <see cref="CompletionScopes.None"/>, which a host answers where completion is off.
	/// </remarks>
	public bool TriggerCharactersInEveryScope { get; init; } = true;

	/// <summary>Whether a typed character starts a word and so schedules a request; defaults to a letter or an underscore.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Func<char, bool> WordStart
	{
		get => _wordStart;
		init => _wordStart = value ?? throw new ArgumentNullException(nameof(WordStart));
	}
}
