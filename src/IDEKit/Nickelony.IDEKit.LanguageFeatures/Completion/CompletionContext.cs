namespace Nickelony.IDEKit.LanguageFeatures.Completion;

/// <summary>Identifies what kind of editor action or host interaction triggered a completion request.</summary>
/// <remarks>
/// The vocabulary is open: a host whose editor distinguishes additional interactions defines custom triggers with <see cref="CreateCustom(string)"/>
/// and matches them by equality. Identifiers are normalized by trimming surrounding whitespace, and a blank identifier is rejected. Equality and hash
/// codes compare the normalized identifier ordinally, and the trigger character when there is one, so custom triggers need no registry.
/// </remarks>
public sealed class CompletionContext : IEquatable<CompletionContext>
{
	private const string TriggerCharacterIdentifier = "TriggerCharacter";

	/// <summary>
	/// The shared trigger for a completion request that a host invoked without a more specific cause, and the trigger used when a request
	/// does not specify one.
	/// </summary>
	public static CompletionContext Invoked { get; } = new("Invoked", triggerCharacter: null);

	private CompletionContext(string identifier, char? triggerCharacter)
	{
		Identifier = identifier;
		TriggerCharacter = triggerCharacter;
	}

	/// <summary>The stable serialized identifier for this trigger.</summary>
	public string Identifier { get; }

	/// <summary>The character the user typed to trigger completion, or <see langword="null"/> for a trigger that carries none.</summary>
	/// <remarks>
	/// Any character is a valid trigger character, a whitespace character included, because a server can advertise one.
	/// </remarks>
	public char? TriggerCharacter { get; }

	/// <summary>Creates the trigger for a completion request that typing a character caused.</summary>
	/// <param name="triggerCharacter">The character the user typed.</param>
	/// <returns>An immutable trigger; repeated calls with the same character produce equal (but distinct) instances.</returns>
	public static CompletionContext ForCharacter(char triggerCharacter) => new(TriggerCharacterIdentifier, triggerCharacter);

	/// <summary>Creates a custom completion trigger.</summary>
	/// <param name="identifier">
	/// The custom identifier. After trimming, it must not be blank and must not match a well-known trigger identifier case-insensitively.
	/// </param>
	/// <returns>An immutable custom trigger; repeated calls with the same identifier produce equal (but distinct) instances.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The identifier is blank or reserved for a well-known trigger.</exception>
	public static CompletionContext CreateCustom(string identifier)
	{
		string normalizedIdentifier = IdentifierVocabulary.Normalize(identifier);

		if (
			IdentifierVocabulary.IsReserved(normalizedIdentifier, Invoked.Identifier)
			|| IdentifierVocabulary.IsReserved(normalizedIdentifier, TriggerCharacterIdentifier)
		)
		{
			throw new ArgumentException($"The completion-trigger identifier '{normalizedIdentifier}' is reserved.", nameof(identifier));
		}

		return new CompletionContext(normalizedIdentifier, triggerCharacter: null);
	}

	/// <inheritdoc/>
	public bool Equals(CompletionContext? other) =>
		other is not null && StringComparer.Ordinal.Equals(Identifier, other.Identifier) && TriggerCharacter == other.TriggerCharacter;

	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is CompletionContext other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Identifier), TriggerCharacter);

	/// <inheritdoc/>
	public override string ToString() => Identifier;

	/// <summary>Compares completion triggers by their normalized ordinal identifiers.</summary>
	/// <param name="left">The left trigger, or <see langword="null"/>.</param>
	/// <param name="right">The right trigger, or <see langword="null"/>.</param>
	/// <returns>
	/// <see langword="true"/> when the triggers are equal, including two <see langword="null"/> values; otherwise, <see langword="false"/>.
	/// </returns>
	public static bool operator ==(CompletionContext? left, CompletionContext? right) =>
		ReferenceEquals(left, right) || (left is not null && left.Equals(right));

	/// <summary>Compares completion triggers by their normalized ordinal identifiers.</summary>
	/// <param name="left">The left trigger, or <see langword="null"/>.</param>
	/// <param name="right">The right trigger, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when the triggers differ; otherwise, <see langword="false"/>.</returns>
	public static bool operator !=(CompletionContext? left, CompletionContext? right) => !(left == right);
}
