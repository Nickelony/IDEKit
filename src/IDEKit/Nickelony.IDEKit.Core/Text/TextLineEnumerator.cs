namespace Nickelony.IDEKit.Core.Text;

/// <summary>Enumerates the lines of a text span, recognizing LF, CRLF, and lone CR line terminators.</summary>
/// <remarks>
/// Each line exposes its content and its line terminator separately. The final line is always yielded, with an empty
/// terminator when the text does not end with one, so an empty text yields a single empty line. Before the first <see cref="MoveNext"/> call,
/// <see cref="StartOffset"/> is <c>0</c> and <see cref="Content"/> and <see cref="Terminator"/> are empty spans.
/// </remarks>
internal ref struct TextLineEnumerator
{
	private readonly ReadOnlySpan<char> _text;

	private int _position;
	private int _contentLength;
	private int _terminatorLength;

	private bool _finished;

	/// <param name="text">The text to enumerate.</param>
	internal TextLineEnumerator(ReadOnlySpan<char> text)
	{
		_text = text;
		_position = 0;
		StartOffset = 0;
		_contentLength = 0;
		_terminatorLength = 0;
		_finished = false;
	}

	/// <summary>The zero-based offset of the current line within the text.</summary>
	internal int StartOffset { get; private set; }

	/// <summary>The current line content, excluding its line terminator.</summary>
	internal readonly ReadOnlySpan<char> Content => _text.Slice(StartOffset, _contentLength);

	/// <summary>The current line terminator, or an empty span for an unterminated final line.</summary>
	internal readonly ReadOnlySpan<char> Terminator => _text.Slice(StartOffset + _contentLength, _terminatorLength);

	/// <summary>Advances to the next line.</summary>
	/// <returns><see langword="true"/> while a line is available; otherwise, <see langword="false"/>.</returns>
	internal bool MoveNext()
	{
		if (_finished)
			return false;

		StartOffset = _position;

		// IndexOfAny vectorizes the scan, which keeps large single-line documents fast.
		int terminatorIndex = _text[StartOffset..].IndexOfAny('\r', '\n');
		int lineEnd = terminatorIndex < 0 ? _text.Length : StartOffset + terminatorIndex;

		_contentLength = lineEnd - StartOffset;

		if (lineEnd >= _text.Length)
		{
			_terminatorLength = 0;
			_position = lineEnd;
			_finished = true;

			return true;
		}

		// Treat CRLF as one line terminator while preserving standalone CR and LF.
		_terminatorLength = LineTerminators.GetTerminatorLength(_text, lineEnd);

		_position = lineEnd + _terminatorLength;

		return true;
	}
}
