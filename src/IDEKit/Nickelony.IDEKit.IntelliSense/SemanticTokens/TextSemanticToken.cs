using Nickelony.IDEKit.Core.Text;
using System.Collections.ObjectModel;

namespace Nickelony.IDEKit.IntelliSense.SemanticTokens;

/// <summary>
/// Represents a style-neutral semantic token identifying a range of document text.
/// </summary>
/// <remarks>
/// <para>
/// The token carries only semantics: a range, a type, and modifiers; never a color or style.
/// Hosts map <see cref="Type"/> and <see cref="Modifiers"/> onto their own theme model, so the
/// token can cross provider and editor boundaries without UI coupling. Offsets are zero-based
/// UTF-16 positions.
/// </para>
/// <para>
/// Instances use structural value equality: two tokens are equal when their ranges, types (ordinal),
/// and modifier sequences (ordinal, in order) are equal. Hosts can therefore compare token
/// sequences with the default equality comparer instead of projecting the components themselves.
/// </para>
/// </remarks>
public sealed class TextSemanticToken : IEquatable<TextSemanticToken>
{
	private readonly ReadOnlyCollection<string> _modifiers;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextSemanticToken"/> class.
	/// </summary>
	/// <param name="range">
	/// The zero-based range the token spans. The range is stored after <see cref="TextRange"/>
	/// validation; its containment in the document is not checked, so a producer is responsible for
	/// supplying offsets within the document.
	/// </param>
	/// <param name="type">
	/// The semantic token type (for example <see cref="TextSemanticTokenTypes.Variable"/>); producers
	/// supply a known or custom type name. The name is trimmed, and a blank value is rejected because
	/// a token without a category cannot be mapped by a host.
	/// </param>
	/// <param name="modifiers">
	/// The optional semantic token modifiers. Names are trimmed and duplicates are removed (the first
	/// occurrence wins); the constructor takes an owned snapshot.
	/// </param>
	/// <exception cref="ArgumentException">
	/// <paramref name="type"/> or a <paramref name="modifiers"/> element is blank, whitespace-only,
	/// or <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
	public TextSemanticToken(TextRange range, string type, IReadOnlyList<string>? modifiers = null)
	{
		ArgumentNullException.ThrowIfNull(type);

		if (string.IsNullOrWhiteSpace(type))
			throw new ArgumentException("The token type must not be blank.", nameof(type));

		Range = range;
		Type = type.Trim();
		_modifiers = CaptureModifiers(modifiers);
	}

	/// <summary>
	/// Validates, normalizes, and deduplicates the supplied modifier names into an owned snapshot.
	/// </summary>
	/// <param name="modifiers">The supplied modifier names, or <see langword="null"/> for none.</param>
	/// <returns>The captured modifier snapshot; the shared empty snapshot when none were supplied.</returns>
	/// <exception cref="ArgumentException">
	/// <paramref name="modifiers"/> contains a <see langword="null"/> or blank element.
	/// </exception>
	private static ReadOnlyCollection<string> CaptureModifiers(IReadOnlyList<string>? modifiers)
	{
		if (modifiers is not { Count: > 0 })
			return PayloadCollections.Empty<string>();

		var captured = new List<string>(modifiers.Count);

		for (int i = 0; i < modifiers.Count; i++)
		{
			string? modifier = modifiers[i];

			if (modifier is null)
				throw new ArgumentException($"The modifier at index {i} is null.", nameof(modifiers));

			string normalizedModifier = modifier.Trim();

			if (normalizedModifier.Length == 0)
				throw new ArgumentException($"The modifier at index {i} is blank.", nameof(modifiers));

			if (!captured.Contains(normalizedModifier))
				captured.Add(normalizedModifier);
		}

		return new ReadOnlyCollection<string>(captured);
	}

	/// <summary>
	/// Gets the zero-based range the token spans.
	/// </summary>
	public TextRange Range { get; }

	/// <summary>
	/// Gets the semantic token type.
	/// </summary>
	public string Type { get; }

	/// <summary>
	/// Gets the owned snapshot of semantic token modifiers. Names are trimmed and duplicates are
	/// removed at capture.
	/// </summary>
	public IReadOnlyList<string> Modifiers => _modifiers;

	/// <summary>
	/// Determines whether the token has the specified modifier.
	/// </summary>
	/// <remarks>
	/// Modifier names are compared using ordinal, case-sensitive comparison; stored names are trimmed
	/// at capture and surrounding whitespace in the probe is ignored. A <see langword="null"/>, empty,
	/// or whitespace-only name is never considered present.
	/// </remarks>
	/// <param name="modifier">
	/// The modifier name to check (for example <see cref="TextSemanticTokenModifiers.Deprecated"/>).
	/// </param>
	/// <returns>
	/// <see langword="true"/> if the modifier is present; otherwise, <see langword="false"/>.
	/// </returns>
	public bool HasModifier(string? modifier)
	{
		string? normalizedModifier = modifier?.Trim();

		if (string.IsNullOrEmpty(normalizedModifier) || _modifiers.Count == 0)
			return false;

		for (int i = 0; i < _modifiers.Count; i++)
		{
			if (string.Equals(_modifiers[i], normalizedModifier, StringComparison.Ordinal))
				return true;
		}

		return false;
	}

	/// <inheritdoc/>
	public bool Equals(TextSemanticToken? other)
	{
		if (other is null || Range != other.Range || !string.Equals(Type, other.Type, StringComparison.Ordinal))
			return false;

		ReadOnlyCollection<string> otherModifiers = other._modifiers;

		if (_modifiers.Count != otherModifiers.Count)
			return false;

		for (int i = 0; i < _modifiers.Count; i++)
		{
			if (!string.Equals(_modifiers[i], otherModifiers[i], StringComparison.Ordinal))
				return false;
		}

		return true;
	}

	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is TextSemanticToken other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Range);
		hash.Add(Type, StringComparer.Ordinal);

		for (int i = 0; i < _modifiers.Count; i++)
			hash.Add(_modifiers[i], StringComparer.Ordinal);

		return hash.ToHashCode();
	}

	/// <summary>
	/// Compares two tokens for structural equality.
	/// </summary>
	/// <param name="left">The first token to compare.</param>
	/// <param name="right">The second token to compare.</param>
	/// <returns><see langword="true"/> when the tokens are equivalent; otherwise, <see langword="false"/>.</returns>
	public static bool operator ==(TextSemanticToken? left, TextSemanticToken? right)
		=> ReferenceEquals(left, right) || left is not null && left.Equals(right);

	/// <summary>
	/// Compares two tokens for structural inequality.
	/// </summary>
	/// <param name="left">The first token to compare.</param>
	/// <param name="right">The second token to compare.</param>
	/// <returns><see langword="true"/> when the tokens differ; otherwise, <see langword="false"/>.</returns>
	public static bool operator !=(TextSemanticToken? left, TextSemanticToken? right)
		=> !(left == right);
}
