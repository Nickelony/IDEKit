using System.Runtime.CompilerServices;
using System.Text;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>Converts the leading whitespace of one line, which both the text operations and the planner of the indentation share.</summary>
/// <remarks>
/// Each conversion returns the new leading whitespace, or <see langword="null"/> when the line keeps the whitespace it has, so a caller allocates
/// and edits only the lines that change. Only spaces and tabs count as leading whitespace.
/// </remarks>
internal static class LeadingIndentation
{
	/// <summary>Throws when a tab size or a level size is not positive or exceeds <see cref="IndentationOperations.MaximumSize"/>.</summary>
	internal static void ValidateSize(int size, [CallerArgumentExpression(nameof(size))] string? parameterName = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size, parameterName);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(size, IndentationOperations.MaximumSize, parameterName);
	}

	/// <summary>Returns the length of the run of spaces and tabs at the start of the line.</summary>
	internal static int Measure(ReadOnlySpan<char> line)
	{
		int length = 0;

		while (length < line.Length && WhitespaceScan.IsSpaceOrTab(line[length]))
			length++;

		return length;
	}

	/// <summary>Converts each tab of the indentation to the spaces up to its tab stop.</summary>
	internal static string? ToSpaces(ReadOnlySpan<char> indentation, int tabSize)
	{
		int column = 0;
		bool hasTab = false;

		foreach (char character in indentation)
		{
			if (character == '\t')
			{
				hasTab = true;
				column = ((column / tabSize) + 1) * tabSize;
			}
			else
			{
				column++;
			}
		}

		return hasTab ? new string(' ', column) : null;
	}

	/// <summary>
	/// Converts each group of spaces that fills a tab stop to a tab, and keeps existing tabs and the spaces that do not fill a stop.
	/// </summary>
	/// <remarks>
	/// A run of spaces always starts at a tab stop, because it follows a tab or the start of the line, so the number of tabs is the run length
	/// divided by the tab size and the rest stays as spaces.
	/// </remarks>
	internal static string? ToTabs(ReadOnlySpan<char> indentation, int tabSize)
	{
		StringBuilder? builder = null;

		for (int index = 0; index < indentation.Length; )
		{
			if (indentation[index] != ' ')
			{
				builder?.Append(indentation[index]);
				index++;

				continue;
			}

			int runStart = index;

			while (index < indentation.Length && indentation[index] == ' ')
				index++;

			int spaceCount = index - runStart;
			int tabCount = spaceCount / tabSize;

			// The builder starts at the first conversion, so the unchanged prefix stays implicit until then.
			if (tabCount > 0)
				(builder ??= new StringBuilder().Append(indentation[..runStart])).Append('\t', tabCount);

			builder?.Append(' ', spaceCount % tabSize);
		}

		return builder?.ToString();
	}

	/// <summary>Gives each whole level of a space indentation the new size and keeps the remainder; an indentation with a tab stays.</summary>
	internal static string? Resize(ReadOnlySpan<char> indentation, int currentSize, int newSize)
	{
		if (indentation.Contains('\t'))
			return null;

		int resizedLength = (indentation.Length / currentSize * newSize) + (indentation.Length % currentSize);

		return resizedLength == indentation.Length ? null : new string(' ', resizedLength);
	}
}
