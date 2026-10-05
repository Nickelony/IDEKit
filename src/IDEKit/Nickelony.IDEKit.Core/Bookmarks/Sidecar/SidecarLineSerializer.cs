using System.Globalization;
using System.Text;

namespace Nickelony.IDEKit.Core.Bookmarks;

/// <summary>
/// Converts the ordered line numbers of a sidecar file to and from their text form: one invariant-culture line number per line,
/// each terminated by <c>\n</c>.
/// </summary>
/// <remarks>
/// The format is byte-identical on every platform (invariant culture, one <c>\n</c> terminator, UTF-8 without a byte-order mark),
/// and the caller supplies de-duplicated, ascending numbers. Reading is tolerant: non-integers and values below one are skipped, and the result
/// is distinct and ascending, so a hand-edited sidecar restores the shape <see cref="Serialize"/> writes; parsing accepts an optional sign
/// and surrounding whitespace.
/// </remarks>
internal static class SidecarLineSerializer
{
	/// <summary>Builds the file content: one invariant-culture line number per line, each terminated by <c>\n</c>.</summary>
	/// <param name="orderedLines">The de-duplicated, ascending line numbers to write.</param>
	/// <returns>The file content.</returns>
	internal static string Serialize(IReadOnlyList<int> orderedLines)
	{
		var builder = new StringBuilder(orderedLines.Count * 8);

		foreach (int lineNumber in orderedLines)
			builder.Append(lineNumber.ToString(CultureInfo.InvariantCulture)).Append('\n');

		return builder.ToString();
	}

	/// <summary>Parses the file lines into the distinct, ascending line numbers they contain.</summary>
	/// <param name="lines">The file lines to parse, as returned by <c>File.ReadAllLines</c>.</param>
	/// <returns>The distinct, ascending line numbers; empty when no line holds a valid number.</returns>
	internal static IReadOnlyList<int> Deserialize(IReadOnlyList<string> lines)
	{
		var parsedLines = new SortedSet<int>();

		foreach (string line in lines)
		{
			if (
				int.TryParse(s: line, style: NumberStyles.Integer, provider: CultureInfo.InvariantCulture, result: out int lineNumber)
				&& lineNumber >= 1
			)
			{
				parsedLines.Add(lineNumber);
			}
		}

		return [.. parsedLines];
	}
}
