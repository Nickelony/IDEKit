#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
#endif

/// <summary>
/// Provides the token styling rules used to resolve TextMate scopes into visual styles.
/// </summary>
/// <remarks>
/// Record equality compares the assigned rule collection by reference rather than the rules by value;
/// two themes with equal rules are not equal unless they expose the same collection instance.
/// </remarks>
public sealed record TextMateTokenTheme
{
	private IReadOnlyList<TextMateTokenThemeRule> _rules = [];

	/// <summary>
	/// Gets or initializes the token styling rules of the theme.
	/// </summary>
	/// <remarks>
	/// The assigned collection is copied, but its rule objects are not cloned. Rules that repeat the
	/// same selector merge with later values overwriting earlier ones for each property the later rule
	/// sets, distinct rules of equal specificity resolve in selector order, and a rule with a blank
	/// scope provides the defaults that apply to every token.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="value"/> is <see langword="null"/>.
	/// </exception>
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
