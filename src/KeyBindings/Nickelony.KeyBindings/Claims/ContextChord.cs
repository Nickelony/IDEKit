namespace Nickelony.KeyBindings;

/// <summary>The lookup key of one declared binding: the context token a chord is declared for, together with the chord itself.</summary>
/// <param name="Context">The context token, or <see langword="null"/> for the always-active context.</param>
/// <param name="Chord">The chord the context is declared for.</param>
/// <remarks>
/// The always-active context is the <see langword="null"/> token, exactly as the public
/// surface represents it; a non-<see langword="null"/> token is used verbatim, so an empty or whitespace-only token is an undeclared token
/// that never matches a declaration. Two keys are co-active when their tokens are equal or either is the always-active context, and only co-active
/// keys can conflict.
/// </remarks>
internal readonly record struct ContextChord(string? Context, KeyChord Chord)
{
	/// <summary>The always-active context token.</summary>
	internal const string? Always = null;

	/// <summary>Indicates whether this key can be active at the same time as another key.</summary>
	/// <param name="other">The other binding key.</param>
	/// <returns>
	/// <see langword="true"/> when the two contexts can be active at the same time, that is when they are the same token
	/// or either is the always-active context.
	/// </returns>
	internal bool IsCoActiveWith(ContextChord other) =>
		Context is null || other.Context is null || string.Equals(Context, other.Context, StringComparison.Ordinal);
}
