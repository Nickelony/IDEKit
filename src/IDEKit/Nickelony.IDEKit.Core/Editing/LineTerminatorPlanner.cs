using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Plans the edits that give every line terminator of a snapshot one style.</summary>
/// <remarks>
/// An edit replaces one line terminator and nothing else, so the content of the lines never changes. A terminator that already has the style
/// gets no edit, and a plan for a snapshot that needs no change is empty. LF, CRLF and lone CR are line terminators, and CRLF counts as one.
/// </remarks>
public static class LineTerminatorPlanner
{
	/// <summary>Plans the replacement of every line terminator that differs from <paramref name="style"/>.</summary>
	/// <param name="snapshot">The text to plan against.</param>
	/// <param name="style">
	/// The style every line terminator takes: <see cref="LineTerminatorStyle.Lf"/>,
	/// <see cref="LineTerminatorStyle.CrLf"/> or <see cref="LineTerminatorStyle.Cr"/>.
	/// </param>
	/// <returns>One edit per line terminator of another style, in text order.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="style"/> is <see cref="LineTerminatorStyle.Mixed"/> or <see cref="LineTerminatorStyle.None"/>,
	/// which name no single terminator.
	/// </exception>
	public static IReadOnlyList<TextIncrementalEdit> PlanConversion(ITextSnapshot snapshot, LineTerminatorStyle style)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		string terminator = LineTerminators.GetText(style) ?? throw new ArgumentException($"{style} names no single line terminator.", nameof(style));
		List<TextIncrementalEdit> edits = [];

		foreach (ITextLine line in snapshot.Lines)
		{
			int length = LineTerminators.GetTerminatorLength(snapshot, line.EndOffset);

			if (length == 0 || IsTerminator(snapshot, offset: line.EndOffset, length, terminator))
				continue;

			edits.Add(new TextIncrementalEdit(new TextRange(line.EndOffset, length), terminator));
		}

		return edits;
	}

	private static bool IsTerminator(ITextSnapshot snapshot, int offset, int length, string terminator)
	{
		if (length != terminator.Length)
			return false;

		for (int index = 0; index < length; index++)
		{
			if (snapshot.GetCharAt(offset + index) != terminator[index])
				return false;
		}

		return true;
	}
}
