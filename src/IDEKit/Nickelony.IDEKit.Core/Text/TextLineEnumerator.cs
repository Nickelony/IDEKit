namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Enumerates the lines of a text span, recognizing LF, CRLF, and lone CR line terminators.
/// </summary>
/// <remarks>
/// Each line exposes its content and its line terminator separately, so callers that preserve the
/// original terminators and callers that ignore them share one implementation. The final line is
/// always yielded, with an empty terminator when the text does not end with a line terminator; an
/// empty text therefore yields a single empty line. Before the first <see cref="MoveNext"/> call,
/// the member state is <see cref="StartOffset"/> = <c>0</c> and empty <see cref="Content"/> and
/// <see cref="Terminator"/> spans.
/// </remarks>
internal ref struct TextLineEnumerator
{
	private readonly ReadOnlySpan<char> _text;

	private int _position;
	private int _contentStart;
	private int _contentLength;
	private int _terminatorLength;
	private bool _finished;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextLineEnumerator"/> struct.
	/// </summary>
	/// <param name="text">The text to enumerate.</param>
	public TextLineEnumerator(ReadOnlySpan<char> text)
	{
		_text = text;
		_position = 0;
		_contentStart = 0;
		_contentLength = 0;
		_terminatorLength = 0;
		_finished = false;
	}

	/// <summary>
	/// Gets the zero-based offset of the current line within the text.
	/// </summary>
	public readonly int StartOffset => _contentStart;

	/// <summary>
	/// Gets the current line content, excluding its line terminator.
	/// </summary>
	public readonly ReadOnlySpan<char> Content
		=> _text.Slice(_contentStart, _contentLength);

	/// <summary>
	/// Gets the current line terminator, or an empty span for an unterminated final line.
	/// </summary>
	public readonly ReadOnlySpan<char> Terminator
		=> _text.Slice(_contentStart + _contentLength, _terminatorLength);

	/// <summary>
	/// Advances to the next line.
	/// </summary>
	/// <returns><see langword="true"/> while a line is available; otherwise, <see langword="false"/>.</returns>
	public bool MoveNext()
	{
		if (_finished)
			return false;

		_contentStart = _position;

		// A vectorized scan replaces the scalar per-character loop for large single-line documents.
		int terminatorIndex = _text[_contentStart..].IndexOfAny('\r', '\n');
		int lineEnd = terminatorIndex < 0 ? _text.Length : _contentStart + terminatorIndex;

		_contentLength = lineEnd - _contentStart;

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
