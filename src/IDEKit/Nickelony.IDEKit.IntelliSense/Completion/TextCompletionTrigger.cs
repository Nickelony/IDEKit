namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Identifies what kind of editor action or host interaction triggered a completion request.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Invoked"/> is the shared, protocol-neutral trigger used when a host requests
/// completion without a more specific cause, so a request that does not name a trigger carries it.
/// The vocabulary is open: hosts whose editors distinguish additional interactions (for example an
/// empty-line or a contextual trigger) define custom triggers with
/// <see cref="CreateCustom(string)"/> and match them by equality.
/// </para>
/// <para>
/// Identifiers are normalized by trimming surrounding whitespace, and a blank identifier is
/// rejected. The well-known identifier is reserved. Equality and hash codes compare the normalized
/// identifier ordinally, so custom triggers need no registry.
/// </para>
/// </remarks>
public sealed class TextCompletionTrigger : IEquatable<TextCompletionTrigger>
{
	/// <summary>
	/// The shared trigger for a completion request that a host invoked without a more specific
	/// cause, and the trigger used when a request does not specify one.
	/// </summary>
	public static TextCompletionTrigger Invoked { get; } = new("Invoked");

	private TextCompletionTrigger(string identifier)
	{
		Identifier = identifier;
	}

	/// <summary>
	/// Gets the stable serialized identifier for this trigger.
	/// </summary>
	public string Identifier { get; }

	/// <summary>
	/// Creates a custom completion trigger.
	/// </summary>
	/// <param name="identifier">
	/// The custom identifier. After trimming, it must not be blank and must not match the well-known
	/// trigger identifier case-insensitively.
	/// </param>
	/// <returns>
	/// An immutable custom trigger; repeated calls with the same identifier produce equal (but
	/// distinct) instances.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="identifier"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The identifier is blank or reserved for the well-known trigger.</exception>
	public static TextCompletionTrigger CreateCustom(string identifier)
	{
		string normalizedIdentifier = IdentifierVocabulary.Normalize(identifier);

		if (IdentifierVocabulary.IsReserved(normalizedIdentifier, Invoked.Identifier))
		{
			throw new ArgumentException(
				$"The completion-trigger identifier '{normalizedIdentifier}' is reserved.",
				nameof(identifier));
		}

		return new TextCompletionTrigger(normalizedIdentifier);
	}

	/// <inheritdoc/>
	public bool Equals(TextCompletionTrigger? other)
		=> other is not null && StringComparer.Ordinal.Equals(Identifier, other.Identifier);

	/// <inheritdoc/>
	public override bool Equals(object? obj)
		=> obj is TextCompletionTrigger other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode()
		=> StringComparer.Ordinal.GetHashCode(Identifier);

	/// <inheritdoc/>
	public override string ToString() => Identifier;

	/// <summary>
	/// Compares completion triggers by their normalized ordinal identifiers.
	/// </summary>
	/// <param name="left">The left trigger, or <see langword="null"/>.</param>
	/// <param name="right">The right trigger, or <see langword="null"/>.</param>
	/// <returns>
	/// <see langword="true"/> when the triggers are equal, including two <see langword="null"/>
	/// values; otherwise, <see langword="false"/>.
	/// </returns>
	public static bool operator ==(TextCompletionTrigger? left, TextCompletionTrigger? right)
		=> ReferenceEquals(left, right) || left is not null && left.Equals(right);

	/// <summary>
	/// Compares completion triggers by their normalized ordinal identifiers.
	/// </summary>
	/// <param name="left">The left trigger, or <see langword="null"/>.</param>
	/// <param name="right">The right trigger, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when the triggers differ; otherwise, <see langword="false"/>.</returns>
	public static bool operator !=(TextCompletionTrigger? left, TextCompletionTrigger? right)
		=> !(left == right);
}
