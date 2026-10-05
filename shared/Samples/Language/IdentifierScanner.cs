using System.Text.RegularExpressions;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.Samples;

/// <summary>Finds the identifiers of a text with a regular expression, which is all the sample language needs.</summary>
internal static partial class IdentifierScanner
{
	internal static IEnumerable<string> FindIdentifiers(string text) =>
		IdentifierPattern().Matches(text).Select(match => match.Value).Distinct(StringComparer.Ordinal);

	/// <summary>Finds the dotted name that contains the offset, or <see langword="null"/> when the offset is not inside a name.</summary>
	internal static TextRange? FindName(string text, int offset)
	{
		foreach (Match match in IdentifierPattern().Matches(text))
		{
			if (offset >= match.Index && offset <= match.Index + match.Length)
				return new TextRange(match.Index, match.Length);
		}

		return null;
	}

	/// <summary>Finds every occurrence of a dotted name, matching whole names only.</summary>
	internal static IEnumerable<TextRange> FindOccurrences(string text, string name)
	{
		foreach (Match match in IdentifierPattern().Matches(text))
		{
			if (string.Equals(match.Value, name, StringComparison.Ordinal))
				yield return new TextRange(match.Index, match.Length);
		}
	}

	[GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*", RegexOptions.CultureInvariant)]
	private static partial Regex IdentifierPattern();
}
