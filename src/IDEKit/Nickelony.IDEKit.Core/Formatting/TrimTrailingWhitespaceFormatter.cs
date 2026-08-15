using Nickelony.IDEKit.Core.Text;
using System.Text;

namespace Nickelony.IDEKit.Core.Formatting;

/// <summary>
/// Trims trailing spaces and tabs from each line while preserving the original line
/// terminators.
/// </summary>
/// <remarks>
/// <para>
/// LF, CRLF, and lone CR are recognized as line terminators, and a final unterminated line is trimmed
/// without adding a terminator. Only spaces and tabs are removed; other Unicode whitespace
/// characters, such as a non-breaking space at the end of a line, are preserved so content that
/// relies on them is not altered silently. An already-trimmed document is returned unchanged (the same
/// instance), so callers can detect a no-op by reference.
/// </para>
/// <para>
/// Some formats give trailing whitespace meaning (Markdown hard line breaks are two trailing
/// spaces, for example); documents in those formats must not be trimmed, so a host selects this
/// formatter per document type instead of applying it unconditionally.
/// </para>
/// </remarks>
public sealed class TrimTrailingWhitespaceFormatter : ITextDocumentFormatter
{
	/// <summary>
	/// Gets the shared instance of the formatter.
	/// </summary>
	public static TrimTrailingWhitespaceFormatter Instance { get; } = new();

	private TrimTrailingWhitespaceFormatter()
	{ }

	/// <inheritdoc/>
	public string FormatDocument(string content)
	{
		ArgumentNullException.ThrowIfNull(content);

		// One pass builds the result and detects the no-op: the builder is created only when the first
		// line that needs trimming is reached, so an already-trimmed document is returned by reference
		// without a second enumeration.
		StringBuilder? builder = null;
		var enumerator = new TextLineEnumerator(content);

		while (enumerator.MoveNext())
		{
			ReadOnlySpan<char> line = enumerator.Content;

			if (!EndsInSpaceOrTab(line))
			{
				builder?.Append(line);
				builder?.Append(enumerator.Terminator);
				continue;
			}

			// The first trimmed line starts the rewrite: the lines before it are unchanged, so they are
			// copied as one prefix instead of being appended line by line.
			if (builder is null)
			{
				builder = new StringBuilder(content.Length);
				builder.Append(content.AsSpan(0, enumerator.StartOffset));
			}

			builder.Append(line.TrimEnd(" \t"));
			builder.Append(enumerator.Terminator);
		}

		return builder?.ToString() ?? content;
	}

	/// <summary>
	/// Determines whether the line ends in a space or tab, the only characters this formatter trims.
	/// </summary>
	private static bool EndsInSpaceOrTab(ReadOnlySpan<char> line)
		=> line.Length > 0 && WhitespaceScan.IsSpaceOrTab(line[^1]);
}
