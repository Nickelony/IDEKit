namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Owns the narrow space/tab predicate and describes the two whitespace alphabets used across Core.
/// </summary>
/// <remarks>
/// <para>
/// The indentation and trailing-whitespace primitives recognize only spaces and tabs, so unusual
/// Unicode whitespace is never consumed or removed silently; use <see cref="IsSpaceOrTab"/> for that
/// alphabet.
/// </para>
/// <para>
/// Blankness checks, comment scanning, and caret fallback resolution follow
/// <see cref="char.IsWhiteSpace(char)"/>, so any Unicode whitespace counts; call that predicate
/// directly for that alphabet. Do not mix the two alphabets within one operation.
/// </para>
/// </remarks>
internal static class WhitespaceScan
{
	/// <summary>
	/// Determines whether the character is a space or a tab, the narrow alphabet of the indentation
	/// and trailing-whitespace primitives.
	/// </summary>
	/// <param name="character">The character to inspect.</param>
	/// <returns><see langword="true"/> when the character is a space or a tab.</returns>
	internal static bool IsSpaceOrTab(char character) => character is ' ' or '\t';
}
