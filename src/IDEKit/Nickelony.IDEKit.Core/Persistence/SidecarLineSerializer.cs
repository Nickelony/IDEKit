using System.Globalization;
using System.Text;

namespace Nickelony.IDEKit.Core.Persistence;

/// <summary>
/// Converts the ordered line numbers of a sidecar file to and from their text form: one
/// invariant-culture line number per line, each terminated by <c>\n</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the serialization seam of <see cref="SidecarLineFile"/>: the file kernel owns the
/// temporary-file replace and the failure mapping, while the format lives here, so the two concerns
/// never drift and the format can be exercised without a file system. The format is byte-identical on
/// every platform (invariant culture, a single <c>\n</c> terminator, written as UTF-8 without a
/// byte-order mark), and the numbers are de-duplicated and ordered ascending by the caller.
/// </para>
/// <para>
/// Reading is tolerant: entries that are not integers and values below one are skipped, and the result
/// is distinct and ordered ascending, so a hand-edited sidecar restores the shape
/// <see cref="Serialize"/> writes. Parsing is invariant-culture and accepts an optional sign and
/// surrounding whitespace.
/// </para>
/// </remarks>
internal static class SidecarLineSerializer
{
	/// <summary>
	/// Builds the file content: one invariant-culture line number per line, each terminated by
	/// <c>\n</c>, so the file is byte-identical on every platform.
	/// </summary>
	/// <param name="orderedLines">The de-duplicated, ascending line numbers to write.</param>
	/// <returns>The file content.</returns>
	internal static string Serialize(IReadOnlyList<int> orderedLines)
	{
		var builder = new StringBuilder(orderedLines.Count * 8);

		foreach (int lineNumber in orderedLines)
			builder.Append(lineNumber.ToString(CultureInfo.InvariantCulture)).Append('\n');

		return builder.ToString();
	}

	/// <summary>
	/// Parses the file lines into the distinct, ascending line numbers they contain.
	/// </summary>
	/// <param name="lines">The file lines to parse, as returned by <c>File.ReadAllLines</c>.</param>
	/// <returns>
	/// The distinct, ascending line numbers; empty when no line holds a valid number.
	/// </returns>
	internal static IReadOnlyList<int> Deserialize(IReadOnlyList<string> lines)
	{
		var parsedLines = new SortedSet<int>();

		foreach (string line in lines)
		{
			if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lineNumber) && lineNumber >= 1)
				parsedLines.Add(lineNumber);
		}

		return [.. parsedLines];
	}
}
