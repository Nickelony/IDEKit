namespace Nickelony.IDEKit.Core.Text;

/// <summary>A read-only random-access view of UTF-16 text: its length and the character at an offset.</summary>
/// <remarks>
/// A scanner that needs only character access takes this contract, so a snapshot, an editor document, a string or a memory slice can be scanned
/// without copying it. A generic scanner takes the source by its type parameter, so a struct adapter is read without boxing.
/// </remarks>
public interface ITextCharSource
{
	/// <summary>The number of UTF-16 code units in the text.</summary>
	int TextLength { get; }

	/// <summary>Returns the character at the specified zero-based UTF-16 offset.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative or not less than <see cref="TextLength"/>.</exception>
	char GetCharAt(int offset);
}
