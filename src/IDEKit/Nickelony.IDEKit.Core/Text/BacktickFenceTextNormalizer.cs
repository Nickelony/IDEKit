namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Normalizes markup text that documents code so it reads as plain text.
/// </summary>
/// <remarks>
/// <para>
/// Only backtick fences are recognized: tilde fences and indented (four-space) code blocks are not
/// special-cased, and fence lines are recognized at any indentation; inline backticks are
/// preserved.
/// </para>
/// <para>
/// The line terminator used to join the retained lines is supplied by the caller, so the type makes
/// no assumption about the host's newline convention.
/// </para>
/// </remarks>
public static class BacktickFenceTextNormalizer
{
	/// <summary>
	/// Removes paired Markdown fence lines while preserving inline backticks and code content,
	/// including fence-like lines nested inside a longer fence.
	/// </summary>
	/// <remarks>
	/// LF, CRLF, and lone CR line terminators are recognized. Trailing whitespace on retained lines is
	/// removed; blank lines at the start and end of the result are dropped, while the indentation of
	/// the retained lines is preserved exactly. A fence line is a run of at least three backticks
	/// followed by an optional info string that contains no further backticks. An opening fence line
	/// is removed, and only a fence of at least the same run length without an info string closes it,
	/// so a shorter or annotated fence-like line inside a fence stays part of the code content.
	/// </remarks>
	/// <param name="text">The text to normalize.</param>
	/// <param name="newLine">The line terminator used to join the retained lines.</param>
	/// <returns>
	/// The normalized text using <paramref name="newLine"/>, or <see langword="null"/> when the input
	/// or result is blank.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="newLine"/> is <see langword="null"/>.</exception>
	public static string? NormalizeForPlainText(string? text, string newLine)
	{
		ArgumentNullException.ThrowIfNull(newLine);

		if (string.IsNullOrWhiteSpace(text))
			return null;

		var normalizedLines = new List<string>();
		var enumerator = new TextLineEnumerator(text);
		int openFenceBacktickCount = 0;

		while (enumerator.MoveNext())
		{
			ReadOnlySpan<char> line = enumerator.Content.TrimEnd();

			if (IsFenceLine(line, out int fenceBacktickCount, out bool hasInfoString))
			{
				if (openFenceBacktickCount == 0)
				{
					// An opening fence line is removed; its run length decides which later fence-like
					// lines can close it.
					openFenceBacktickCount = fenceBacktickCount;
					continue;
				}

				// A fence-like line closes the open fence only when it is at least as long and carries
				// no info string (the CommonMark closing rule); anything else stays content.
				if (!hasInfoString && fenceBacktickCount >= openFenceBacktickCount)
				{
					openFenceBacktickCount = 0;
					continue;
				}
			}

			normalizedLines.Add(line.ToString());
		}

		// Drop only blank leading and trailing lines; trimming the joined result would also remove the
		// indentation of the first retained line while later lines keep theirs.
		int start = 0;
		int end = normalizedLines.Count;

		while (start < end && normalizedLines[start].Length == 0)
			start++;

		while (end > start && normalizedLines[end - 1].Length == 0)
			end--;

		if (start >= end)
			return null;

		// GetRange copies the retained slice; the target framework exposes no span-based string.Join
		// overload, so the copy is kept.
		return string.Join(newLine, normalizedLines.GetRange(start, end - start));
	}

	private static bool IsFenceLine(ReadOnlySpan<char> line, out int backtickCount, out bool hasInfoString)
	{
		ReadOnlySpan<char> trimmedLine = line.Trim();

		// A fence line starts with a run of at least three backticks; the optional info string
		// that follows may contain any character except another backtick.
		backtickCount = 0;

		while (backtickCount < trimmedLine.Length && trimmedLine[backtickCount] == '`')
			backtickCount++;

		hasInfoString = backtickCount >= 3 && trimmedLine[backtickCount..].Length > 0;

		return backtickCount >= 3
			&& trimmedLine[backtickCount..].IndexOf('`') < 0;
	}
}
