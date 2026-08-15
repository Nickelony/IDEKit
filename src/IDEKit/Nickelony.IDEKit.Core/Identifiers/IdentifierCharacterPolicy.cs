namespace Nickelony.IDEKit.Core.Identifiers;

/// <summary>
/// Character-level rules that define which characters belong to an identifier or token.
/// </summary>
/// <remarks>
/// The policy distinguishes characters that may begin a token from characters that may appear
/// within one. Both rules are caller-supplied predicates; the policy itself only caches them and
/// exposes the evaluation order the boundary walkers rely on.
/// </remarks>
public sealed class IdentifierCharacterPolicy
{
	private readonly Func<char, bool> _isPartCharacter;
	private readonly Func<char, bool> _isStartCharacter;
	private Func<char, bool>? _partCharacterPredicate;

	/// <summary>
	/// Gets the policy that accepts letters, digits, and underscores as token characters. This is an
	/// approximation, not a language identifier policy; see the remarks before relying on it.
	/// </summary>
	/// <remarks>
	/// This is a C-like approximation: <see cref="char.IsLetterOrDigit(char)"/> works on UTF-16 code
	/// units, so an astral-plane identifier splits at the surrogate pair, and the default
	/// start-character rule accepts digits. It is also narrower than most language definitions:
	/// combining marks (for example the acute accent of an NFD identifier) and connector punctuation
	/// other than <c>_</c> are rejected. Use <see cref="Create"/> with explicit predicates for other
	/// language rules.
	/// </remarks>
	public static IdentifierCharacterPolicy Default { get; } = Create(static character => char.IsLetterOrDigit(character) || character == '_');

	private IdentifierCharacterPolicy(Func<char, bool> isPartCharacter, Func<char, bool>? isStartCharacter)
	{
		_isPartCharacter = isPartCharacter;
		_isStartCharacter = isStartCharacter ?? isPartCharacter;
	}

	/// <summary>
	/// Creates a policy with the supplied character rules.
	/// </summary>
	/// <param name="isPartCharacter">Determines whether a character may appear within a token.</param>
	/// <param name="isStartCharacter">
	/// Determines whether a character may begin a token. Defaults to <paramref name="isPartCharacter"/>.
	/// </param>
	/// <returns>The created policy.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="isPartCharacter"/> is <see langword="null"/>.</exception>
	public static IdentifierCharacterPolicy Create(
		Func<char, bool> isPartCharacter,
		Func<char, bool>? isStartCharacter = null)
	{
		ArgumentNullException.ThrowIfNull(isPartCharacter);

		return new IdentifierCharacterPolicy(isPartCharacter, isStartCharacter);
	}

	/// <summary>
	/// Determines whether the character may begin a token.
	/// </summary>
	/// <param name="character">The character to test.</param>
	/// <returns><see langword="true"/> when the character may begin a token; otherwise, <see langword="false"/>.</returns>
	public bool IsStartCharacter(char character)
		=> _isStartCharacter(character);

	/// <summary>
	/// Determines whether the character may appear within a token.
	/// </summary>
	/// <param name="character">The character to test.</param>
	/// <returns><see langword="true"/> when the character may appear within a token; otherwise, <see langword="false"/>.</returns>
	public bool IsPartCharacter(char character)
		=> _isPartCharacter(character);

	/// <summary>
	/// Gets a cached delegate for <see cref="IsPartCharacter"/> so shared boundary walks do not
	/// allocate a delegate per call.
	/// </summary>
	/// <remarks>
	/// The cache is intentionally unsynchronized: concurrent first calls may each create an
	/// equivalent delegate, which is harmless, and a lock on this hot path is not warranted.
	/// </remarks>
	internal Func<char, bool> PartCharacterPredicate => _partCharacterPredicate ??= IsPartCharacter;
}
