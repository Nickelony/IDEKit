using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Highlighting;

/// <summary>Represents a highlighting rule that applies a <see cref="RegexHighlightingStyle"/> to text matched by a regular expression.</summary>
/// <remarks>
/// The rule is evaluated per line while the editor renders, so use a <see cref="Regex"/>
/// with a bounded match timeout. When that timeout fires, the resulting <see cref="RegexMatchTimeoutException"/> surfaces in the editor's colorizer
/// on the UI thread, where no layer above the engine catches it. The pattern must not match empty text or use <see cref="RegexOptions.RightToLeft"/>;
/// see <see cref="RegexHighlightingRuleSet"/>.
/// </remarks>
public sealed class RegexHighlightingRule
{
	/// <exception cref="ArgumentNullException"><paramref name="pattern"/> or <paramref name="style"/> is <see langword="null"/>.</exception>
	public RegexHighlightingRule(Regex pattern, RegexHighlightingStyle style)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		ArgumentNullException.ThrowIfNull(style);

		Pattern = pattern;
		Style = style;
	}

	public Regex Pattern { get; }

	public RegexHighlightingStyle Style { get; }
}
