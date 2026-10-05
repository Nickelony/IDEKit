using System.Text.RegularExpressions;
using Nickelony.IDEKit.Core.FindReplace;

namespace Nickelony.Samples;

/// <summary>What a search looks for.</summary>
/// <param name="FindText">The text or the regular expression to find.</param>
/// <param name="UseRegex">Whether <paramref name="FindText"/> is a regular expression.</param>
/// <param name="MatchCase">Whether the search distinguishes upper and lower case.</param>
/// <param name="MatchWholeWord">Whether a match must be a whole word.</param>
internal sealed record SampleSearchOptions(string FindText, bool UseRegex = false, bool MatchCase = false, bool MatchWholeWord = false)
{
	internal SearchQuery ToQuery()
	{
		return new SearchQuery(
			FindReplaceOperations.BuildPattern(FindText, UseRegex, MatchWholeWord),
			FindReplaceOperations.BuildRegexOptions(MatchCase),
			TimeSpan.FromSeconds(1)
		);
	}
}
