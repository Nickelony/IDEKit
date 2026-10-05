namespace Nickelony.IDEKit.Core.Text;

/// <summary>Represents a single line within an <see cref="ITextSnapshot"/>.</summary>
/// <remarks>
/// Offsets are zero-based and line numbers are one-based.
/// </remarks>
public interface ITextLine
{
	/// <summary>The zero-based offset of the first character of this line within the source text.</summary>
	int Offset { get; }

	/// <summary>The length of the line text, excluding any line terminator.</summary>
	int Length { get; }

	/// <summary>The zero-based offset of the first character after this line's text.</summary>
	int EndOffset { get; }

	/// <summary>The one-based line number of this line within the source text.</summary>
	int LineNumber { get; }
}
