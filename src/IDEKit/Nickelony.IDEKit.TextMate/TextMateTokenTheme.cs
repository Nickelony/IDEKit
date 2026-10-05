namespace Nickelony.IDEKit.TextMate;

/// <summary>Provides the token styling rules that resolve TextMate scopes into visual styles.</summary>
/// <remarks>
/// Record equality compares the assigned rule collection by reference rather than the rules by value; two themes with equal rules are not equal
/// unless they expose the same collection instance.
/// </remarks>
public sealed record TextMateTokenTheme
{
	private IReadOnlyList<TextMateTokenThemeRule> _rules = [];

	/// <summary>The token styling rules of the theme.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The assigned collection is copied, but its rule objects are not cloned. Rules that repeat the same selector merge with later
	/// values overwriting earlier ones for each property the later rule sets, distinct rules of equal specificity resolve in selector order
	/// rather than theme order, and a rule with a blank scope provides the defaults that apply to every token.
	/// </remarks>
	public IReadOnlyList<TextMateTokenThemeRule> Rules
	{
		get => _rules;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_rules = Array.AsReadOnly([.. value]);
		}
	}
}
