namespace Nickelony.KeyBindings;

/// <summary>
/// The lookup key of one declared binding: the context token a chord is declared for, together with
/// the chord itself.
/// </summary>
/// <remarks>
/// <para>
/// The always-active context is represented by a <see langword="null"/> token, exactly as the public
/// surface represents it, so the key needs no separate sentinel. A non-null token is used verbatim: an
/// empty or whitespace-only token is not the always-active context, it is an undeclared token that
/// never matches a declaration.
/// </para>
/// <para>
/// Two keys are co-active when their tokens are equal or either is the always-active context. Only
/// co-active keys can conflict, which is what keeps a context condition decidable without an
/// expression language.
/// </para>
/// </remarks>
/// <param name="Context">The context token, or <see langword="null"/> for the always-active context.</param>
/// <param name="Chord">The chord the context is declared for.</param>
internal readonly record struct ContextChord(string? Context, KeyChord Chord)
{
	/// <summary>The internal representation of the always-active context: a <see langword="null"/> token.</summary>
	internal const string? Always = null;

	/// <summary>
	/// Creates the key that represents a public context token.
	/// </summary>
	/// <param name="context">The public context token, or <see langword="null"/> for the always-active context.</param>
	/// <param name="chord">The chord the context is declared for.</param>
	/// <returns>The lookup key for the token.</returns>
	internal static ContextChord Create(string? context, KeyChord chord)
		=> new(context ?? Always, chord);

	/// <summary>
	/// Gets a value indicating whether this key can be active at the same time as another key.
	/// </summary>
	/// <param name="other">The other binding key.</param>
	/// <returns>
	/// <see langword="true"/> when the two contexts can be active at the same time, that is when they
	/// are the same token or either is the always-active context.
	/// </returns>
	internal bool IsCoActiveWith(ContextChord other)
		=> Context is null ||
			other.Context is null ||
			string.Equals(Context, other.Context, StringComparison.Ordinal);
}
