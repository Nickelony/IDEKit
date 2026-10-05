using System.Text;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Formatting;

/// <summary>Trims trailing spaces and tabs from each line while preserving the original line terminators.</summary>
/// <remarks>
/// LF, CRLF, and lone CR terminate lines, and a final unterminated line is trimmed without adding one. Only spaces and tabs are removed,
/// so other Unicode whitespace survives, and an already-trimmed document is returned as the same instance. A host applies this formatter per document
/// type and skips formats where trailing whitespace carries meaning, such as Markdown hard line breaks.
/// </remarks>
public sealed class TrimTrailingWhitespaceFormatter : IDocumentFormatter
{
	public static TrimTrailingWhitespaceFormatter Instance { get; } = new();

	private TrimTrailingWhitespaceFormatter() { }

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

	private static bool EndsInSpaceOrTab(ReadOnlySpan<char> line) => line.Length > 0 && WhitespaceScan.IsSpaceOrTab(line[^1]);
}
